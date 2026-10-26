namespace Shorebound.Tests;

/// <summary>
/// The horde: where it comes from, what keeps it away, what it does when it arrives, and what
/// makes more of it.
/// </summary>
/// <remarks>
/// The pressure curve is the claim worth pinning down. A horde whose size did not answer to what
/// the player built would be weather rather than a consequence, and the whole middle of this game
/// is the trade between building more and being able to hold it.
/// </remarks>
public sealed class StalkerTests
{
    private readonly GameBlocks _blocks = new(new BlockRegistry());

    [Fact]
    public void an_empty_shore_allows_only_a_few()
    {
        Assert.Equal(4, Stalkers.Allowed(0));
    }

    [Fact]
    public void running_more_machines_brings_more_of_them()
    {
        // The trade the game is about: every smelter is production and another thing to defend.
        Assert.True(Stalkers.Allowed(8) > Stalkers.Allowed(0));
        Assert.True(Stalkers.Allowed(20) > Stalkers.Allowed(8));
    }

    [Fact]
    public void the_horde_has_a_ceiling()
    {
        // Because the cost of them is linear and the fear of them is not.
        Assert.Equal(Stalkers.Allowed(1000), Stalkers.Allowed(10_000));
    }

    [Fact]
    public void none_come_out_in_broad_daylight()
    {
        var world = Flat(out var registry);
        var stalkers = new Stalkers(7);

        Run(stalkers, world, registry, seconds: 30.0, isDay: true);

        Assert.Equal(0, stalkers.Count);
    }

    [Fact]
    public void they_come_out_at_night()
    {
        var world = Flat(out var registry);
        var stalkers = new Stalkers(7);

        Run(stalkers, world, registry, seconds: 30.0, isDay: false);

        Assert.True(stalkers.Count > 0);
    }

    [Fact]
    public void they_bite_a_player_standing_among_them()
    {
        var world = Flat(out var registry);
        var stalkers = new Stalkers(11);
        var vitality = new Vitality();

        Run(stalkers, world, registry, seconds: 120.0, isDay: false, vitality: vitality);

        Assert.True(vitality.Health < Vitality.Whole, "nothing ever reached the player");
    }

    [Fact]
    public void daylight_finishes_the_ones_caught_out()
    {
        var world = Flat(out var registry);
        var stalkers = new Stalkers(13);

        Run(stalkers, world, registry, seconds: 40.0, isDay: false);
        Assert.True(stalkers.Count > 0);

        Run(stalkers, world, registry, seconds: 20.0, isDay: true);
        Assert.Equal(0, stalkers.Count);
    }

    [Fact]
    public void a_lamp_keeps_them_off_the_ground_it_lights()
    {
        // The reason a lantern is worth carrying back up out of the mine.
        var world = Flat(out var registry);
        var stalkers = new Stalkers(17);

        for (var z = -60; z <= 60; z += 6)
        for (var x = -60; x <= 60; x += 6)
            world.SetBlock(new int3(x, 21, z), _blocks.Lantern);

        Run(stalkers, world, registry, seconds: 60.0, isDay: false);

        Assert.Equal(0, stalkers.Count);
    }

    [Fact]
    public void two_swings_finish_one()
    {
        var world = Flat(out var registry);
        var stalkers = new Stalkers(19);

        Run(stalkers, world, registry, seconds: 30.0, isDay: false);
        var before = stalkers.Count;
        Assert.True(before > 0);

        // Swung at from above and behind nothing, so the cone certainly covers one of them.
        var target = stalkers.All[0].FeetMeters;
        var from = target + new double3(0.0, 1.4, -2.0);
        var along = Normalised((target + new double3(0.0, 0.45, 0.0)) - from);

        Assert.True(stalkers.Strike(from, along, 3.2, 3f));
        Assert.Equal(before, stalkers.Count);

        Assert.True(stalkers.Strike(from, along, 3.2, 3f));
        Assert.Equal(before - 1, stalkers.Count);
    }

    [Fact]
    public void a_swing_at_nothing_connects_with_nothing()
    {
        var stalkers = new Stalkers(23);

        Assert.False(stalkers.Strike(double3.Zero, new double3(0, 0, 1), 3.2, 3f));
    }

