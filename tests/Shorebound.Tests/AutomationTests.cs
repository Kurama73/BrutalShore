namespace Shorebound.Tests;

/// <summary>
/// Machines in a row: that a belt carries, that it backs up rather than piling, that a smelter
/// can be fed by one, and that a drill on a seam keeps a whole line fed by itself.
/// </summary>
/// <remarks>
/// These are the claims the factory half of the game stands on, and they are claims about several
/// machines at once rather than about any of them. The one that matters most is the last: a line
/// that runs end to end with nobody touching it is the whole promise, and everything before it is
/// a machine that happens to work.
/// </remarks>
public sealed class AutomationTests
{
    private readonly GameBlocks _blocks = new(new BlockRegistry());
    private readonly Smelting _smelting;
    private readonly VoxelWorld _world;
    private readonly Machines _machines;

    public AutomationTests()
    {
        _smelting = new Smelting(_blocks);
        _world = new VoxelWorld(_blocks.Registry, new SlabGenerator(_blocks.Stone, 20));
        _machines = new Machines(_smelting);

        for (var chunkY = 0; chunkY < VoxelWorld.ChunksTall; chunkY++)
            _world.Load(new int3(0, chunkY, 0));
    }

    [Fact]
    public void a_belt_carries_what_is_put_on_it_to_the_one_in_front()
    {
        var first = Belt(new int3(1, 21, 1), Facing.East);
        var second = Belt(new int3(2, 21, 1), Facing.East);

        Assert.True(first.Offer(_blocks.CoalOre));

        Run(4.0);

        Assert.Equal(0, first.Count);
        Assert.Equal(1, second.Count);
    }

    [Fact]
    public void a_belt_with_nothing_in_front_holds_on_to_what_it_has()
    {
        // Rather than dropping it on the floor, which is the one thing a player who mislays a
        // belt must not be punished with.
        var belt = Belt(new int3(1, 21, 1), Facing.East);
        belt.Offer(_blocks.IronOre);

        Run(6.0);

        Assert.Equal(1, belt.Count);
    }

    [Fact]
    public void items_on_a_belt_queue_up_behind_each_other()
    {
        var belt = Belt(new int3(1, 21, 1), Facing.East);

        for (var item = 0; item < Conveyor.Capacity; item++)
        {
            Assert.True(belt.Offer(_blocks.Stone), $"item {item} would not fit");
            Run(0.3);
        }

        Run(4.0);

        // Full and stopped, with nowhere to go, and still exactly as many as went on.
        Assert.Equal(Conveyor.Capacity, belt.Count);
        Assert.False(belt.Offer(_blocks.Stone));
    }

    [Fact]
    public void a_belt_takes_the_output_of_the_machine_behind_it()
    {
        var smelter = Smelter(new int3(1, 21, 1));
        var belt = Belt(new int3(2, 21, 1), Facing.East);

        smelter.Input.Add(_blocks.CopperOre, 3);
        smelter.Fuel.Add(_blocks.CoalOre, 1);

        Run(20.0);

        Assert.True(belt.Count > 0 || smelter.Output.At(0).IsNothing is false);
        Assert.Contains(belt.Items, item => item.Block == _blocks.CopperIngot);
    }

    [Fact]
    public void a_belt_feeds_the_smelter_in_front_of_it_both_ore_and_fuel()
    {
        // One line carrying both is how a player actually lays a factory out, and it only works
        // because the smelter decides which of its slots a block belongs in.
        var belt = Belt(new int3(1, 21, 1), Facing.East);
        var smelter = Smelter(new int3(2, 21, 1));

        belt.Offer(_blocks.CoalOre);
        Run(1.0);
        belt.Offer(_blocks.IronOre);

        Run(30.0);

        Assert.Equal(_blocks.IronIngot, smelter.Output.At(0).Block);
    }

    [Fact]
    public void a_belt_does_not_take_back_what_the_machine_in_front_is_working_on()
    {
        var belt = Belt(new int3(1, 21, 1), Facing.East);
        var smelter = Smelter(new int3(2, 21, 1));

        smelter.Input.Add(_blocks.IronOre, 4);
        smelter.Fuel.Add(_blocks.CoalOre, 2);

        Run(3.0);

        Assert.Equal(0, belt.Count);
    }

    [Fact]
    public void a_drill_on_a_seam_keeps_bringing_it_up()
    {
        _world.SetBlock(new int3(4, 20, 4), _blocks.IronOre);
        var miner = Drill(new int3(4, 21, 4));
        miner.Fuel.Add(_blocks.CoalOre, 2);

        Run(30.0);

        Assert.Equal(_blocks.IronOre, miner.Output.At(0).Block);
        Assert.True(miner.Output.At(0).Count >= 3);
    }

