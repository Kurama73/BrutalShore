using BrutalShore.Voxel;

namespace BrutalShore.Voxel.Tests;

/// <summary>
/// A world of a single chunk at the origin, plus whatever the test declares around it. The second
/// implementation of <see cref="IVoxelSource"/> that the interface exists for.
/// </summary>
/// <remarks>
/// It answers <see cref="ISkyline" /> as well, worked out from the same blocks, so a mesher test
/// gets whatever light its own scene implies rather than having to state it twice.
/// </remarks>
internal sealed class TestWorld(Chunk chunk) : IVoxelSource, ISkyline
{
    private readonly Dictionary<(int, int, int), BlockId> _outside = [];

    // The y values declared outside the chunk, per column, so answering the skyline is the
    // maximum of a handful of numbers rather than a scan of everything declared.
    private readonly Dictionary<(int X, int Z), HashSet<int>> _outsideColumns = [];

    public Chunk Chunk { get; } = chunk;

    /// <summary>Declares a block outside the chunk, which is how a seam test builds a neighbour.</summary>
    public TestWorld With(int x, int y, int z, BlockId block)
    {
        _outside[(x, y, z)] = block;

        if (!_outsideColumns.TryGetValue((x, z), out var column))
            _outsideColumns[(x, z)] = column = [];

        column.Add(y);
        return this;
    }

    /// <summary>
    /// Puts a block wherever it belongs — in the chunk if the coordinate is inside it, outside
    /// otherwise — which is how a scene that straddles the chunk's edge is declared.
    /// </summary>
    public TestWorld Place(int x, int y, int z, BlockId block)
    {
        if (Chunk.Contains(x, y, z))
        {
            Chunk[x, y, z] = block;
            return this;
        }

        return With(x, y, z, block);
    }

    /// <summary>Declares a whole solid plane just past one face of the chunk.</summary>
    public TestWorld WithSolidNeighbourAbove(BlockId block)
    {
        for (var z = 0; z < Chunk.Size; z++)
            for (var x = 0; x < Chunk.Size; x++)
                _outside[(x, Chunk.Size, z)] = block;
        return this;
    }

    /// <summary>Declares a whole solid plane just under the chunk, which is the ground it sits on.</summary>
    /// <remarks>
    /// Worth having because a chunk floating in the open is lit from underneath — daylight reaches
    /// its underside sideways from the columns around it, in a gradient — and a merge count is a
    /// claim about merging, not about that.
    /// </remarks>
    public TestWorld WithSolidNeighbourBelow(BlockId block)
    {
        for (var z = 0; z < Chunk.Size; z++)
            for (var x = 0; x < Chunk.Size; x++)
                _outside[(x, -1, z)] = block;
        return this;
    }

    public BlockId BlockAt(int x, int y, int z) =>
        Chunk.Contains(x, y, z)
            ? Chunk[x, y, z]
            : _outside.GetValueOrDefault((x, y, z), BlockId.Air);

    public int HighestBlock(int x, int z)
    {
        var highest = int.MinValue;

        if (_outsideColumns.TryGetValue((x, z), out var column))
            foreach (var y in column)
                if (_outside[(x, y, z)].IsSolid)
                    highest = int.Max(highest, y);

        if (!Chunk.Contains(x, 0, z))
            return highest;

        var inside = Chunk.HighestBlock(x, z);
        return inside >= 0 ? int.Max(highest, inside) : highest;
    }

    /// <summary>The light around the chunk at the origin, as this world's own blocks imply it.</summary>
    public LightVolume Light(BlockRegistry registry)
    {
        var volume = new LightVolume();
        volume.Illuminate(int3.Zero, this, registry, this);
        return volume;
    }
}
