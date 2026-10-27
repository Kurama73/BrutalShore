using Shorebound.Domain.Player;
using Shorebound.Domain.Production;
using Shorebound.Domain.Progression;
using Shorebound.Domain.World;

namespace Shorebound.Tests.Domain.Progression;

/// <summary>
/// The tree of milestones: what is open when, what handing in costs, what a half-paid step must
/// not do, and that the shape of it can actually be played.
/// </summary>
/// <remarks>
/// The claims that matter are the gates and the shape. A progression that can be skipped by
/// laying out a pattern you were not meant to know is not a progression; one that takes a
/// player's blocks without advancing them is a bug they will never forgive; and a branch whose
/// cost nothing before it can make is a wall the player cannot see.
/// </remarks>
public sealed class ProgressionTests
{
    private readonly GameBlocks _blocks = new(new BlockRegistry());
    private readonly Shorebound.Domain.Progression.Progression _progression;

    public ProgressionTests() => _progression = new Shorebound.Domain.Progression.Progression(_blocks);

    [Fact]
    public void wood_and_stone_are_open_from_the_start()
    {
        // Otherwise the first five minutes of the game are a menu.
        Assert.True(_progression.IsUnlocked("planks"));
        Assert.True(_progression.IsUnlocked("stone brick"));
    }

    [Fact]
    public void everything_else_starts_shut()
    {
        Assert.False(_progression.IsUnlocked("smelter"));
        Assert.False(_progression.IsUnlocked("lantern"));
        Assert.False(_progression.IsUnlocked("iron block"));
    }

    [Fact]
    public void exactly_one_step_is_open_on_the_first_morning()
    {
        // A tree that offered a choice before the player had made anything would be offering it
        // to somebody with nothing to choose with.
        Assert.Equal(["Make landfall"], _progression.Open.Select(step => step.Name));
    }

    [Fact]
    public void the_shore_opens_two_ways_at_once_once_there_is_metal()
    {
        _progression.Restore(["Make landfall", "First smoke"]);

        // The whole reason this is a tree and not a queue: light to go down, belts to stop
        // carrying, and the player decides which one they want first.
        Assert.True(_progression.Open.Count() >= 2, "nothing branches");
    }

    [Fact]
    public void the_branches_come_back_together()
    {
        // Branching without rejoining doubles the content the game needs. Rejoining is what lets
        // both arms be worth taking without either being optional.
        Assert.Contains(_progression.All, step => step.Requires.Count >= 2);
    }

    [Fact]
    public void a_step_whose_requirements_are_unmet_cannot_be_paid_for_at_any_price()
    {
        var turret = _progression.All.Single(step => step.Name == "Stand watch");
        var inventory = new Inventory(_blocks);

        foreach (var (block, count) in turret.Cost)
            inventory.Add(block, count * 2);

        // The gate is the requirement and not the price. A player who finds iron blocks early
        // must not be able to buy the end of the game with them.
        Assert.False(_progression.CanAfford(turret, inventory));
        Assert.Null(_progression.HandIn(turret, inventory));
    }

    [Fact]
    public void a_locked_recipe_makes_nothing_even_laid_out_correctly()
    {
        // The gate has to be in the matching and not only in the list. A player who guesses the
        // pattern must not get the thing, or the whole tree is a suggestion.
        var recipes = new Recipes(_blocks);
        var grid = new Slots(Recipes.GridSize);

        foreach (var slot in (ReadOnlySpan<int>)[0, 1, 2, 3, 5, 6, 7, 8])
            grid.Put(slot, new Stack(_blocks.StoneBrick, 1));

        Assert.True(recipes.Match(grid, _progression).IsNothing);

        _progression.Restore(["Make landfall"]);

        Assert.Equal(_blocks.Smelter, recipes.Match(grid, _progression).Block);
    }

    [Fact]
    public void an_empty_handed_player_cannot_pay()
    {
        var inventory = new Inventory(_blocks);

        Assert.False(_progression.CanAfford(_progression.Open.First(), inventory));
        Assert.Null(_progression.HandIn(_progression.Open.First(), inventory));
        Assert.Equal(0, _progression.DoneCount);
    }

    [Fact]
    public void one_block_short_takes_nothing_at_all()
    {
        var inventory = new Inventory(_blocks);
        var step = _progression.Open.First();

        foreach (var (block, count) in step.Cost)
            inventory.Add(block, count);

        inventory.TakeOneOf(step.Cost[0].Block);

        Assert.Null(_progression.HandIn(step, inventory));
        Assert.Equal(step.Cost[1].Count, inventory.CountOf(step.Cost[1].Block));
    }

    [Fact]
    public void handing_in_takes_the_cost_and_opens_what_it_promised()
    {
        var inventory = new Inventory(_blocks);
        var step = _progression.Open.First();

        foreach (var (block, count) in step.Cost)
            inventory.Add(block, count + 3);

        Assert.Equal(step.Name, _progression.HandIn(step, inventory)?.Name);

        foreach (var (block, _) in step.Cost)
            Assert.Equal(3, inventory.CountOf(block));

        foreach (var recipe in step.Unlocks)
            Assert.True(_progression.IsUnlocked(recipe));
    }

    [Fact]
    public void a_step_cannot_be_paid_for_twice()
    {
        var inventory = new Inventory(_blocks);
        var step = _progression.Open.First();

        foreach (var (block, count) in step.Cost)
            inventory.Add(block, count * 2);

        Assert.NotNull(_progression.HandIn(step, inventory));
        Assert.Null(_progression.HandIn(step, inventory));
        Assert.Equal(1, _progression.DoneCount);
    }

