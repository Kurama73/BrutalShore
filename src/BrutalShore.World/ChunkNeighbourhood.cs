namespace BrutalShore.World;

/// <summary>A chunk, the eight columns around it and its own, frozen as a set of references.</summary>
/// <remarks>
/// <para>
/// Meshing a chunk needs the blocks just outside it, which means reading its neighbours. Doing
/// that through <see cref="VoxelWorld" /> means walking a dictionary the main thread is free to
/// add to and remove from, and a dictionary being resized under a reader is the kind of fault
/// that shows up as a hang rather than an exception. This is the way round that: the main thread
/// takes the references once, and the worker reads only those.
/// </para>
/// <para>
/// References, not copies. Twenty-seven chunks is nearly a megabyte, and copying that per mesh
/// would cost more than the meshing. What it means is that a block can change while a worker is
/// reading it — an edit is one aligned write of two bytes, so the worker sees the old value or
/// the new one and never half of each, and the edit marks the chunk dirty anyway, so whatever it
/// produced is replaced on the next pass.
/// </para>
/// <para>
/// A chunk that was unloaded while a worker held this stays alive for as long as the reference
/// does, which is what makes the arrangement safe at all: nothing here can become a dangling
/// read, only an out-of-date one.
/// </para>
/// <para>
/// Whole columns rather than only the three chunks above and below, because light needs the skyline
/// and the skyline is a question about a column: whether a cell can see the sky depends on
/// everything above it, however far up that is. Nine columns is seventy-two references against
/// twenty-seven, which costs nothing — the chunks are not copied either way — and it is what
/// makes this an <see cref="ISkyline" /> as well as a source of blocks.
/// </para>
/// </remarks>
public sealed class ChunkNeighbourhood : IVoxelSource, ISkyline
{
    /// <summary>How far the neighbourhood reaches sideways, in chunks, from the middle.</summary>
    private const int Reach = 1;

    private const int Span = (Reach * 2) + 1;

    private readonly Chunk?[] _chunks = new Chunk?[Span * Span * VoxelWorld.ChunksTall];

    /// <summary>Captures the chunks around one coordinate.</summary>
    /// <param name="centre">The chunk in the middle, whose surface is wanted.</param>
    /// <param name="lookup">How to find a chunk; called once per neighbour, on the calling thread.</param>
    /// <exception cref="ArgumentException">There is no chunk at the middle, or it is outside the world.</exception>
    public ChunkNeighbourhood(int3 centre, Func<int3, Chunk?> lookup)
    {
        if ((uint)centre.Y >= VoxelWorld.ChunksTall)
            throw new ArgumentException($"Chunk {centre} is above or below the world.", nameof(centre));

        Centre = centre;

        for (var y = 0; y < VoxelWorld.ChunksTall; y++)
        for (var z = -Reach; z <= Reach; z++)
        for (var x = -Reach; x <= Reach; x++)
            _chunks[IndexOf(x, y, z)] = lookup(new int3(centre.X + x, y, centre.Z + z));

        Middle =
            _chunks[IndexOf(0, centre.Y, 0)]
            ?? throw new ArgumentException($"There is no chunk at {centre} to mesh.", nameof(centre));
    }

    /// <summary>Where the middle chunk sits, in chunk units.</summary>
    public int3 Centre { get; }

    /// <summary>The chunk in the middle, the one being meshed.</summary>
    public Chunk Middle { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Anything outside the nine columns reads as air, as does anything above or below the world,
    /// where there is genuinely nothing. For a mesher that is the right answer: a face with
    /// nothing known on the far side is a face worth drawing, and the caller only asks for a
    /// chunk once all of its neighbours are loaded.
    /// </remarks>
    public BlockId BlockAt(int x, int y, int z)
    {
        var offsetX = WorldCoordinates.ToChunk(x) - Centre.X;
        var offsetZ = WorldCoordinates.ToChunk(z) - Centre.Z;

        if (!InReach(offsetX) || !InReach(offsetZ))
            return BlockId.Air;

        var chunk = ChunkAt(offsetX, y, offsetZ);
        return chunk?[WorldCoordinates.ToLocal(x), WorldCoordinates.ToLocal(y), WorldCoordinates.ToLocal(z)]
            ?? BlockId.Air;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Walks the column down from the top of the world, which is eight chunks and a lookup each,
    /// because every chunk keeps its own columns' tops. A column outside the nine, or one holding
    /// nothing, reads as open sky — the same optimism as a block outside reading as air, and with
    /// the same consequence: the edge of what is loaded is lit as though nothing stood there.
    /// </remarks>
    public int HighestBlock(int x, int z)
    {
        var offsetX = WorldCoordinates.ToChunk(x) - Centre.X;
        var offsetZ = WorldCoordinates.ToChunk(z) - Centre.Z;

        if (!InReach(offsetX) || !InReach(offsetZ))
            return int.MinValue;

        var localX = WorldCoordinates.ToLocal(x);
        var localZ = WorldCoordinates.ToLocal(z);

        for (var y = VoxelWorld.ChunksTall - 1; y >= 0; y--)
        {
            var top = _chunks[IndexOf(offsetX, y, offsetZ)]?.HighestBlock(localX, localZ) ?? -1;
            if (top >= 0)
                return (y * Chunk.Size) + top;
        }

        return int.MinValue;
    }

    private static bool InReach(int offset) => offset is >= -Reach and <= Reach;

    private static int IndexOf(int x, int y, int z) =>
        (y * Span * Span) + ((z + Reach) * Span) + (x + Reach);

    /// <summary>The chunk holding a world y in one of the nine columns, or nothing outside the world.</summary>
    private Chunk? ChunkAt(int offsetX, int worldY, int offsetZ)
    {
        var chunkY = WorldCoordinates.ToChunk(worldY);
        return (uint)chunkY < VoxelWorld.ChunksTall ? _chunks[IndexOf(offsetX, chunkY, offsetZ)] : null;
    }
}
