using System.Collections.Concurrent;

namespace BrutalShore.World;

/// <summary>Builds chunk meshes on worker threads so the frame only has to upload them.</summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="ChunkStreamer" /> and for the same reason, one step further along
/// the pipeline: a chunk arrives, it is meshed, and the mesh is uploaded. Generating and meshing
/// are the two halves of making a chunk appear, and neither needs the GPU — so neither belongs on
/// the thread that drives it.
/// </para>
/// <para>
/// What a worker reads is a <see cref="ChunkNeighbourhood" />, captured on the main thread, never
/// the world itself. What it writes is a <see cref="ChunkSurface" /> nobody else holds. Both the
/// surfaces and the mesher's own scratch buffers are kept and reused, because a chunk's geometry
/// is tens of kilobytes and a world being flown across produces hundreds a second.
/// </para>
/// <para>
/// A worker lights a chunk before it meshes it. The two belong together: light is wanted per
/// corner of every face and nowhere else, so working it out here and baking it into the vertices
/// means neither the frame nor a resident chunk ever has to hold it.
/// </para>
/// <para>
/// A surface handed to <see cref="Collect" /> is only the caller's for the duration of the call:
/// it goes back into the pool afterwards. That is what keeps the pool from needing a return call
/// a caller can forget, and it costs nothing, because the only thing done with a finished surface
/// is to copy it into GPU buffers.
/// </para>
/// </remarks>
public sealed class MeshStreamer : IDisposable
{
    private readonly BlockRegistry _registry;
    private readonly BlockingCollection<ChunkNeighbourhood> _requested = new();
    private readonly ConcurrentQueue<Finished> _finished = new();
    private readonly ConcurrentBag<ChunkSurface> _spare = [];
    private readonly HashSet<int3> _outstanding = [];
    private readonly Thread[] _workers;

    private bool _stopped;

    /// <summary>Starts the workers.</summary>
    /// <param name="registry">Where a block id's colour is resolved; read only, from every worker.</param>
    /// <param name="workerCount">How many threads to mesh on; at least one.</param>
    /// <param name="queueCapacity">How many chunks may be waiting to be meshed; at least one.</param>
    /// <exception cref="ArgumentOutOfRangeException">A count is not positive.</exception>
    public MeshStreamer(BlockRegistry registry, int workerCount, int queueCapacity)
    {
        if (workerCount < 1)
            throw new ArgumentOutOfRangeException(nameof(workerCount), workerCount, "A streamer needs a worker.");

        if (queueCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(queueCapacity), queueCapacity, "A streamer needs room to queue.");

        _registry = registry;
        QueueCapacity = queueCapacity;
        _workers = new Thread[workerCount];

        for (var index = 0; index < workerCount; index++)
        {
            _workers[index] = new Thread(Work)
            {
                IsBackground = true,
                Name = $"chunk mesher {index}",
            };
            _workers[index].Start();
        }
    }

    /// <summary>How many threads are meshing.</summary>
    public int WorkerCount => _workers.Length;

    /// <summary>How many chunks may be waiting to be meshed at once.</summary>
    public int QueueCapacity { get; }

    /// <summary>How many chunks have been asked for and not yet collected.</summary>
    public int OutstandingCount => _outstanding.Count;

    /// <summary>How many surfaces are finished and waiting to be uploaded.</summary>
    public int ReadyCount => _finished.Count;

    /// <summary>Asks for a chunk's surface to be built.</summary>
    /// <param name="neighbourhood">The chunk and its neighbours, captured on this thread.</param>
    /// <returns>
    /// <see langword="true" /> if this call added the request; <see langword="false" /> if that
    /// chunk is already being meshed, or if the queue is full.
    /// </returns>
    /// <remarks>
    /// Main thread only. A chunk that changes again before its mesh arrives is simply refused
    /// here and stays dirty, so it is asked for again once the one in flight has landed.
    /// </remarks>
    public bool Request(ChunkNeighbourhood neighbourhood)
    {
        if (_stopped || _outstanding.Count >= QueueCapacity || !_outstanding.Add(neighbourhood.Centre))
            return false;

        _requested.Add(neighbourhood);
        return true;
    }

    /// <summary>Hands over the meshes that are ready.</summary>
    /// <param name="maxSurfaces">How many to take at most.</param>
    /// <param name="accept">
    /// What to do with each. The surface is valid only for the duration of the call; it is reused
    /// afterwards, so anything wanted from it has to be copied out.
    /// </param>
    /// <returns>How many were handed over.</returns>
    /// <remarks>Main thread only.</remarks>
    public int Collect(int maxSurfaces, Action<int3, ChunkSurface> accept)
    {
        var taken = 0;
        while (taken < maxSurfaces && _finished.TryDequeue(out var ready))
        {
            _outstanding.Remove(ready.Coordinate);

            try
            {
                accept(ready.Coordinate, ready.Surface);
            }
            finally
            {
                _spare.Add(ready.Surface);
            }

            taken++;
        }

        return taken;
    }

    /// <summary>Stops the workers and waits for the one chunk each is part way through.</summary>
    public void Dispose()
    {
        if (_stopped)
            return;

        _stopped = true;
        _requested.CompleteAdding();

        foreach (var worker in _workers)
            worker.Join();

        _requested.Dispose();
    }

    private void Work()
    {
        // One of each per worker: both keep scratch buffers between calls, so sharing either
        // would be two threads writing the same array.
        var mesher = new ChunkMesher();
        var light = new LightVolume();

        try
        {
            foreach (var neighbourhood in _requested.GetConsumingEnumerable())
            {
                if (!_spare.TryTake(out var surface))
                    surface = new ChunkSurface();

                light.Illuminate(neighbourhood.Centre, neighbourhood, _registry, neighbourhood);
                mesher.Mesh(neighbourhood.Middle, neighbourhood.Centre, neighbourhood, _registry, light, surface);
                _finished.Enqueue(new Finished(neighbourhood.Centre, surface));
            }
        }
        catch (ObjectDisposedException)
        {
            // Disposed from under us during shutdown; there is nothing left to mesh for.
        }
    }

    private readonly record struct Finished(int3 Coordinate, ChunkSurface Surface);
}
