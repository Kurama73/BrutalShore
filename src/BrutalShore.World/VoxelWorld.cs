namespace BrutalShore.World;

/// <summary>
/// The world: which chunks are resident, how they are produced, and read access to every block in
/// them.
/// </summary>
/// <remarks>
/// <para>
/// Unbounded horizontally and bounded vertically, which is the shape a voxel game of this kind
/// wants. Horizontal is unbounded because walking should never end. Vertical is bounded because a
/// column of blocks is what terrain generation, lighting and the player's intuition are all built
/// around, and because an unbounded vertical axis turns streaming from a disc into a sphere and
/// multiplies the resident chunk count for sky nobody looks at.
/// </para>
/// <para>
/// A chunk nobody has touched is <em>regenerated</em> rather than stored, which is why unloading one
/// is free and why no save file exists yet. The moment a block is edited, that stops being true for
/// its chunk, and this is the class that will have to know the difference.
/// </para>
/// </remarks>
public sealed class VoxelWorld : IVoxelSource, IDisposable
{
    /// <summary>How many chunks tall the world is.</summary>
    public const int ChunksTall = 8;

    /// <summary>How many blocks tall the world is.</summary>
    public const int HeightBlocks = ChunksTall * Chunk.Size;

    private readonly IChunkGenerator _generator;
    private readonly ChunkStreamer? _streamer;
    private readonly Dictionary<int3, Chunk> _chunks = [];
    private readonly HashSet<int3> _dirty = [];

    // Reused every call: the chunks to unload cannot be removed while the map is being walked,
    // and allocating the list again each frame is a kilobyte of garbage per loaded chunk.
    private readonly List<int3> _leaving = [];

    /// <summary>Creates a world.</summary>
    /// <param name="registry">The block types its chunks are made of.</param>
    /// <param name="generator">What produces a chunk nobody has stored.</param>
    public VoxelWorld(BlockRegistry registry, IChunkGenerator generator)
        : this(registry, generator, streamer: null) { }

    /// <summary>Builds a world whose streaming generates chunks on worker threads.</summary>
    /// <param name="registry">The block types the chunks are made of.</param>
    /// <param name="generator">What makes a chunk nobody has edited.</param>
    /// <param name="streamer">
    /// The workers <see cref="UpdateStreaming" /> hands coordinates to, or <see langword="null" />
    /// to generate on the calling thread. The world takes ownership and disposes it.
    /// </param>
    /// <remarks>
    /// With a streamer, <see cref="UpdateStreaming" /> no longer makes the chunks it asks for; it
    /// asks, and hands over whichever arrived since last time. The chunks that exist afterwards
    /// are the same ones either way, but not in the same call — which is why a test that wants to
    /// assert what a single call loaded uses the other constructor.
    /// </remarks>
    public VoxelWorld(BlockRegistry registry, IChunkGenerator generator, ChunkStreamer? streamer)
    {
        Registry = registry;
        _generator = generator;
        _streamer = streamer;
    }

    /// <summary>The block types this world is made of.</summary>
    public BlockRegistry Registry { get; }

    /// <summary>
    /// Increments every time a chunk arrives, leaves, or has a block changed in it.
    /// </summary>
    /// <remarks>
    /// For anything that keeps a copy of a part of the world and has to know when it is stale.
    /// Comparing against the whole set of chunks would be the alternative, and a counter that
    /// moves on every change is both cheaper and harder to get subtly wrong — what it cannot do
    /// is say <em>where</em> the change was, which is why whoever reads it rebuilds everything.
    /// </remarks>
    public int Revision { get; private set; }

    /// <summary>Every block that has been changed from what the generator would have made.</summary>
    /// <remarks>
    /// Writable from outside so a save can be loaded into a world before anything is streamed;
    /// every edit recorded here is applied to a chunk as it is generated.
    /// </remarks>
    public BlockEdits Edits { get; } = new();

    /// <summary>How many chunks are resident.</summary>
    public int LoadedChunkCount => _chunks.Count;

    /// <summary>How many chunks have been asked for and have not arrived yet.</summary>
    public int StreamingCount => _streamer?.OutstandingCount ?? 0;

