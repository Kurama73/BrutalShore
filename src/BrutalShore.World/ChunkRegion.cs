namespace BrutalShore.World;

/// <summary>A box of chunks, frozen as a set of references for a worker to read.</summary>
/// <remarks>
/// <para>
/// The same arrangement as <see cref="ChunkNeighbourhood" /> and for the same reason — a worker
/// must never walk the world's dictionary while the main thread is adding to it — but a different
/// shape for a different job. A neighbourhood is one chunk and what it needs to be meshed: nine
/// columns, with a skyline. A region is a box of any size around a point, with no middle and no
/// skyline, because what reads it is not meshing anything.
/// </para>
/// <para>
/// What reads it is the ray marching field: a cube of the world around the camera, copied into a
/// texture so a shader can walk it. That cube spans far more chunks than a neighbourhood and has
/// no chunk at its centre that has to exist, so the two could only be one type by making both of
/// them vaguer.
/// </para>
/// <para>
/// References, not copies, exactly as there: the chunks stay alive while this is held, a block
/// that changes under a reader is one aligned write, and anything produced from an out-of-date
/// read is replaced on the next pass.
/// </para>
/// </remarks>
public sealed class ChunkRegion : IVoxelSource
{
    private readonly Chunk?[] _chunks;
    private readonly int3 _lowest;
    private readonly int3 _span;

    /// <summary>Captures every chunk overlapping a box of blocks.</summary>
    /// <param name="fromBlock">The box's lowest corner, in world blocks.</param>
    /// <param name="sizeBlocks">How many blocks the box spans on each axis.</param>
    /// <param name="lookup">How to find a chunk; called once per chunk, on the calling thread.</param>
    /// <exception cref="ArgumentOutOfRangeException">The box has no size.</exception>
    public ChunkRegion(int3 fromBlock, int3 sizeBlocks, Func<int3, Chunk?> lookup)
    {
        if (sizeBlocks.X <= 0 || sizeBlocks.Y <= 0 || sizeBlocks.Z <= 0)
            throw new ArgumentOutOfRangeException(nameof(sizeBlocks), sizeBlocks, "A region needs a positive size.");

        _lowest = WorldCoordinates.ToChunk(fromBlock);
        var highest = WorldCoordinates.ToChunk(fromBlock + sizeBlocks - new int3(1, 1, 1));
        _span = highest - _lowest + new int3(1, 1, 1);

        _chunks = new Chunk?[_span.X * _span.Y * _span.Z];

        for (var y = 0; y < _span.Y; y++)
        for (var z = 0; z < _span.Z; z++)
        for (var x = 0; x < _span.X; x++)
            _chunks[IndexOf(x, y, z)] = lookup(_lowest + new int3(x, y, z));
    }

    /// <summary>How many chunks were captured, loaded or not.</summary>
    public int ChunkCount => _chunks.Length;

    /// <inheritdoc />
    /// <remarks>Anything outside the box, or in a chunk that is not loaded, reads as air.</remarks>
    public BlockId BlockAt(int x, int y, int z)
    {
        var chunk = WorldCoordinates.ToChunk(new int3(x, y, z)) - _lowest;

        if (
            (uint)chunk.X >= (uint)_span.X
            || (uint)chunk.Y >= (uint)_span.Y
            || (uint)chunk.Z >= (uint)_span.Z
        )
            return BlockId.Air;

        return _chunks[IndexOf(chunk.X, chunk.Y, chunk.Z)]
                ?[WorldCoordinates.ToLocal(x), WorldCoordinates.ToLocal(y), WorldCoordinates.ToLocal(z)]
            ?? BlockId.Air;
    }

    private int IndexOf(int x, int y, int z) => (y * _span.X * _span.Z) + (z * _span.X) + x;
}
