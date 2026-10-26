namespace Shorebound;

/// <summary>What a structure needs to know about the ground it would stand on.</summary>
/// <remarks>
/// <para>
/// Narrow on purpose. <see cref="Trees" /> asks only how high the ground is and how much grows
/// there, which keeps it independent of how either answer is arrived at — and keeps a test able
/// to grow a forest on flat ground without building a terrain generator to do it.
/// </para>
/// <para>
/// Every answer has to be a pure function of the coordinate, because two chunks on either side of
/// a tree will each ask the same question and have to get the same answer.
/// </para>
/// </remarks>
public interface IGround
{
    /// <summary>The height of the top solid block of a column.</summary>
    /// <param name="blockX">The column's x, in blocks.</param>
    /// <param name="blockZ">The column's z, in blocks.</param>
    /// <returns>The y of the highest solid block there.</returns>
    int SurfaceHeight(int blockX, int blockZ);

    /// <summary>How much of the usual forest grows at a column, from 0 to 1.</summary>
    /// <param name="blockX">The column's x, in blocks.</param>
    /// <param name="blockZ">The column's z, in blocks.</param>
    /// <returns>The share of trees that take root there.</returns>
    float TreeChance(int blockX, int blockZ);
}