    /// <summary>
    /// The chunks whose geometry no longer matches what is drawn for them, including the neighbours
    /// of an edited chunk, since a block on a boundary changes the mesh on both sides of it.
    /// </summary>
    public IReadOnlyCollection<int3> Dirty => _dirty;

    /// <summary>Every resident chunk's coordinate.</summary>
    public IReadOnlyCollection<int3> LoadedCoordinates => _chunks.Keys;

    /// <summary>Whether a chunk coordinate is within the world's vertical bounds.</summary>
    public static bool IsWithinBounds(int3 chunkCoordinate) =>
        (uint)chunkCoordinate.Y < ChunksTall;

    /// <summary>The resident chunk at a coordinate, or <see langword="null"/>.</summary>
    public Chunk? Find(int3 chunkCoordinate) => _chunks.GetValueOrDefault(chunkCoordinate);

    /// <summary>
    /// Makes a chunk resident, generating it if it is not, and returns it. Returns
    /// <see langword="null"/> for a coordinate outside the world's vertical bounds.
    /// </summary>
    public Chunk? Load(int3 chunkCoordinate)
    {
        if (!IsWithinBounds(chunkCoordinate))
            return null;

        if (_chunks.TryGetValue(chunkCoordinate, out var existing))
            return existing;

        var chunk = new Chunk();
        _generator.Generate(chunkCoordinate, chunk);

        // The generator makes the world as it was; the edits make it as it is. Without this
        // step a block broken next to the player comes back the moment they walk far enough
        // for the chunk to unload, because unloading is how a chunk is forgotten.
        Edits.ApplyTo(chunkCoordinate, chunk);
        _chunks[chunkCoordinate] = chunk;
        Revision++;

        // A new chunk needs meshing, and so do its neighbours: the faces along the boundary they
        // share were built against air and now have something to be culled against.
        MarkDirtyWithNeighbours(chunkCoordinate);
        return chunk;
    }

    /// <summary>Drops a chunk. Returns whether there was one.</summary>
    public bool Unload(int3 chunkCoordinate)
    {
        if (!_chunks.Remove(chunkCoordinate))
            return false;

        Revision++;
        MarkDirtyWithNeighbours(chunkCoordinate);
        _dirty.Remove(chunkCoordinate);
        return true;
    }

    /// <inheritdoc />
    public BlockId BlockAt(int x, int y, int z)
    {
        var chunk = _chunks.GetValueOrDefault(
            new int3(WorldCoordinates.ToChunk(x), WorldCoordinates.ToChunk(y), WorldCoordinates.ToChunk(z))
        );

        return chunk?[WorldCoordinates.ToLocal(x), WorldCoordinates.ToLocal(y), WorldCoordinates.ToLocal(z)]
            ?? BlockId.Air;
    }

    /// <summary>The altitude of the highest solid block in a column, across the whole world.</summary>
    /// <param name="x">The column's x, in blocks.</param>
    /// <param name="z">Its z, in blocks.</param>
    /// <returns>
    /// The y of the highest block, or <see cref="int.MinValue" /> for a column that is empty or
    /// not loaded.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Eight dictionary lookups and eight array reads, because every chunk already keeps its own
    /// columns' tops for the lighting. That is what makes this worth having as a query rather
    /// than something a caller walks down a column to find out: the answer is already computed
    /// and the naive version reads a hundred and twenty-eight blocks.
    /// </para>
    /// <para>
    /// "Is there sky here" is the question it is really for, and it is one anything above the
    /// voxel layer asks: what the weather falls on, where a creature may stand in daylight, which
    /// surface a tree may grow from.
    /// </para>
    /// </remarks>
    public int HighestBlock(int x, int z)
    {
        var chunkX = WorldCoordinates.ToChunk(x);
        var chunkZ = WorldCoordinates.ToChunk(z);
        var localX = WorldCoordinates.ToLocal(x);
        var localZ = WorldCoordinates.ToLocal(z);

        for (var chunkY = ChunksTall - 1; chunkY >= 0; chunkY--)
        {
            var top = _chunks.GetValueOrDefault(new int3(chunkX, chunkY, chunkZ))?.HighestBlock(localX, localZ) ?? -1;
            if (top >= 0)
                return (chunkY * Chunk.Size) + top;
        }

        return int.MinValue;
    }

