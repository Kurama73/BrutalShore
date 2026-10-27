using Shorebound.Application.Automation;
using Shorebound.Domain.Creatures;
using Shorebound.Domain.Movement;
using Shorebound.Domain.Player;
using Shorebound.Domain.Production;
using Shorebound.Domain.Progression;
using Shorebound.Domain.World;
using Shorebound.Tests.Domain.Creatures;

namespace Shorebound.Tests.Application.Automation;

/// <summary>
/// The whole game played through once, from arriving with nothing to a works that defends
/// itself — every stage driven through the same objects the game drives.
/// </summary>
/// <remarks>
/// <para>
/// Every other test here asserts one thing in isolation. This one exists because a game is not
/// its parts: a smelter that works, a milestone that unlocks and a turret that fires are three
/// facts that can all be true while the game is still unplayable, because the costs do not line
/// up, or the thing a tier unlocks cannot be reached with what the tier before it gave you.
/// </para>
/// <para>
/// So this plays it. It mines, crafts, hands in, builds, wires, and survives a night, and it
/// asserts at each stage that what the player now has is what the next stage needs. When a
/// constant moves — the cost of a belt, how long iron takes, how much a milestone asks for —
/// this is what says whether the game is still a game.
/// </para>
/// </remarks>
public sealed class WholeLoopTests
{
    private readonly GameBlocks _blocks = new(new BlockRegistry());
    private readonly Smelting _smelting;
    private readonly Recipes _recipes;
    private readonly Progression _progression;
    private readonly Inventory _inventory;
    private readonly Bench _bench;
    private readonly VoxelWorld _world;
    private readonly Machines _machines;

    public WholeLoopTests()
    {
        _smelting = new Smelting(_blocks);
        _recipes = new Recipes(_blocks);
        _progression = new Progression(_blocks);
        _inventory = new Inventory(_blocks);
        _bench = new Bench(_recipes, _progression);
        _world = new VoxelWorld(_blocks.Registry, new SlabGenerator(_blocks.Stone, 20));
        _machines = new Machines(_smelting);

        for (var chunkZ = -2; chunkZ <= 2; chunkZ++)
        for (var chunkX = -2; chunkX <= 2; chunkX++)
        for (var chunkY = 0; chunkY < VoxelWorld.ChunksTall; chunkY++)
            _world.Load(new int3(chunkX, chunkY, chunkZ));
    }