    [Fact]
    public void a_drill_does_not_eat_the_seam_it_stands_on()
    {
        // A seam that ran out would turn the interesting question — is this worth running a line
        // out to — into a countdown.
        _world.SetBlock(new int3(4, 20, 4), _blocks.CopperOre);
        var miner = Drill(new int3(4, 21, 4));
        miner.Fuel.Add(_blocks.CoalOre, 4);

        Run(60.0);

        Assert.Equal(_blocks.CopperOre, _world.BlockAt(4, 20, 4));
    }

    [Fact]
    public void a_drill_with_no_fuel_does_nothing()
    {
        _world.SetBlock(new int3(4, 20, 4), _blocks.IronOre);
        var miner = Drill(new int3(4, 21, 4));

        Run(30.0);

        Assert.True(miner.Output.At(0).IsNothing);
    }

    [Fact]
    public void a_drill_standing_on_nothing_burns_nothing()
    {
        _world.SetBlock(new int3(4, 20, 4), BlockId.Air);
        var miner = Drill(new int3(4, 21, 4));
        miner.Fuel.Add(_blocks.CoalOre, 3);

        Run(40.0);

        Assert.Equal(3, miner.Fuel.At(0).Count);
    }

    [Fact]
    public void a_drill_a_belt_and_a_smelter_run_a_whole_line_by_themselves()
    {
        // The claim the factory half of this game exists for. Nothing in this test touches the
        // machines after they are set up: ore comes out of the ground, rides a belt, goes into a
        // fire and comes out as metal, with nobody there.
        _world.SetBlock(new int3(1, 20, 1), _blocks.CopperOre);

        var miner = Drill(new int3(1, 21, 1));
        Belt(new int3(2, 21, 1), Facing.East);
        Belt(new int3(3, 21, 1), Facing.East);
        var smelter = Smelter(new int3(4, 21, 1));

        miner.Fuel.Add(_blocks.CoalOre, 4);
        smelter.Fuel.Add(_blocks.CoalOre, 4);

        Run(90.0);

        Assert.Equal(_blocks.CopperIngot, smelter.Output.At(0).Block);
        Assert.True(smelter.Output.At(0).Count >= 2, $"only {smelter.Output.At(0).Count} came out");
    }

    [Fact]
    public void breaking_a_belt_gives_back_what_was_on_it()
    {
        var belt = Belt(new int3(1, 21, 1), Facing.East);
        belt.Offer(_blocks.IronOre);

        // A moment between the two, because a belt refuses a second item in the same place —
        // which is the whole reason a line has a throughput rather than a capacity.
        Run(0.3);
        belt.Offer(_blocks.CoalOre);

        var contents = belt.Contents().ToList();

        Assert.Equal(2, contents.Count);
        Assert.Equal(2, contents.Sum(stack => stack.Count));
    }

    [Fact]
    public void how_fast_a_line_runs_does_not_depend_on_the_frame_rate()
    {
        // The property that stops a better computer being a better factory.
        Assert.Equal(Delivered(1.0 / 20.0), Delivered(1.0 / 120.0));
    }

    private int Delivered(double step)
    {
        var smelting = new Smelting(_blocks);
        var machines = new Machines(smelting);

        var belt = new Conveyor(Facing.East);
        machines.Place(new int3(1, 21, 1), belt);
        var into = new Conveyor(Facing.East);
        machines.Place(new int3(2, 21, 1), into);

        for (var item = 0; item < Conveyor.Capacity; item++)
            belt.Offer(_blocks.Stone);

        for (var elapsed = 0.0; elapsed < 6.0; elapsed += step)
            machines.Step(step, _world);

        return into.Count;
    }

    private Conveyor Belt(int3 at, Facing facing)
    {
        var belt = new Conveyor(facing);
        _machines.Place(at, belt);
        return belt;
    }

    private Smelter Smelter(int3 at)
    {
        var smelter = new Smelter();
        _machines.Place(at, smelter);
        return smelter;
    }

    private Miner Drill(int3 at)
    {
        var miner = new Miner();
        _machines.Place(at, miner);
        return miner;
    }

    private void Run(double seconds)
    {
        const double Step = 1.0 / 60.0;

        for (var elapsed = 0.0; elapsed < seconds; elapsed += Step)
            _machines.Step(Step, _world);
    }
}
