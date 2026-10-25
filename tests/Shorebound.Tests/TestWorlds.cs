namespace Shorebound.Tests;

/// <summary>Small hand-built worlds for testing how the player moves through one.</summary>
/// <remarks>
/// Deliberately not this game's terrain. A movement test that used it would be asserting what the
/// noise happened to put at a coordinate, and would break the day the terrain is tuned.
/// </remarks>
internal static class TestWorlds
{
    /// <summary>One solid layer at y = 0, unbounded sideways.</summary>
    public static IVoxelSource Floor { get; } = new FloorWorld();

    /// <summary>A floor, plus a step of a given height filling everything from one x onwards.</summary>
    /// <param name="height">How many blocks tall the step is.</param>
    /// <param name="fromX">The first x the step occupies.</param>
    /// <returns>The world.</returns>
    public static IVoxelSource FloorWithStep(int height, int fromX) => new StepWorld(height, fromX);

    private sealed class FloorWorld : IVoxelSource
    {
        public BlockId BlockAt(int x, int y, int z) => y == 0 ? new BlockId(1) : BlockId.Air;
    }

    private sealed class StepWorld(int height, int fromX) : IVoxelSource
    {
        public BlockId BlockAt(int x, int y, int z) =>
            y == 0 || (x >= fromX && y <= height) ? new BlockId(1) : BlockId.Air;
    }
}
