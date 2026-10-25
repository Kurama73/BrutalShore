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
public sealed class VoxelWorld : IVoxelSource
{
    /// <summary>How many chunks tall the world is.</summary>
    public const int ChunksTall = 8;

    /// <summary>How many blocks tall the world is.</summary>
    public const int HeightBlocks = ChunksTall * Chunk.Size;

    private readonly IChunkGenerator _generator;
    private readonly Dictionary<int3, Chunk> _chunks = [];
    private readonly HashSet<int3> _dirty = [];

    /// <summary>Creates a world.</summary>
    /// <param name="registry">The block types its chunks are made of.</param>
    /// <param name="generator">What produces a chunk nobody has stored.</param>
    public VoxelWorld(BlockRegistry registry, IChunkGenerator generator)
    {
        Registry = registry;
        _generator = generator;
    }

    /// <summary>The block types this world is made of.</summary>
    public BlockRegistry Registry { get; }

    /// <summary>Every block that has been changed from what the generator would have made.</summary>
    /// <remarks>
    /// Writable from outside so a save can be loaded into a world before anything is streamed;
    /// every edit recorded here is applied to a chunk as it is generated.
    /// </remarks>
    public BlockEdits Edits { get; } = new();

    /// <summary>How many chunks are resident.</summary>
    public int LoadedChunkCount => _chunks.Count;

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

    /// <summary>
    /// Whether every neighbour a mesher needs is resident, so the chunk can be meshed without
    /// building faces against space that is merely unloaded.
    /// </summary>
    /// <remarks>
    /// The full 3x3x3 neighbourhood, not just the six faces, because ambient occlusion reads the
    /// blocks diagonal to a face. Neighbours above or below the world are not missing — there is
    /// genuinely nothing there — so they do not count against this.
    /// </remarks>
    public bool IsMeshable(int3 chunkCoordinate)
    {
        if (!_chunks.ContainsKey(chunkCoordinate))
            return false;

        for (var y = -1; y <= 1; y++)
        for (var z = -1; z <= 1; z++)
        for (var x = -1; x <= 1; x++)
        {
            var neighbour = chunkCoordinate + new int3(x, y, z);
            if (IsWithinBounds(neighbour) && !_chunks.ContainsKey(neighbour))
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
        var loaded = 0;

        // Nearest first, so what fills in first is what the observer is standing in.
        for (var ring = 0; ring <= radiusChunks && loaded < maxLoadsPerCall; ring++)
        {
            for (var z = -ring; z <= ring && loaded < maxLoadsPerCall; z++)
            {
                for (var x = -ring; x <= ring && loaded < maxLoadsPerCall; x++)
                {
                    // Only the new ring's own cells: the inner ones were handled by earlier rings.
                    if (int.Max(int.Abs(x), int.Abs(z)) != ring)
                        continue;

                    for (var y = 0; y < ChunksTall && loaded < maxLoadsPerCall; y++)
                    {
                        var coordinate = new int3(centreChunk.X + x, y, centreChunk.Z + z);
                        if (_chunks.ContainsKey(coordinate))
                            continue;

                        Load(coordinate);
                        loaded++;
                    }
                }
            }
        }

        var unloadRadius = radiusChunks + 2;
        foreach (var coordinate in _chunks.Keys.ToArray())
        {
            var distance = int.Max(
                int.Abs(coordinate.X - centreChunk.X),
                int.Abs(coordinate.Z - centreChunk.Z)
            );

            if (distance > unloadRadius)
                Unload(coordinate);
        }

        return loaded;
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