    [Fact]
    public void a_whole_game_can_be_played_from_nothing_to_a_works_that_defends_itself()
    {
        // ---- 1. Ashore with nothing. ------------------------------------
        Assert.False(_inventory.HasSelection);
        Assert.Equal(0, _progression.DoneCount);
        Assert.False(_progression.IsUnlocked("smelter"));

        // ---- 2. Cut wood and break stone, and make the first two things. --
        Gather(_blocks.Log, 20);
        Gather(_blocks.Stone, 48);

        Craft("planks", times: 5);
        Craft("stone brick", times: 12);

        Assert.True(_inventory.CountOf(_blocks.Planks) >= 16, "not enough planks for the first milestone");
        Assert.True(_inventory.CountOf(_blocks.StoneBrick) >= 24, "not enough brick for the first milestone");

        // ---- 3. Make landfall, which opens the smelter. -------------------
        Assert.Equal("Make landfall", HandIn("Make landfall"));
        Assert.True(_progression.IsUnlocked("smelter"));

        // ---- 4. Build one, feed it copper, and get metal out of it. -------
        // The bricks left over from the milestone are exactly what the smelter is made of,
        // which is the tier paying for the next one.
        Craft("smelter", times: 1);
        Assert.Equal(1, _inventory.CountOf(_blocks.Smelter));

        var smelter = Build(new int3(0, 21, 0), _blocks.Smelter, new Smelter());

        Smelt(smelter, _blocks.CopperOre, 24);

        Assert.True(_inventory.CountOf(_blocks.CopperIngot) >= 20, "the smelter did not keep up");

        // ---- 5. First smoke, then the belt, then the lift. ----------------
        Assert.Equal("First smoke", HandIn("First smoke"));
        Assert.True(_progression.IsUnlocked("copper block"));

        Gather(_blocks.EmberOre, 8);
        Assert.Equal("Into the dark", HandIn("Into the dark"));
        Assert.True(_progression.IsUnlocked("lantern"));

        // The lantern is the thing the whole resource layer was built for: it exists, it lights
        // a cave, and until this point no player could ever hold one.
        Craft("planks", times: 2);
        Craft("lantern", times: 1);
        Assert.True(_inventory.CountOf(_blocks.Lantern) > 0, "the lantern is still unreachable");

        // ---- 6. Iron, which is the deepest thing the shore asks for. ------
        // Iron is the long one: a stack of sixty-four is nearly ten minutes of fire. Smelted in
        // stacks because that is the size of the slot, which is the real reason a player builds
        // a second smelter rather than waiting.
        // Four stacks, which is what the rest of the line costs once the blocks the last two
        // milestones want are counted: eight iron blocks is seventy-two ingots on its own.
        Smelt(smelter, _blocks.IronOre, 64);
        Smelt(smelter, _blocks.IronOre, 64);
        Smelt(smelter, _blocks.IronOre, 64);
        Smelt(smelter, _blocks.IronOre, 64);

        Assert.True(_inventory.CountOf(_blocks.IronIngot) >= 24, "the smelter could not make the iron the shore asks for");

        Smelt(smelter, _blocks.CopperOre, 64);
        Smelt(smelter, _blocks.CopperOre, 64);
        Smelt(smelter, _blocks.CopperOre, 64);

        Gather(_blocks.Log, 20);
        Craft("planks", times: 5);

        Assert.Equal("The line", HandIn("The line"));
        Assert.True(_progression.IsUnlocked("conveyor"));

        Assert.Equal("Ironworks", HandIn("Ironworks"));
        Assert.True(_progression.IsUnlocked("iron block"));

        Gather(_blocks.Log, 12);
        Craft("planks", times: 8);
        Assert.Equal("The long way up", HandIn("The long way up"));
        Assert.True(_progression.IsUnlocked("lift"));

        // ---- 7. A drill, so the shore works without anyone at the rock. ---
        Gather(_blocks.Stone, 140);
        Craft("stone brick", times: 34);
        Assert.Equal("Hands off", HandIn("Hands off"));
        Assert.True(_progression.IsUnlocked("miner"));

        Craft("conveyor", times: 1);
        Craft("miner", times: 1);
        Craft("smelter", times: 1);

        _world.SetBlock(new int3(-4, 20, 0), _blocks.IronOre);
        var drill = Build(new int3(-4, 21, 0), _blocks.Miner, new Miner());
        Build(new int3(-3, 21, 0), _blocks.Conveyor, new Conveyor(Facing.East));
        Build(new int3(-2, 21, 0), _blocks.Conveyor, new Conveyor(Facing.East));
        var furnace = Build(new int3(-1, 21, 0), _blocks.Smelter, new Smelter());

        Gather(_blocks.CoalOre, 16);
        Feed(drill.Fuel, _blocks.CoalOre, 8);
        Feed(furnace.Fuel, _blocks.CoalOre, 8);

        Run(120.0);

        Assert.True(
            furnace.Output.At(0).Count > 0 || furnace.Input.At(0).Count > 0,
            "nothing reached the second smelter down the belt"
        );

        // ---- 8. Current, so one fire feeds the lot. ----------------------
        Craft("iron block", times: 4);
        Assert.Equal("Current", HandIn("Current"));
        Assert.True(_progression.IsUnlocked("generator") && _progression.IsUnlocked("cable"));

        Craft("cable", times: 1);
        Craft("generator", times: 1);

        var power = Build(new int3(2, 21, 0), _blocks.Generator, new Generator());
        Gather(_blocks.CoalOre, 32);
        Feed(power.Fuel, _blocks.CoalOre, 32);
        Build(new int3(1, 21, 0), _blocks.Cable, new Cable());

        // The smelter beside the cable now melts without any fuel of its own.
        smelter.Fuel.Take(0);
        Gather(_blocks.Sand, 8);
        Feed(smelter.Input, _blocks.Sand, 8);
        Run(60.0);

        Assert.True(smelter.Output.At(0).Count > 0, "the wired smelter never ran");

        // ---- 9. Stand watch. ---------------------------------------------
        // Nine, not eight: the shore wants eight and the turret itself is built around a ninth.
        Craft("iron block", times: 9);
        Craft("copper block", times: 4);
        Assert.Equal("Stand watch", HandIn("Stand watch"));
        Assert.True(_progression.IsUnlocked("turret"));
        Assert.True(_progression.IsFinished, "the shore still wants something");

        Craft("turret", times: 1);
        var turret = Build(new int3(0, 22, 0), _blocks.Turret, new Turret());
        Build(new int3(1, 22, 0), _blocks.Cable, new Cable());

        // ---- 10. A night, with something built to live through it. -------
        var stalkers = new Stalkers(99);
        var vitality = new Vitality();
        var player = new double3(0.5, 21.0, 0.5);

        const double Step = 1.0 / 60.0;
        var killed = 0;

        for (var elapsed = 0.0; elapsed < 90.0; elapsed += Step)
        {
            _machines.Step(Step, _world);
            vitality.Step(Step);

            var before = stalkers.Count;
            stalkers.Step(_world, _blocks.Registry, Step, player, vitality, _machines.LitCount, isDay: false);
            turret.Fire(new int3(0, 22, 0), stalkers, Step);

            if (stalkers.Count < before)
                killed++;
        }

        Assert.True(killed > 0, "the turret never killed anything");
        Assert.True(vitality.IsAlive, "the player died standing next to a working turret");
    }