    /// <summary>
    /// Writes a block at an absolute coordinate, marking what has to be re-meshed. Returns whether
    /// anything changed.
    /// </summary>
    public bool SetBlock(int3 blockCoordinate, BlockId block)
    {
        var chunkCoordinate = WorldCoordinates.ToChunk(blockCoordinate);
        var chunk = Find(chunkCoordinate);
        if (chunk is null)
            return false;

        var local = WorldCoordinates.ToLocal(blockCoordinate);
        if (!chunk.Set(local.X, local.Y, local.Z, block))
            return false;

        Edits.Record(blockCoordinate, block);
        Revision++;
        _dirty.Add(chunkCoordinate);

        // Only the neighbours the block actually touches. A block in the middle of a chunk affects
        // nothing outside it, and re-meshing all twenty-six neighbours for every edit would make
        // placing a block cost twenty-seven chunk meshes instead of one.
        for (var axis = 0; axis < 3; axis++)
        {
            var component = axis switch
            {
                0 => local.X,
                1 => local.Y,
                _ => local.Z,
            };

            if (component == 0)
                _dirty.Add(chunkCoordinate - Unit(axis));
            else if (component == Chunk.Size - 1)
                _dirty.Add(chunkCoordinate + Unit(axis));
        }

        return true;
    }

    /// <summary>Takes the references a worker needs to mesh one chunk.</summary>
    /// <param name="chunkCoordinate">The chunk whose surface is wanted.</param>
    /// <returns>
    /// The chunk and the twenty-six around it, or <see langword="null" /> if that chunk is not
    /// loaded.
    /// </returns>
    /// <remarks>
    /// Must be called from the thread that owns the world: it walks the map, which nothing else
    /// may do. What comes back is safe to hand to another thread; see
    /// <see cref="ChunkNeighbourhood" /> for why.
    /// </remarks>
    public ChunkNeighbourhood? CaptureNeighbourhood(int3 chunkCoordinate) =>
        _chunks.ContainsKey(chunkCoordinate) ? new ChunkNeighbourhood(chunkCoordinate, Find) : null;

    /// <summary>
    /// Whether every neighbour a mesher needs is resident, so the chunk can be meshed without
    /// building faces against space that is merely unloaded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The nine columns around and including it, not just the six neighbouring faces. Ambient
    /// occlusion reads the blocks diagonal to a face, which is why the corners count; light reads
    /// the skyline, which is why the whole column does. A column that is only half loaded would
    /// report its sky as lower than it is and light a roofed chunk as though it were open.
    /// </para>
    /// <para>
    /// Whole columns is barely stricter than the twenty-seven were, because streaming asks for a
    /// column at a time: what is loaded sideways of a chunk is loaded all the way up.
    /// </para>
    /// </remarks>
    public bool IsMeshable(int3 chunkCoordinate)
    {
        if (!_chunks.ContainsKey(chunkCoordinate))
            return false;

        for (var y = 0; y < ChunksTall; y++)
        for (var z = -1; z <= 1; z++)
        for (var x = -1; x <= 1; x++)
        {
            var neighbour = new int3(chunkCoordinate.X + x, y, chunkCoordinate.Z + z);
            if (!_chunks.ContainsKey(neighbour))
                return false;
        }

        return true;
    }

    /// <summary>Forgets that a chunk needed re-meshing, which the renderer calls once it has.</summary>
    public void ClearDirty(int3 chunkCoordinate) => _dirty.Remove(chunkCoordinate);

