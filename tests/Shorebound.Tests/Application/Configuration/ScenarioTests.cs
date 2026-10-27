using Shorebound.Application.Configuration;
using Shorebound.Domain.Creatures;
using Shorebound.Domain.Movement;
using Shorebound.Domain.Production;
using Shorebound.Domain.World;
using Shorebound.Tests.Domain.Creatures;

namespace Shorebound.Tests.Application.Configuration;

/// <summary>
/// The prepared works: that what it builds is wired to itself, and that leaving it alone turns
/// rock into metal.
/// </summary>
/// <remarks>
/// A scenario exists to be watched running in the real game, which is a thing no test can do. What
/// a test can do is everything up to the window: that the machines are placed where the next one
/// expects them, that the grid finds them, and that stepping the whole arrangement produces what
/// it is supposed to produce. If that holds here, a run that shows nothing is a renderer problem
/// rather than a works problem — which is the distinction that makes a screenshot worth taking.
/// </remarks>
public sealed class ScenarioTests
{
    private readonly GameBlocks _blocks = new(new BlockRegistry());
    private readonly Smelting _smelting;
    private readonly VoxelWorld _world;
    private readonly Machines _machines;

    public ScenarioTests()
    {
        _smelting = new Smelting(_blocks);
        _world = new VoxelWorld(_blocks.Registry, new SlabGenerator(_blocks.Stone, 20));
        _machines = new Machines(_smelting);

        for (var chunkZ = -2; chunkZ <= 2; chunkZ++)
        for (var chunkX = -2; chunkX <= 2; chunkX++)
        for (var chunkY = 0; chunkY < VoxelWorld.ChunksTall; chunkY++)
            _world.Load(new int3(chunkX, chunkY, chunkZ));
    }

    [Fact]
    public void every_name_on_the_command_line_is_a_scenario()
    {
        foreach (var name in Scenario.Names)
            Assert.NotNull(Scenario.Named(name));

        Assert.Null(Scenario.Named("a works that does not exist"));
    }

    [Fact]
    public void a_works_stands_on_top_of_the_ground_rather_than_inside_it()
    {
        var corner = new Works().Build(_blocks, _world, _machines, Scenario.CornerAt(0));

        // Asked for at the bottom of the world and still laid on the surface, because a works at
        // the height of one corner of a slope is a works with a hillside through the middle.
        Assert.Equal(21, corner.Y);
    }

    [Fact]
    public void a_works_puts_a_seam_under_the_drill()
    {
        var corner = Build(new Works());
        var under = new int3(corner.X, corner.Y - 1, corner.Z);

        // The drill reads what is beneath it and leaves it there, so one block of ore is a works
        // that runs for as long as it is fed.
        Assert.Equal(_blocks.IronOre, _world.BlockAt(under.X, under.Y, under.Z));
        Assert.IsType<Miner>(_machines.At(corner));
    }

    [Fact]
    public void the_drill_feeds_the_fire_along_the_belts()
    {
        var corner = Build(new Works());

        Assert.IsType<Conveyor>(_machines.At(corner + new int3(1, 0, 0)));
        Assert.IsType<Conveyor>(_machines.At(corner + new int3(2, 0, 0)));
        Assert.IsType<Smelter>(_machines.At(corner + new int3(3, 0, 0)));

        // Each belt has to point at the next thing along, or the line is three machines beside
        // each other rather than a line.
        Assert.Equal(Facing.East, ((Conveyor)_machines.At(corner + new int3(1, 0, 0))!).Facing);
        Assert.Equal(Facing.East, ((Conveyor)_machines.At(corner + new int3(2, 0, 0))!).Facing);
    }

    [Fact]
    public void one_fire_runs_the_whole_works_down_a_wire()
    {
        Build(new Works());

        // A fire with nothing in it draws nothing, so the second consumer only appears once the
        // drill has dug something and the belts have carried it the length of the line.
        Step(2.0);
        Assert.Equal(Miner.DrawWatts, _machines.Power.DemandedWatts, 1e-6);

        Step(90.0);

        // The drill and the fire both drawing, out of one generator, through the cable between
        // them: ninety watts each is the whole of the game's middle in one number.
        Assert.Equal(Miner.DrawWatts + Smelter.DrawWatts, _machines.Power.DemandedWatts, 1e-6);
        Assert.Equal(_machines.Power.DemandedWatts, _machines.Power.DeliveredWatts, 1e-6);
    }

    [Fact]
    public void leaving_the_works_alone_turns_rock_into_metal()
    {
        var corner = Build(new Works());

        Step(180.0);

        var smelter = (Smelter)_machines.At(corner + new int3(3, 0, 0))!;
        Assert.True(
            smelter.Output.CountOf(_blocks.IronIngot) > 0,
            "three minutes of a fully wired works made no iron at all"
        );
    }

    [Fact]
    public void the_night_works_puts_something_on_the_wire_to_defend_it()
    {
        var corner = Build(new Night());

        Step(2.0);

        var turret = _machines.At(corner + new int3(5, 0, 1));
        Assert.IsType<Turret>(turret);

        // On the same run as everything else. A turret that needed a generator of its own would
        // be a turret nobody builds.
        Assert.True(_machines.Power.IsWired(corner + new int3(5, 0, 1)), "the turret is on nothing");
    }

    [Fact]
    public void a_report_says_what_the_works_actually_did()
    {
        var scenario = new Works();
        Build(scenario);

        Step(180.0);

        var report = scenario.Report(_blocks, _machines, new Stalkers(1));

        Assert.Contains("ingots made", report, StringComparison.Ordinal);
        Assert.DoesNotContain("0 ingots made", report, StringComparison.Ordinal);
    }

    private int3 Build(Scenario scenario) => scenario.Build(_blocks, _world, _machines, Scenario.CornerAt(20));

    /// <summary>Steps the works the way the game does, at the game's own rate.</summary>
    private void Step(double seconds)
    {
        const double Tick = 1.0 / 60.0;

        for (var elapsed = 0.0; elapsed < seconds; elapsed += Tick)
            _machines.Step(Tick, _world);
    }
}
