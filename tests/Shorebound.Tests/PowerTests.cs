namespace Shorebound.Tests;

/// <summary>
/// The grid: who is wired to whom, who gets how much when there is not enough, and what a
/// machine does when the wire goes dead.
/// </summary>
/// <remarks>
/// The claims that matter are the two that make a grid worth building. Wiring a machine has to
/// stop it needing its own fuel, or there is no reason to run cable; and a grid that is short has
/// to slow everything on it down equally, because a factory that is visibly struggling tells a
/// player to build another generator and one where an arbitrary third of the machines stopped
/// tells them nothing.
/// </remarks>
public sealed class PowerTests
{
    private readonly GameBlocks _blocks = new(new BlockRegistry());
    private readonly Smelting _smelting;
    private readonly VoxelWorld _world;
    private readonly Machines _machines;

    public PowerTests()
    {
        _smelting = new Smelting(_blocks);
        _world = new VoxelWorld(_blocks.Registry, new SlabGenerator(_blocks.Stone, 20));
        _machines = new Machines(_smelting);

        for (var chunkY = 0; chunkY < VoxelWorld.ChunksTall; chunkY++)
            _world.Load(new int3(0, chunkY, 0));
    }

    [Fact]
    public void a_world_with_no_cable_has_no_grids()
    {
        Smelter(new int3(1, 21, 1));

        Run(1.0);

        Assert.Equal(0, _machines.Power.NetworkCount);
    }

    [Fact]
    public void a_run_of_cable_is_one_grid()
    {
        for (var x = 1; x <= 6; x++)
            _machines.Place(new int3(x, 21, 1), new Cable());

        Run(0.1);

        Assert.Equal(1, _machines.Power.NetworkCount);
    }

    [Fact]
    public void two_runs_that_do_not_touch_are_two_grids()
    {
        // Which is what lets a player keep the turrets off the smelters' generator.
        _machines.Place(new int3(1, 21, 1), new Cable());
        _machines.Place(new int3(2, 21, 1), new Cable());
        _machines.Place(new int3(9, 21, 1), new Cable());

        Run(0.1);

        Assert.Equal(2, _machines.Power.NetworkCount);
    }

    [Fact]
    public void a_machine_between_two_runs_does_not_join_them()
    {
        // A machine is not a conductor. The rule has to be one a player can see by looking at
        // where they put cable, and "cable touches cable" is that rule.
        _machines.Place(new int3(1, 21, 1), new Cable());
        Smelter(new int3(2, 21, 1));
        _machines.Place(new int3(3, 21, 1), new Cable());

        Run(0.1);

        Assert.Equal(2, _machines.Power.NetworkCount);
    }

    [Fact]
    public void a_wired_smelter_melts_without_any_fuel_of_its_own()
    {
        // The whole reason to run cable.
        var smelter = Smelter(new int3(2, 21, 1));
        _machines.Place(new int3(1, 21, 1), new Cable());
        var generator = Generator(new int3(0, 21, 1));

        generator.Fuel.Add(_blocks.CoalOre, 4);
        smelter.Input.Add(_blocks.CopperOre, 2);

        Run(20.0);

        Assert.Equal(_blocks.CopperIngot, smelter.Output.At(0).Block);
        Assert.True(smelter.Fuel.At(0).IsNothing);
    }

    [Fact]
    public void an_unwired_machine_falls_back_to_burning()
    {
        // A half-wired factory has to work, or laying cable becomes an all-or-nothing job.
        var smelter = Smelter(new int3(2, 21, 1));
        smelter.Input.Add(_blocks.CopperOre, 1);
        smelter.Fuel.Add(_blocks.CoalOre, 1);

        Run(20.0);

        Assert.Equal(_blocks.CopperIngot, smelter.Output.At(0).Block);
    }

    [Fact]
    public void a_generator_with_nothing_drawing_on_it_burns_nothing()
    {
        // Otherwise it eats a player's coal while they sleep, and they cannot see it happening.
        _machines.Place(new int3(1, 21, 1), new Cable());
        var generator = Generator(new int3(0, 21, 1));
        generator.Fuel.Add(_blocks.CoalOre, 5);

        Run(60.0);

        Assert.Equal(5, generator.Fuel.At(0).Count);
    }

    [Fact]
    public void a_generator_with_no_fuel_supplies_nothing()
    {
        var smelter = Smelter(new int3(2, 21, 1));
        _machines.Place(new int3(1, 21, 1), new Cable());
        Generator(new int3(0, 21, 1));

        smelter.Input.Add(_blocks.IronOre, 1);

        Run(20.0);

        Assert.True(smelter.Output.At(0).IsNothing);
    }

