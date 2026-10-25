using RustSpace.Voxel;

namespace RustSpace.Voxel.Tests;

/// <summary>
/// A world of a single chunk at the origin, plus whatever the test declares around it. The second
/// implementation of <see cref="IVoxelSource"/> that the interface exists for.
/// </summary>
internal sealed class TestWorld(Chunk chunk) : IVoxelSource
{
    private readonly Dictionary<(int, int, int), BlockId> _outside = [];

    public Chunk Chunk { get; } = chunk;

    /// <summary>Declares a block outside the chunk, which is how a seam test builds a neighbour.</summary>
    public TestWorld With(int x, int y, int z, BlockId block)
    {
        _outside[(x, y, z)] = block;
        return this;
    }

    /// <summary>Declares a whole solid plane just past one face of the chunk.</summary>
    public TestWorld WithSolidNeighbourAbove(BlockId block)
    {
        for (var z = 0; z < Chunk.Size; z++)
            for (var x = 0; x < Chunk.Size; x++)
                _outside[(x, Chunk.Size, z)] = block;
        return this;
    }

    public BlockId BlockAt(int x, int y, int z) =>
        Chunk.Contains(x, y, z)
            ? Chunk[x, y, z]
            : _outside.GetValueOrDefault((x, y, z), BlockId.Air);
}
