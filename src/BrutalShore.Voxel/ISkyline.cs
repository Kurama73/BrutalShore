namespace BrutalShore.Voxel;

/// <summary>Where the open sky stops, per column of the world.</summary>
/// <remarks>
/// <para>
/// This exists because sky light is the one quantity that cannot be worked out from a
/// neighbourhood. Everything else a mesher or <see cref="LightVolume" /> needs is within fifteen
/// blocks, and fifteen blocks is a few chunks; whether a cell can see the sky depends on every
/// block above it, however far up that goes.
/// </para>
/// <para>
/// Separating the question from the answer is what keeps <see cref="LightVolume" /> testable:
/// lighting a cave means saying where the roof is, and saying it does not require a streamed
/// world to say it with.
/// </para>
/// </remarks>
public interface ISkyline
{
    /// <summary>
    /// The y of the highest block in a column, or <see cref="int.MinValue" /> if the column holds
    /// nothing at all. Every cell above the answer sees the sky with nothing in the way.
    /// </summary>
    /// <param name="x">The column's x, in world blocks.</param>
    /// <param name="z">The column's z, in world blocks.</param>
    int HighestBlock(int x, int z);
}
