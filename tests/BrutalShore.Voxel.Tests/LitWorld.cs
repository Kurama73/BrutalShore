using BrutalShore.Voxel;

namespace BrutalShore.Voxel.Tests;

/// <summary>
/// A world declared block by block, which also answers where its own sky stops.
/// </summary>
/// <remarks>
/// The second implementation of <see cref="ISkyline" /> that the interface exists for, and the
/// reason lighting a cave can be asserted without a streamed world: the roof is three lines of
/// test, and the skyline follows from it rather than having to be stated separately and kept in
/// step by hand.
/// </remarks>
internal sealed class LitWorld : IVoxelSource, ISkyline
{
    private readonly Dictionary<(int X, int Y, int Z), BlockId> _blocks = [];

    // The y values held in each column, so the skyline is the maximum of a handful of numbers
    // rather than a scan of the whole world per column asked about.
    private readonly Dictionary<(int X, int Z), HashSet<int>> _columns = [];

    /// <summary>Puts one block in the world.</summary>
    public LitWorld With(int x, int y, int z, BlockId block)
    {
        _blocks[(x, y, z)] = block;

        if (!_columns.TryGetValue((x, z), out var column))
            _columns[(x, z)] = column = [];

        column.Add(y);
        return this;
    }

    /// <summary>Fills a box, inclusive at both ends, which is how a roof or a floor is declared.</summary>
    public LitWorld WithBox(int3 from, int3 to, BlockId block)
    {
        for (var y = from.Y; y <= to.Y; y++)
        for (var z = from.Z; z <= to.Z; z++)
        for (var x = from.X; x <= to.X; x++)
            With(x, y, z, block);

        return this;
    }

    /// <summary>Takes a block back out, which is how a hole in a roof is declared.</summary>
    public LitWorld Without(int x, int y, int z)
    {
        _blocks.Remove((x, y, z));
        _columns.GetValueOrDefault((x, z))?.Remove(y);
        return this;
    }

    public BlockId BlockAt(int x, int y, int z) => _blocks.GetValueOrDefault((x, y, z), BlockId.Air);

    public int HighestBlock(int x, int z)
    {
        if (!_columns.TryGetValue((x, z), out var column))
            return int.MinValue;

        var highest = int.MinValue;
        foreach (var y in column)
            highest = int.Max(highest, y);

        return highest;
    }
}