    /// <summary>
    /// Brings residency in line with where the observer is: loads the chunks within
    /// <paramref name="radiusChunks"/> of them, nearest first, and drops the ones well outside.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="maxLoadsPerCall"/> is what keeps this from stalling a frame. Generating a
    /// chunk costs real time, and a player who teleports or turns a corner can bring hundreds into
    /// range at once; loading them over several frames shows the world filling in, which is the
    /// failure everyone prefers to a frozen window.
    /// </para>
    /// <para>
    /// Unloading uses a larger radius than loading. With one radius, an observer standing exactly
    /// on the boundary would load and unload the same chunk on alternate frames forever.
    /// </para>
    /// </remarks>
    /// <returns>How many chunks were loaded.</returns>
    public int UpdateStreaming(int3 centreChunk, int radiusChunks, int maxLoadsPerCall)
    {
        // Whatever the workers finished since last time, first: a chunk that has arrived should
        // be in the map before the scan below decides what is still missing.
        var loaded = _streamer?.Collect(maxLoadsPerCall, Insert) ?? 0;

        // Nearest first, so what fills in first is what the observer is standing in. Without a
        // streamer this is the generation itself and the budget bounds the frame's work; with
        // one it only queues, and the streamer's own capacity is what stops the queue running
        // away ahead of the player.
        var asked = 0;
        var askLimit = _streamer is null ? maxLoadsPerCall : _streamer.QueueCapacity;

        for (var ring = 0; ring <= radiusChunks && asked < askLimit; ring++)
        {
            for (var z = -ring; z <= ring && asked < askLimit; z++)
            {
                for (var x = -ring; x <= ring && asked < askLimit; x++)
                {
                    // Only the new ring's own cells: the inner ones were handled by earlier rings.
                    if (int.Max(int.Abs(x), int.Abs(z)) != ring)
                        continue;

                    for (var y = 0; y < ChunksTall && asked < askLimit; y++)
                    {
                        var coordinate = new int3(centreChunk.X + x, y, centreChunk.Z + z);
                        if (_chunks.ContainsKey(coordinate))
                            continue;

                        if (_streamer is null)
                        {
                            Load(coordinate);
                            loaded++;
                            asked++;
                        }
                        else if (_streamer.Request(coordinate))
                        {
                            asked++;
                        }
                    }
                }
            }
        }

        UnloadBeyond(centreChunk, radiusChunks + 2);
        return loaded;
    }

    /// <inheritdoc />
    /// <remarks>Stops the worker threads, if this world has any.</remarks>
    public void Dispose() => _streamer?.Dispose();

    /// <summary>Puts a chunk a worker finished into the map.</summary>
    /// <remarks>
    /// A chunk can arrive after the player has walked out of range of it, because a request
    /// cannot be taken back once a worker has started on it. Taking it anyway and letting the
    /// next unload pass drop it costs one frame of memory and saves the bookkeeping of a request
    /// that might be half done.
    /// </remarks>
    private void Insert(int3 coordinate, Chunk chunk)
    {
        if (!IsWithinBounds(coordinate) || _chunks.ContainsKey(coordinate))
            return;

        Edits.ApplyTo(coordinate, chunk);
        _chunks[coordinate] = chunk;
        Revision++;
        MarkDirtyWithNeighbours(coordinate);
    }

    private void UnloadBeyond(int3 centreChunk, int unloadRadius)
    {
        _leaving.Clear();

        foreach (var coordinate in _chunks.Keys)
        {
            var distance = int.Max(
                int.Abs(coordinate.X - centreChunk.X),
                int.Abs(coordinate.Z - centreChunk.Z)
            );

            if (distance > unloadRadius)
                _leaving.Add(coordinate);
        }

        foreach (var coordinate in _leaving)
            Unload(coordinate);
    }

    private void MarkDirtyWithNeighbours(int3 chunkCoordinate)
    {
        for (var y = -1; y <= 1; y++)
        for (var z = -1; z <= 1; z++)
        for (var x = -1; x <= 1; x++)
        {
            var neighbour = chunkCoordinate + new int3(x, y, z);
            if (_chunks.ContainsKey(neighbour))
                _dirty.Add(neighbour);
        }
    }

    private static int3 Unit(int axis) =>
        axis switch
        {
            0 => new int3(1, 0, 0),
            1 => new int3(0, 1, 0),
            _ => new int3(0, 0, 1),
        };
}
