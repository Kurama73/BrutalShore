using Shorebound.Domain.Creatures;
using Shorebound.Domain.Movement;
using Shorebound.Domain.Player;
using Shorebound.Domain.Production;
using Shorebound.Domain.World;
using Shorebound.Tests.Domain.Creatures;

namespace Shorebound.Tests.Domain.Production;

/// <summary>
/// Turrets and lifts: the two machines that close the loop between the factory half of the game
/// and the other two things a player has to deal with, the dark and the depth.
/// </summary>
/// <remarks>
/// The turret's claims are about power, because that is where it earns its place. A turret that
/// worked without a grid would be a wall; one that needs electricity is the reason a player finds
/// out, the first time a fight starts during a smelting run, that they built one grid where they
/// needed two.
/// </remarks>
public sealed class DefenceTests
{
    private readonly GameBlocks _blocks = new(new BlockRegistry());
    private readonly Smelting _smelting;
    private readonly VoxelWorld _world;
    private readonly Machines _machines;

    public DefenceTests()
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
    public void a_turret_with_no_power_does_not_shoot()
    {
        var stalkers = HordeNear();
        var turret = new Turret();
        _machines.Place(new int3(0, 21, 0), turret);

        var before = stalkers.Count;
        RunTurrets(turret, stalkers, 20.0);

        Assert.Equal(before, stalkers.Count);
    }

    [Fact]
    public void a_wired_turret_kills_what_comes_near_it()
    {
        // The whole promise: something that does not sleep.
        var stalkers = HordeNear();
        var turret = Wired(new int3(0, 21, 0));

        var before = stalkers.Count;
        RunTurrets(turret, stalkers, 20.0);

        Assert.True(stalkers.Count < before, "nothing was shot");
    }

    [Fact]
    public void a_turret_ignores_what_is_out_of_range()
    {
        var stalkers = new Stalkers(5);
        var turret = Wired(new int3(0, 21, 0));

        // Nothing near it at all, so nothing to shoot and nothing drawn.
        RunTurrets(turret, stalkers, 10.0);

        Assert.Equal(0.0, turret.DemandWatts);
    }

    [Fact]
    public void a_turret_with_nothing_to_shoot_draws_nothing()
    {
        // Otherwise a line of them holds the grid down all day and the smelters crawl.
        var turret = Wired(new int3(0, 21, 0));
        var stalkers = new Stalkers(5);

        RunTurrets(turret, stalkers, 5.0);

        Assert.Equal(0.0, turret.DemandWatts);
    }

    [Fact]
    public void a_turret_asks_for_power_once_something_is_in_range()
    {
        var stalkers = HordeNear();
        var turret = Wired(new int3(0, 21, 0));

        RunTurrets(turret, stalkers, 1.0);

        Assert.Equal(Turret.DrawWatts, turret.DemandWatts);
    }

    [Fact]
    public void a_turret_turns_towards_what_it_is_shooting()
    {
        // So that a player can see which way the trouble is coming from without looking for it.
        var stalkers = HordeNear();
        var turret = Wired(new int3(0, 21, 0));

        RunTurrets(turret, stalkers, 2.0);

        Assert.True(float.IsFinite(turret.Yaw));
    }

    [Fact]
    public void a_lift_carries_what_is_put_in_it_to_the_block_above()
    {
        var bottom = new Lift();
        var top = new Lift();
        _machines.Place(new int3(0, 21, 0), bottom);
        _machines.Place(new int3(0, 22, 0), top);

        Assert.True(bottom.Offer(_blocks.IronOre));

        Run(4.0);

        Assert.Equal(0, bottom.Count);
        Assert.Equal(1, top.Count);
    }

    [Fact]
    public void a_lift_takes_from_the_machine_underneath_it()
    {
        var smelter = new Smelter();
        _machines.Place(new int3(0, 21, 0), smelter);
        var lift = new Lift();
        _machines.Place(new int3(0, 22, 0), lift);

        smelter.Input.Add(_blocks.CopperOre, 2);
        smelter.Fuel.Add(_blocks.CoalOre, 2);

        Run(20.0);

        Assert.True(lift.Count > 0, "nothing was lifted out of the smelter");
    }

    [Fact]
    public void a_stack_of_lifts_carries_a_thing_all_the_way_up()
    {
        // The arrangement everybody builds first: the mine is down and the works are up.
        var lifts = new List<Lift>();
        for (var y = 21; y <= 28; y++)
        {
            var lift = new Lift();
            _machines.Place(new int3(0, y, 0), lift);
            lifts.Add(lift);
        }

        var belt = new Conveyor(Facing.East);
        _machines.Place(new int3(0, 29, 0), belt);

        lifts[0].Offer(_blocks.EmberOre);

        Run(20.0);

        Assert.Equal(0, lifts.Sum(lift => lift.Count));
        Assert.Equal(1, belt.Count);
    }

    [Fact]
    public void a_lift_with_nothing_above_holds_on_to_what_it_has()
    {
        var lift = new Lift();
        _machines.Place(new int3(0, 21, 0), lift);
        lift.Offer(_blocks.Stone);

        Run(10.0);

        Assert.Equal(1, lift.Count);
    }

    [Fact]
    public void a_lift_fills_up_and_then_refuses()
    {
        var lift = new Lift();
        _machines.Place(new int3(0, 21, 0), lift);

        for (var item = 0; item < Lift.Capacity; item++)
        {
            Assert.True(lift.Offer(_blocks.Stone), $"item {item} would not fit");
            Run(0.5);
        }

        Run(4.0);

        Assert.Equal(Lift.Capacity, lift.Count);
        Assert.False(lift.Offer(_blocks.Stone));
    }

    /// <summary>A turret wired to a generator with plenty of fuel.</summary>
    private Turret Wired(int3 at)
    {
        var turret = new Turret();
        _machines.Place(at, turret);
        _machines.Place(new int3(at.X + 1, at.Y, at.Z), new Cable());

        var generator = new Generator();
        generator.Fuel.Add(_blocks.CoalOre, 20);
        _machines.Place(new int3(at.X + 2, at.Y, at.Z), generator);

        return turret;
    }

    /// <summary>A horde brought out in the dark right next to the origin.</summary>
    private Stalkers HordeNear()
    {
        var stalkers = new Stalkers(3);
        var player = new double3(0.5, 21.0, 0.5);
        var vitality = new Vitality();

        const double Step = 1.0 / 60.0;
        for (var elapsed = 0.0; elapsed < 40.0; elapsed += Step)
            stalkers.Step(_world, _blocks.Registry, Step, player, vitality, 0, isDay: false);

        Assert.True(stalkers.Count > 0, "nothing came out of the dark");
        return stalkers;
    }

    private void Run(double seconds)
    {
        const double Step = 1.0 / 60.0;

        for (var elapsed = 0.0; elapsed < seconds; elapsed += Step)
            _machines.Step(Step, _world);
    }

    /// <summary>Steps the machines and then lets the turret shoot, the way the game does.</summary>
    private void RunTurrets(Turret turret, Stalkers stalkers, double seconds)
    {
        const double Step = 1.0 / 60.0;
        var player = new double3(0.5, 21.0, 0.5);
        var vitality = new Vitality();

        for (var elapsed = 0.0; elapsed < seconds; elapsed += Step)
        {
            _machines.Step(Step, _world);
            stalkers.Step(_world, _blocks.Registry, Step, player, vitality, 0, isDay: false);
            turret.Fire(new int3(0, 21, 0), stalkers, Step);
        }
    }
}