    [Fact]
    public void a_grid_that_is_short_slows_everything_on_it_down_together()
    {
        // A brownout, which is far better than a cut-out: the factory visibly struggles
        // everywhere rather than stopping in an arbitrary half.
        var cables = new List<int3>();
        for (var x = 0; x < 9; x++)
            cables.Add(new int3(x, 21, 1));

        foreach (var at in cables)
            _machines.Place(at, new Cable());

        var generator = Generator(new int3(0, 22, 1));
        generator.Fuel.Add(_blocks.CoalOre, 40);

        // Eight smelters on one generator, which covers four.
        var smelters = new List<Smelter>();
        for (var x = 0; x < 8; x++)
        {
            var smelter = Smelter(new int3(x, 20, 1));
            smelter.Input.Add(_blocks.Sand, 20);
            smelters.Add(smelter);
        }

        Run(30.0);

        var made = smelters.Select(smelter => smelter.Output.At(0).Count).ToList();

        Assert.All(made, count => Assert.True(count > 0, "a smelter on a short grid made nothing"));
        Assert.True(made.Max() - made.Min() <= 1, $"they drifted apart: {string.Join(',', made)}");
    }

    [Fact]
    public void a_grid_delivers_what_it_can_and_says_so()
    {
        _machines.Place(new int3(1, 21, 1), new Cable());
        var generator = Generator(new int3(0, 21, 1));
        generator.Fuel.Add(_blocks.CoalOre, 10);

        var smelter = Smelter(new int3(2, 21, 1));
        smelter.Input.Add(_blocks.Sand, 10);

        Run(2.0);

        Assert.Equal(Shorebound.Smelter.DrawWatts, _machines.Power.DemandedWatts, 1.0);
        Assert.Equal(Shorebound.Smelter.DrawWatts, _machines.Power.DeliveredWatts, 1.0);
    }

    [Fact]
    public void an_idle_machine_does_not_hold_the_grid_down()
    {
        // A row of smelters with nothing in them must let the one that is working run at full
        // speed, rather than all of them running at a twelfth.
        for (var x = 0; x < 10; x++)
            _machines.Place(new int3(x, 21, 1), new Cable());

        var generator = Generator(new int3(0, 22, 1));
        generator.Fuel.Add(_blocks.CoalOre, 20);

        for (var x = 1; x < 9; x++)
            Smelter(new int3(x, 20, 1));

        var working = Smelter(new int3(9, 20, 1));
        working.Input.Add(_blocks.Sand, 8);

        Run(2.0);

        Assert.Equal(Shorebound.Smelter.DrawWatts, _machines.Power.DemandedWatts, 1.0);
    }

    [Fact]
    public void a_wired_drill_works_without_fuel_of_its_own()
    {
        _world.SetBlock(new int3(4, 20, 4), _blocks.IronOre);

        var miner = new Miner();
        _machines.Place(new int3(4, 21, 4), miner);
        _machines.Place(new int3(5, 21, 4), new Cable());
        var generator = Generator(new int3(6, 21, 4));
        generator.Fuel.Add(_blocks.CoalOre, 6);

        Run(20.0);

        Assert.Equal(_blocks.IronOre, miner.Output.At(0).Block);
    }

    [Fact]
    public void breaking_the_cable_takes_the_power_away()
    {
        var smelter = Smelter(new int3(2, 21, 1));
        _machines.Place(new int3(1, 21, 1), new Cable());
        var generator = Generator(new int3(0, 21, 1));

        generator.Fuel.Add(_blocks.CoalOre, 8);
        smelter.Input.Add(_blocks.Sand, 20);

        Run(10.0);
        var madeWired = smelter.Output.At(0).Count;
        Assert.True(madeWired > 0);

        _machines.Remove(new int3(1, 21, 1));
        Run(10.0);

        Assert.Equal(madeWired, smelter.Output.At(0).Count);
    }

    private Smelter Smelter(int3 at)
    {
        var smelter = new Smelter();
        _machines.Place(at, smelter);
        return smelter;
    }

    private Generator Generator(int3 at)
    {
        var generator = new Generator();
        _machines.Place(at, generator);
        return generator;
    }

    private void Run(double seconds)
    {
        const double Step = 1.0 / 60.0;

        for (var elapsed = 0.0; elapsed < seconds; elapsed += Step)
            _machines.Step(Step, _world);
    }
}