    [Fact]
    public void clearing_forgets_the_whole_horde()
    {
        var world = Flat(out var registry);
        var stalkers = new Stalkers(29);

        Run(stalkers, world, registry, seconds: 30.0, isDay: false);
        stalkers.Clear();

        Assert.Equal(0, stalkers.Count);
    }

    /// <summary>A loaded flat world with its surface at 20 and open sky above it.</summary>
    private VoxelWorld Flat(out BlockRegistry registry)
    {
        registry = _blocks.Registry;
        var world = new VoxelWorld(registry, new SlabGenerator(_blocks.Stone, 20));

        for (var chunkZ = -3; chunkZ <= 3; chunkZ++)
        for (var chunkX = -3; chunkX <= 3; chunkX++)
        for (var chunkY = 0; chunkY < VoxelWorld.ChunksTall; chunkY++)
            world.Load(new int3(chunkX, chunkY, chunkZ));

        return world;
    }

    private static void Run(
        Stalkers stalkers,
        VoxelWorld world,
        BlockRegistry registry,
        double seconds,
        bool isDay,
        Vitality? vitality = null,
        int machines = 0,
        double3? at = null
    )
    {
        const double Step = 1.0 / 60.0;
        var player = at ?? new double3(0.5, 21.0, 0.5);
        var health = vitality ?? new Vitality();

        for (var elapsed = 0.0; elapsed < seconds; elapsed += Step)
        {
            health.Step(Step);
            stalkers.Step(world, registry, Step, player, health, machines, isDay);
        }
    }

    [Fact]
    public void nothing_reaches_a_player_standing_well_above_it()
    {
        // The regression. Reach was measured flat, so something standing on the ground bit a
        // player hovering fourteen blocks over it — found by leaving a works running through a
        // night and watching the camera die in mid-air with nothing anywhere near it.
        var world = Flat(out var registry);
        var stalkers = new Stalkers(11);
        var ground = new double3(0.5, 21.0, 0.5);

        // Let them gather where the player was, then rise out of reach without moving off.
        Run(stalkers, world, registry, seconds: 120.0, isDay: false, at: ground);
        Assert.True(stalkers.Count > 0, "nothing came out, so this proves nothing");

        var aloft = new Vitality();
        Run(stalkers, world, registry, seconds: 20.0, isDay: false, vitality: aloft, at: ground + new double3(0, 6, 0));

        Assert.Equal(Vitality.Whole, aloft.Health);
    }

    [Fact]
    public void stepping_up_one_block_does_not_make_a_player_safe()
    {
        // The other half of the claim: the fix must not turn every ledge into a wall, because
        // something that steps up one block has to be able to bite what is standing up there.
        var world = Flat(out var registry);
        var stalkers = new Stalkers(11);
        var ground = new double3(0.5, 21.0, 0.5);

        Run(stalkers, world, registry, seconds: 120.0, isDay: false, at: ground);

        var raised = new Vitality();
        Run(stalkers, world, registry, seconds: 30.0, isDay: false, vitality: raised, at: ground + new double3(0, 1, 0));

        Assert.True(raised.Health < Vitality.Whole, "a single block of ledge made the player untouchable");
    }

    private static double3 Normalised(double3 value)
    {
        var length = double.Sqrt(double3.Dot(value, value));
        return length < 1e-9 ? value : value / length;
    }
}

/// <summary>A world that is solid up to an altitude and open air above it.</summary>
/// <remarks>
/// A second generator for the game's own tests, so that what the stalkers do can be asserted
/// against ground that is flat rather than against whatever this seed's terrain happens to be.
/// </remarks>
internal sealed class SlabGenerator(BlockId ground, int surfaceHeight) : IChunkGenerator
{
    public void Generate(int3 chunkCoordinate, Chunk into)
    {
        var originY = chunkCoordinate.Y * Chunk.Size;
        if (originY > surfaceHeight)
            return;

        var blocks = into.BeginBulkWrite();
        for (var y = 0; y < Chunk.Size; y++)
        {
            if (originY + y > surfaceHeight)
                break;

            for (var z = 0; z < Chunk.Size; z++)
            for (var x = 0; x < Chunk.Size; x++)
                blocks[Chunk.IndexOf(x, y, z)] = ground;
        }

        into.EndBulkWrite();
    }
}
