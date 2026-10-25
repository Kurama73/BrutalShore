namespace BrutalShore.World;

/// <summary>A chunk and the twenty-six around it, frozen as a set of references.</summary>
/// <remarks>
/// <para>
/// Meshing a chunk needs the blocks just outside it, which means reading its neighbours. Doing
/// that through <see cref="VoxelWorld" /> means walking a dictionary the main thread is free to
/// add to and remove from, and a dictionary being resized under a reader is the kind of fault
/// that shows up as a hang rather than an exception. This is the way round that: the main thread
/// takes the twenty-seven references once, and the worker reads only those.
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
/// </remarks>
public sealed class ChunkNeighbourhood : IVoxelSource
{
    /// <summary>How far the neighbourhood reaches, in chunks, from the middle.</summary>
    private const int Reach = 1;

    private const int Span = (Reach * 2) + 1;

    private readonly Chunk?[] _chunks = new Chunk?[Span * Span * Span];

    /// <summary>Captures the chunks around one coordinate.</summary>
    /// <param name="centre">The chunk in the middle, whose surface is wanted.</param>
    /// <param name="lookup">How to find a chunk; called once per neighbour, on the calling thread.</param>
    /// <exception cref="ArgumentException">There is no chunk at the middle.</exception>
    public ChunkNeighbourhood(int3 centre, Func<int3, Chunk?> lookup)
    {
        Centre = centre;

        for (var y = -Reach; y <= Reach; y++)
        for (var z = -Reach; z <= Reach; z++)
        for (var x = -Reach; x <= Reach; x++)
            _chunks[IndexOf(x, y, z)] = lookup(new int3(centre.X + x, centre.Y + y, centre.Z + z));

        Middle =
            _chunks[IndexOf(0, 0, 0)]
            ?? throw new ArgumentException($"There is no chunk at {centre} to mesh.", nameof(centre));
    }

    /// <summary>Where the middle chunk sits, in chunk units.</summary>
    public int3 Centre { get; }

    /// <summary>The chunk in the middle, the one being meshed.</summary>
    public Chunk Middle { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Anything outside the twenty-seven reads as air. For a mesher that is the right answer:
    /// a face with nothing known on the far side is a face worth drawing, and the caller only
    /// asks for a chunk once all of its neighbours are loaded.
    /// </remarks>
    public BlockId BlockAt(int x, int y, int z)
    {
        var offsetX = WorldCoordinates.ToChunk(x) - Centre.X;
        var offsetY = WorldCoordinates.ToChunk(y) - Centre.Y;
        var offsetZ = WorldCoordinates.ToChunk(z) - Centre.Z;

        if (!InReach(offsetX) || !InReach(offsetY) || !InReach(offsetZ))
            return BlockId.Air;

        var chunk = _chunks[IndexOf(offsetX, offsetY, offsetZ)];
        return chunk?[WorldCoordinates.ToLocal(x), WorldCoordinates.ToLocal(y), WorldCoordinates.ToLocal(z)]
            ?? BlockId.Air;
    }

    private static bool InReach(int offset) => offset is >= -Reach and <= Reach;

    private static int IndexOf(int x, int y, int z) =>
        ((y + Reach) * Span * Span) + ((z + Reach) * Span) + (x + Reach);
}
