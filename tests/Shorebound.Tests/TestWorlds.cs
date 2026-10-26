namespace Shorebound.Tests;

/// <summary>Small hand-built worlds for testing how the player moves through one.</summary>
/// <remarks>
/// Deliberately not this game's terrain. A movement test that used it would be asserting what the
/// noise happened to put at a coordinate, and would break the day the terrain is tuned.
/// </remarks>
internal static class TestWorlds
{
    /// <summary>A registry matching the worlds below: one block, and it is a wall.</summary>
    public static BlockRegistry Walls { get; } = BuildWalls();

    /// <summary>One solid layer at y = 0, unbounded sideways.</summary>
    public static IVoxelSource Floor { get; } = new FloorWorld();

    /// <summary>A floor, plus a step of a given height filling everything from one x onwards.</summary>
    /// <param name="height">How many blocks tall the step is.</param>
    /// <param name="fromX">The first x the step occupies.</param>
    /// <returns>The world.</returns>
    public static IVoxelSource FloorWithStep(int height, int fromX) => new StepWorld(height, fromX);

    /// <summary>A registry with a wall and a liquid, for testing what water does to a body.</summary>
    public static BlockRegistry WallsAndWater { get; } = BuildWallsAndWater();

    /// <summary>The liquid in <see cref="WallsAndWater" />.</summary>
    public static BlockId Water { get; private set; }

    /// <summary>A floor, with the liquid filling everything from one height up to another.</summary>
    /// <param name="from">The lowest block the liquid fills.</param>
    /// <param name="to">The highest block the liquid fills.</param>
    /// <returns>The world.</returns>
    public static IVoxelSource FloorUnderWater(int from, int to) => new FloodedWorld(from, to, Water);

    private static BlockRegistry BuildWallsAndWater()
    {
        var registry = new BlockRegistry();
        registry.Register(new BlockType { Name = "wall", Color = new byte3(128, 128, 128) });
        Water = registry.Register(
            new BlockType
            {
                Name = "water",
                Color = new byte3(0, 0, 255),
                Opacity = 170,
                Collides = false,
                IsLiquid = true,
            }
        );
        return registry;
    }

    private sealed class FloodedWorld(int from, int to, BlockId water) : IVoxelSource
    {
        public BlockId BlockAt(int x, int y, int z) =>
            y == 0 ? new BlockId(1)
            : y >= from && y <= to ? water
            : BlockId.Air;
    }

    private static BlockRegistry BuildWalls()
    {
        var registry = new BlockRegistry();
        registry.Register(new BlockType { Name = "wall", Color = new byte3(128, 128, 128) });
        return registry;
    }

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