    [Fact]
    public void the_tree_runs_out_rather_than_wrapping_round()
    {
        _progression.Restore(_progression.All.Select(step => step.Name).ToArray());

        Assert.True(_progression.IsFinished);
        Assert.Empty(_progression.Open);
    }

    [Fact]
    public void a_save_naming_a_step_this_build_has_never_heard_of_still_opens()
    {
        // A world written by a build with more steps in it must still open, and opening it with
        // everything this build does recognise is the only answer that takes nothing away.
        _progression.Restore(["Make landfall", "Orbital elevator"]);

        Assert.True(_progression.IsDone("Make landfall"));
        Assert.Equal(1, _progression.DoneCount);
    }

    [Fact]
    public void a_world_saved_before_the_tree_opens_on_the_line_it_was_played_on()
    {
        // The old save format held a count, and the order the steps are written down is the line
        // that count counted along.
        _progression.RestoreCount(2);

        Assert.True(_progression.IsDone(_progression.All[0].Name));
        Assert.True(_progression.IsDone(_progression.All[1].Name));
        Assert.False(_progression.IsDone(_progression.All[2].Name));

        _progression.RestoreCount(999);

        Assert.True(_progression.IsFinished);
    }

    [Fact]
    public void every_recipe_is_either_free_or_reachable()
    {
        // The claim that stops a recipe being added and then never unlocked by anything, which
        // is exactly how the lantern spent its first weeks in this game.
        var recipes = new Recipes(_blocks);
        _progression.Restore(_progression.All.Select(step => step.Name).ToArray());

        foreach (var recipe in recipes.All)
            Assert.True(_progression.IsUnlocked(recipe.Name), $"nothing unlocks '{recipe.Name}'");
    }

    [Fact]
    public void every_milestone_unlocks_something_that_exists()
    {
        var recipes = new Recipes(_blocks);

        foreach (var milestone in _progression.All)
        foreach (var name in milestone.Unlocks)
            Assert.Contains(recipes.All, recipe => recipe.Name == name);
    }

    [Fact]
    public void every_milestone_says_what_it_is_for()
    {
        foreach (var milestone in _progression.All)
        {
            Assert.NotEmpty(milestone.Name);
            Assert.NotEmpty(milestone.Story);
            Assert.NotEmpty(milestone.Cost);
        }
    }

    [Fact]
    public void every_step_is_paid_for_with_what_its_own_branch_already_made()
    {
        // The shape of the whole middle of the game, and the one claim a tree needs that a line
        // did not: a step's cost has to be payable with what the steps it requires unlocked, and
        // not with what some other branch happens to have unlocked by then. Otherwise a player
        // who takes the branches in the other order walks into a wall nothing warned them about.
        foreach (var step in _progression.All)
        {
            var behind = new Shorebound.Domain.Progression.Progression(_blocks);
            behind.Restore(Behind(step).ToArray());

            var reachable = Reachable(behind);

            foreach (var (block, _) in step.Cost)
            {
                Assert.True(
                    reachable.Contains(block),
                    $"'{step.Name}' asks for {_blocks.Registry[block].Name}, which nothing it requires makes"
                );
            }
        }
    }

    [Fact]
    public void the_tree_ends_with_something_that_defends_what_it_built()
    {
        // The shape of the whole game in one assertion: it starts with a shelter and ends with
        // the works being able to look after themselves.
        var last = _progression.All.Single(step => _progression.All.All(other => !other.Requires.Contains(step.Name)));

        Assert.Contains("turret", last.Unlocks);
    }

    /// <summary>Every step that has to be done before one, however far back.</summary>
    private IEnumerable<string> Behind(Milestone step)
    {
        var behind = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(step.Requires);

        while (pending.TryDequeue(out var name))
        {
            if (!behind.Add(name))
                continue;

            foreach (var required in _progression.All.Single(candidate => candidate.Name == name).Requires)
                pending.Enqueue(required);
        }

        return behind;
    }

    /// <summary>Every block a player could be holding, given what a progression has unlocked.</summary>
    /// <remarks>
    /// Run to a fixed point rather than in one pass: a recipe's result can be another recipe's
    /// ingredient, and a smelter turns a block that was itself crafted into a third one.
    /// </remarks>
    private HashSet<BlockId> Reachable(Shorebound.Domain.Progression.Progression progression)
    {
        var recipes = new Recipes(_blocks);
        var smelting = new Smelting(_blocks);
        var reachable = new HashSet<BlockId>(Worldly());
        bool grew;

        do
        {
            grew = false;

            foreach (var recipe in recipes.All.Where(recipe => progression.IsUnlocked(recipe.Name)))
            {
                if (recipe.Pattern.Where(block => !block.IsAir).All(reachable.Contains))
                    grew |= reachable.Add(recipe.Result);
            }

            foreach (var (input, result, _) in smelting.All)
            {
                if (reachable.Contains(input) && reachable.Contains(_blocks.Smelter))
                    grew |= reachable.Add(result);
            }
        } while (grew);

        return reachable;
    }

    /// <summary>Everything a player can pick up out of the world without making anything.</summary>
    private IEnumerable<BlockId> Worldly() =>
        [
            _blocks.Stone, _blocks.Dirt, _blocks.Grass, _blocks.Sand, _blocks.Snow,
            _blocks.Log, _blocks.Leaves,
            _blocks.CoalOre, _blocks.CopperOre, _blocks.IronOre, _blocks.EmberOre,
        ];
}