    /// <summary>
    /// Mines some ore, works out the fuel it needs, and runs a smelter until it is all through.
    /// </summary>
    /// <remarks>
    /// The fuel is read off the tables rather than written down, so that a change to how long
    /// iron takes or how long coal burns makes this test smelt the right amount instead of
    /// failing. What it must not do is quietly pass with a smelter that ran dry, which the
    /// assertion at the end is for.
    /// </remarks>
    private void Smelt(Smelter smelter, BlockId ore, int count)
    {
        Assert.True(smelter.Input.At(0).IsNothing, "the last batch did not all go through");

        var melt = _smelting.Melts(ore);
        Assert.NotNull(melt);

        var burn = _smelting.BurnSeconds(_blocks.CoalOre);
        var needed = (int)double.Ceiling(count * melt.Value.Seconds / burn) + 1;

        Gather(ore, count);
        Gather(_blocks.CoalOre, needed);
        Feed(smelter.Input, ore, count);
        Feed(smelter.Fuel, _blocks.CoalOre, needed);

        Run((count * melt.Value.Seconds) + 10.0);
        Take(smelter.Output);

        Assert.True(smelter.Input.At(0).IsNothing, $"the smelter did not get through {count} {_blocks.Registry[ore].Name}");
    }

    /// <summary>Picks up blocks, as breaking and walking over them does.</summary>
    private void Gather(BlockId block, int count) =>
        Assert.Equal(count, _inventory.Add(block, count));

    /// <summary>Lays a recipe out and makes it, as a player at the grid does.</summary>
    private void Craft(string name, int times)
    {
        var recipe = _recipes.All.Single(candidate => candidate.Name == name);

        for (var once = 0; once < times; once++)
        {
            Assert.True(
                _bench.Fill(recipe, _inventory),
                $"could not lay out '{name}' at attempt {once + 1}; carrying "
                    + string.Join(", ", Enumerable.Range(0, Inventory.SlotCount)
                        .Select(slot => _inventory.At(slot))
                        .Where(held => held.Count > 0)
                        .Select(held => $"{_blocks.Registry[held.Block].Name} {held.Count}"))
            );

            var made = _bench.Craft();
            Assert.False(made.IsNothing, $"'{name}' was laid out and made nothing");
            Assert.Equal(made.Count, _inventory.Add(made.Block, made.Count));
        }

        _bench.ReturnTo(_inventory);
    }

    /// <summary>Hands in one named step, which has to be open and has to be payable.</summary>
    /// <remarks>
    /// Named rather than taken off the top, because the progression branches: calling this in
    /// order is the test asserting that this order through the tree is one a player can take.
    /// </remarks>
    private string HandIn(string name)
    {
        var milestone = _progression.All.SingleOrDefault(step => step.Name == name);
        Assert.True(_progression.IsOpen(milestone), $"'{name}' is not open yet");

        var paid = _progression.HandIn(milestone, _inventory);
        Assert.True(
            paid is not null,
            $"could not pay for '{milestone.Name}': "
                + string.Join(
                    ", ",
                    milestone.Cost.Select(part =>
                        $"{_blocks.Registry[part.Block].Name} {_inventory.CountOf(part.Block)}/{part.Count}")
                )
        );

        return paid!.Value.Name;
    }

    /// <summary>Places a block out of the inventory and puts its machine in the world.</summary>
    private TMachine Build<TMachine>(int3 at, BlockId block, TMachine machine)
        where TMachine : IMachine
    {
        Assert.True(_inventory.TakeOneOf(block), $"nothing to place at {at}");
        _world.SetBlock(at, block);
        _machines.Place(at, machine);
        return machine;
    }

    /// <summary>Puts blocks from the inventory into a machine's slot.</summary>
    private void Feed(Slots slots, BlockId block, int count)
    {
        for (var one = 0; one < count; one++)
        {
            Assert.True(_inventory.TakeOneOf(block), $"ran out of {_blocks.Registry[block].Name}");
            var held = slots.At(0);
            Assert.True(
                slots.Add(block, 1) > 0,
                $"the machine would not take {_blocks.Registry[block].Name}; its slot holds "
                    + (held.IsNothing ? "nothing" : $"{_blocks.Registry[held.Block].Name} {held.Count}")
            );
        }
    }

    /// <summary>Empties a machine's output into the inventory.</summary>
    private void Take(Slots slots)
    {
        var made = slots.Take(0);
        if (!made.IsNothing)
            _inventory.Add(made.Block, made.Count);
    }

    /// <summary>Lets the world run, and touches nothing while it does.</summary>
    /// <remarks>
    /// Deliberately inert. It emptied a smelter's output as it went at first, to let more than a
    /// slotful through in one batch — and since it picked the smelter by searching, it emptied
    /// whichever one the dictionary happened to hand back, which made a later assertion about a
    /// different smelter fail for a reason nothing in the test said.
    /// </remarks>
    private void Run(double seconds)
    {
        const double Step = 1.0 / 60.0;

        for (var elapsed = 0.0; elapsed < seconds; elapsed += Step)
            _machines.Step(Step, _world);
    }
}
