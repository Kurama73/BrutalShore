namespace Shorebound.Tests;

/// <summary>
/// What a broken block does next: it falls out, it lands, it is pulled to whoever is near, and it
/// goes into their row — or it stays where it is.
/// </summary>
/// <remarks>
/// The claims worth having are the refusals. A drop that could be picked up through a wall, or
/// before it had visibly fallen, or into a row with no room in it, would each be a quiet way of
/// making blocks appear from nowhere or disappear into it.
/// </remarks>
public sealed class DropsTests
{
    private const double Step = 1.0 / 60.0;

    private readonly GameBlocks _blocks = new(new BlockRegistry());
    private readonly IVoxelSource _world;

    /// <summary>A world with one solid floor in it, for drops to land on.</summary>
    public DropsTests() => _world = new FloorAt(10, _blocks.Stone);

    [Fact]
    public void a_dropped_block_lands_on_the_floor_and_stays_there()
    {
        var drops = new Drops(seed: 1);
        drops.Spawn(_blocks.Stone, new int3(4, 20, 4));

        Run(drops, seconds: 4.0, playerAt: Far);

        Assert.Equal(1, drops.Count);

        // The floor's top is at y = 11, so a cube resting on it has its base there.
        Assert.Equal(11.0, drops.All[0].FeetMeters.Y, 0.01);
    }

    [Fact]
    public void a_block_that_broke_is_picked_up_by_a_player_standing_over_it()
    {
        var drops = new Drops(seed: 1);
        var inventory = new Inventory(_blocks);
        drops.Spawn(_blocks.Dirt, new int3(4, 11, 4));

        Run(drops, seconds: 3.0, playerAt: new double3(4.5, 11.0, 4.5), inventory);

        Assert.Equal(0, drops.Count);
        Assert.Equal((_blocks.Dirt, 1), inventory.At(0));
    }

    [Fact]
    public void a_block_that_broke_far_away_is_left_where_it_fell()
    {
        var drops = new Drops(seed: 1);
        var inventory = new Inventory(_blocks);
        drops.Spawn(_blocks.Dirt, new int3(4, 11, 4));

        Run(drops, seconds: 3.0, playerAt: Far, inventory);

        Assert.Equal(1, drops.Count);
        Assert.False(inventory.HasSelection);
    }

    [Fact]
    public void a_block_is_not_picked_up_on_the_frame_it_broke()
    {
        // Otherwise breaking a block is indistinguishable from it vanishing into the row, and
        // the whole point of a drop is that something visibly happened.
        var drops = new Drops(seed: 1);
        var inventory = new Inventory(_blocks);
        drops.Spawn(_blocks.Dirt, new int3(4, 11, 4));

        drops.Step(_world, _blocks.Registry, Step, new double3(4.5, 11.0, 4.5), inventory);

        Assert.Equal(1, drops.Count);
        Assert.False(inventory.HasSelection);
    }

    [Fact]
    public void a_drop_a_full_row_cannot_take_stays_on_the_ground()
    {
        var drops = new Drops(seed: 1);
        var inventory = new Inventory(_blocks);
        for (var slot = 0; slot < Inventory.SlotCount; slot++)
            inventory.Add(_blocks.Stone, Inventory.StackSize);

        drops.Spawn(_blocks.Dirt, new int3(4, 11, 4));
        Run(drops, seconds: 3.0, playerAt: new double3(4.5, 11.0, 4.5), inventory);

        Assert.Equal(1, drops.Count);
    }

    [Fact]
    public void several_blocks_broken_together_do_not_land_on_the_same_spot()
    {
        var drops = new Drops(seed: 7);
        for (var dropped = 0; dropped < 4; dropped++)
            drops.Spawn(_blocks.Stone, new int3(4, 20, 4));

        Run(drops, seconds: 4.0, playerAt: Far);

        var places = drops.All.Select(drop => (drop.FeetMeters.X, drop.FeetMeters.Z)).Distinct().Count();
        Assert.Equal(drops.Count, places);
    }

    [Fact]
    public void air_is_never_dropped()
    {
        var drops = new Drops(seed: 1);

        drops.Spawn(BlockId.Air, new int3(0, 20, 0));

        Assert.Equal(0, drops.Count);
    }

    [Fact]
    public void clearing_forgets_everything_lying_about()
    {
        var drops = new Drops(seed: 1);
        drops.Spawn(_blocks.Stone, new int3(4, 20, 4));

        drops.Clear();

        Assert.Equal(0, drops.Count);
    }

    /// <summary>Somewhere no drop could ever be picked up from.</summary>
    private static double3 Far => new(500.0, 11.0, 500.0);

    private void Run(Drops drops, double seconds, double3 playerAt, Inventory? inventory = null)
    {
        var row = inventory ?? new Inventory(_blocks);
        for (var tick = 0; tick < seconds / Step; tick++)
            drops.Step(_world, _blocks.Registry, Step, playerAt, row);
    }

    /// <summary>One solid layer, unbounded sideways.</summary>
    private sealed class FloorAt(int level, BlockId block) : IVoxelSource
    {
        public BlockId BlockAt(int x, int y, int z) => y == level ? block : BlockId.Air;
    }
}
