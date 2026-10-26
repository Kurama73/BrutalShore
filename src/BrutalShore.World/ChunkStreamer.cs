using System.Collections.Concurrent;

namespace BrutalShore.World;

/// <summary>Generates chunks on worker threads so the frame does not wait for them.</summary>
/// <remarks>
/// <para>
/// Generation is the expensive half of streaming — several noise fields per column — and it is
/// also the half that needs nothing but a coordinate and a seed. That is what makes it the piece
/// worth moving off the frame: a worker touches no shared state at all, because the chunk it
/// fills is one nobody else has seen yet.
/// </para>
/// <para>
/// Nothing here touches the world. A finished chunk waits in a queue until the main thread asks
/// for it, and only the main thread ever inserts one. That keeps the world's own map free of
/// locks and makes the ordering easy to reason about: a chunk exists, or it does not, and it only
/// changes between frames.
/// </para>
/// <para>
/// Requests are capped rather than unbounded. A player sprinting in a straight line can ask for
/// chunks faster than they can be made, and a queue that grew would spend the workers on ground
/// already behind them.
/// </para>
/// </remarks>
public sealed class ChunkStreamer : IDisposable
{
    private readonly IChunkGenerator _generator;
    private readonly BlockingCollection<int3> _requested = new();
    private readonly ConcurrentQueue<Finished> _finished = new();
    private readonly HashSet<int3> _outstanding = [];
    private readonly Thread[] _workers;

    private bool _stopped;

    /// <summary>Starts the workers.</summary>
    /// <param name="generator">What makes a chunk; it has to be safe to call from several threads.</param>
    /// <param name="workerCount">How many threads to generate on; at least one.</param>
    /// <param name="queueCapacity">How many requests may be outstanding at once; at least one.</param>
    /// <exception cref="ArgumentOutOfRangeException">A count is not positive.</exception>
    public ChunkStreamer(IChunkGenerator generator, int workerCount, int queueCapacity)
    {
        if (workerCount < 1)
            throw new ArgumentOutOfRangeException(nameof(workerCount), workerCount, "A streamer needs a worker.");

        if (queueCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(queueCapacity), queueCapacity, "A streamer needs room to queue.");

        _generator = generator;
        QueueCapacity = queueCapacity;
        _workers = new Thread[workerCount];

        for (var index = 0; index < workerCount; index++)
        {
            // Background threads, so a crash on the main thread still ends the process rather
            // than leaving it generating terrain nobody will ever see.
            _workers[index] = new Thread(Work)
            {
                IsBackground = true,
                Name = $"chunk generator {index}",
            };
            _workers[index].Start();
        }
    }

    /// <summary>How many threads are generating.</summary>
    public int WorkerCount => _workers.Length;

    /// <summary>How many requests may be outstanding at once.</summary>
    public int QueueCapacity { get; }

    /// <summary>How many chunks have been asked for and not yet collected.</summary>
    public int OutstandingCount => _outstanding.Count;

    /// <summary>How many chunks are finished and waiting to be taken into the world.</summary>
    /// <remarks>
    /// A number that is always climbing means the collection budget is too small for the workers,
    /// and the queue is turning into the memory the streaming was meant to bound.
    /// </remarks>
    public int ReadyCount => _finished.Count;

    /// <summary>Asks for a chunk to be generated.</summary>
    /// <param name="coordinate">Which chunk.</param>
    /// <returns>
    /// <see langword="true" /> if this call added the request; <see langword="false" /> if it was
    /// already asked for, or if the queue is full.
    /// </returns>
    /// <remarks>Main thread only: it is the only one that touches the outstanding set.</remarks>
    public bool Request(int3 coordinate)
    {
        if (_stopped || _outstanding.Count >= QueueCapacity || !_outstanding.Add(coordinate))
            return false;

        _requested.Add(coordinate);
        return true;
    }

    /// <summary>Hands over the chunks that are ready.</summary>
    /// <param name="maxChunks">How many to take at most.</param>
    /// <param name="accept">What to do with each; called on the calling thread.</param>
    /// <returns>How many were handed over.</returns>
    /// <remarks>Main thread only.</remarks>
    public int Collect(int maxChunks, Action<int3, Chunk> accept)
    {
        var taken = 0;
        while (taken < maxChunks && _finished.TryDequeue(out var ready))
        {
            _outstanding.Remove(ready.Coordinate);
            accept(ready.Coordinate, ready.Chunk);
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
        try
        {
            foreach (var coordinate in _requested.GetConsumingEnumerable())
            {
                var chunk = new Chunk();
                _generator.Generate(coordinate, chunk);
                _finished.Enqueue(new Finished(coordinate, chunk));
            }
        }
        catch (ObjectDisposedException)
        {
            // Disposed from under us during shutdown; there is nothing left to generate for.
        }
    }

    private readonly record struct Finished(int3 Coordinate, Chunk Chunk);
}
