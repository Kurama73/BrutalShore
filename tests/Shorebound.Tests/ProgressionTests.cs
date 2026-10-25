namespace Shorebound.Tests;

/// <summary>
/// The line of milestones: what is open when, what handing in costs, and what a half-paid step
/// must not do.
/// </summary>
/// <remarks>
/// The claims that matter are the gates. A progression that can be skipped by laying out a
/// pattern you were not meant to know is not a progression, and one that takes a player's blocks
/// without advancing them is a bug they will never forgive.
/// </remarks>
public sealed class ProgressionTests
{
    private readonly GameBlocks _blocks = new(new BlockRegistry());
    private readonly Progression _progression;

    public ProgressionTests() => _progression = new Progression(_blocks);

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
    public void a_locked_recipe_makes_nothing_even_laid_out_correctly()
    {
        // The gate has to be in the matching and not only in the list. A player who guesses the
        // pattern must not get the thing, or the whole line is a suggestion.
        var recipes = new Recipes(_blocks);
        var grid = new Slots(Recipes.GridSize);

        foreach (var slot in (ReadOnlySpan<int>)[0, 1, 2, 3, 5, 6, 7, 8])
            grid.Put(slot, new Stack(_blocks.StoneBrick, 1));

        Assert.True(recipes.Match(grid, _progression).IsNothing);

        _progression.Restore(1);

        Assert.Equal(_blocks.Smelter, recipes.Match(grid, _progression).Block);
    }

    [Fact]
    public void an_empty_handed_player_cannot_pay()
    {
        var inventory = new Inventory(_blocks);

        Assert.False(_progression.CanAfford(inventory));
        Assert.Null(_progression.HandIn(inventory));
        Assert.Equal(0, _progression.Reached);
    }

    [Fact]
    public void one_block_short_takes_nothing_at_all()
    {
        var inventory = new Inventory(_blocks);
        var cost = _progression.Next!.Value.Cost;

        foreach (var (block, count) in cost)
            inventory.Add(block, count);

        inventory.TakeOneOf(cost[0].Block);

        Assert.Null(_progression.HandIn(inventory));
        Assert.Equal(cost[1].Count, inventory.CountOf(cost[1].Block));
    }

    [Fact]
    public void handing_in_takes_the_cost_and_opens_what_it_promised()
    {
        var inventory = new Inventory(_blocks);
        var milestone = _progression.Next!.Value;

        foreach (var (block, count) in milestone.Cost)
            inventory.Add(block, count + 3);

        Assert.Equal(milestone.Name, _progression.HandIn(inventory)?.Name);

        foreach (var (block, _) in milestone.Cost)
            Assert.Equal(3, inventory.CountOf(block));

        foreach (var recipe in milestone.Unlocks)
            Assert.True(_progression.IsUnlocked(recipe));
    }

    [Fact]
    public void the_line_runs_out_rather_than_wrapping_round()
    {
        _progression.Restore(_progression.All.Count);

        Assert.True(_progression.IsFinished);
        Assert.Null(_progression.Next);
        Assert.Null(_progression.HandIn(new Inventory(_blocks)));
    }

    [Fact]
    public void a_save_from_a_later_build_opens_with_what_this_one_knows()
    {
        // Clamping rather than trusting. A world written by a build with more steps in it must
        // still open, and opening it with everything unlocked is the only answer that does not
        // take something away from the player.
        _progression.Restore(999);

        Assert.Equal(_progression.All.Count, _progression.Reached);
    }

    [Fact]
    public void every_recipe_is_either_free_or_reachable()
    {
        // The claim that stops a recipe being added and then never unlocked by anything, which
        // is exactly how the lantern spent its first weeks in this game.
        var recipes = new Recipes(_blocks);
        _progression.Restore(_progression.All.Count);

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
    public void every_milestone_is_paid_for_with_what_the_one_before_it_unlocked()
    {
        // The shape the whole middle of the game has: each tier bought with the output of the
        // last, so the line cannot be skipped and never asks for something unreachable. A cost
        // naming a block that nothing before it made would be a wall rather than a step.
        var recipes = new Recipes(_blocks);

        // What a player can get with their hands before any milestone: anything in the world,
        // and anything the free recipes make.
        var reachable = new HashSet<BlockId>(Worldly());
        foreach (var recipe in recipes.All.Where(recipe => _progression.IsUnlocked(recipe.Name)))
            reachable.Add(recipe.Result);

        for (var step = 0; step < _progression.All.Count; step++)
        {
            var milestone = _progression.All[step];

            foreach (var (block, _) in milestone.Cost)
            {
                Assert.True(
                    reachable.Contains(block),
                    $"'{milestone.Name}' asks for {_blocks.Registry[block].Name}, which nothing before it makes"
                );
            }

            _progression.Restore(step + 1);

            foreach (var name in milestone.Unlocks)
            {
                var recipe = recipes.All.Single(candidate => candidate.Name == name);
                reachable.Add(recipe.Result);
            }

            // And whatever those new machines produce from what is already reachable.
            foreach (var (input, result, _) in new Smelting(_blocks).All)
            {
                if (reachable.Contains(input) && reachable.Contains(_blocks.Smelter))
                    reachable.Add(result);
            }
        }
    }

    [Fact]
    public void the_line_ends_with_something_that_defends_what_it_built()
    {
        // The shape of the whole game in one assertion: it starts with a shelter and ends with
        // the works being able to look after themselves.
        var last = _progression.All[^1];

        Assert.Contains("turret", last.Unlocks);
    }

    /// <summary>Everything a player can pick up out of the world without making anything.</summary>
    private IEnumerable<BlockId> Worldly() =>
        [
            _blocks.Stone, _blocks.Dirt, _blocks.Grass, _blocks.Sand, _blocks.Snow,
            _blocks.Log, _blocks.Leaves,
            _blocks.CoalOre, _blocks.CopperOre, _blocks.IronOre, _blocks.EmberOre,
        ];
}
