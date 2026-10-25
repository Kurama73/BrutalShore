namespace Shorebound.Tests;

/// <summary>
/// What a grid of blocks makes: the shapes that match, the ones that must not, and what crafting
/// takes out of the grid.
/// </summary>
/// <remarks>
/// The claims worth pinning down are about the matching rule rather than about any one recipe. A
/// rule that only matched a pattern in the top left corner, or that matched a pattern with a
/// stray block beside it, would be wrong in a way that is maddening to play against and invisible
/// to read.
/// </remarks>
public sealed class RecipeTests
{
    private readonly GameBlocks _blocks = new(new BlockRegistry());
    private readonly Recipes _recipes;

    public RecipeTests() => _recipes = new Recipes(_blocks);

    [Fact]
    public void an_empty_grid_makes_nothing()
    {
        Assert.True(_recipes.Match(Grid()).IsNothing);
    }

    [Fact]
    public void one_log_anywhere_in_the_grid_makes_planks()
    {
        // The whole point of trimming: a player drops a log in whichever slot their pointer was
        // over, and all nine of them have to work.
        for (var slot = 0; slot < Recipes.GridSize; slot++)
        {
            var grid = Grid();
            grid.Put(slot, new Stack(_blocks.Log, 1));

            var made = _recipes.Match(grid);

            Assert.Equal(_blocks.Planks, made.Block);
            Assert.Equal(4, made.Count);
        }
    }

    [Fact]
    public void a_two_by_two_recipe_matches_in_every_corner()
    {
        foreach (var corner in (ReadOnlySpan<int>)[0, 1, 3, 4])
        {
            var grid = Grid();
            foreach (var offset in (ReadOnlySpan<int>)[0, 1, 3, 4])
                grid.Put(corner + offset, new Stack(_blocks.Stone, 1));

            Assert.Equal(_blocks.StoneBrick, _recipes.Match(grid).Block);
        }
    }

    [Fact]
    public void a_stray_block_beside_a_pattern_stops_it_matching()
    {
        var grid = Grid();
        foreach (var slot in (ReadOnlySpan<int>)[0, 1, 3, 4])
            grid.Put(slot, new Stack(_blocks.Stone, 1));

        grid.Put(8, new Stack(_blocks.Dirt, 1));

        Assert.True(_recipes.Match(grid).IsNothing);
    }

    [Fact]
    public void a_gap_inside_a_pattern_has_to_be_a_gap()
    {
        // The lantern's frame has a hole at each corner. Filling one makes a different shape, and
        // a rule that ignored the empty cells of a pattern would make it a lantern anyway.
        var grid = Lantern();
        grid.Put(0, new Stack(_blocks.Planks, 1));

        Assert.True(_recipes.Match(grid).IsNothing);
    }

    [Fact]
    public void the_lantern_is_made_of_ember_in_a_frame_of_planks()
    {
        var made = _recipes.Match(Lantern());

        Assert.Equal(_blocks.Lantern, made.Block);
        Assert.Equal(2, made.Count);
    }

    [Fact]
    public void the_lantern_can_be_made_at_all_which_is_the_only_way_to_hold_one()
    {
        // Nothing in the world generates a lantern. Without a recipe for it the block exists, is
        // documented, lights a cave, and is unreachable — which is what this is here to stop.
        Assert.Contains(_recipes.All, recipe => recipe.Result == _blocks.Lantern);
    }

    [Fact]
    public void the_wrong_block_in_the_right_shape_makes_nothing()
    {
        var grid = Grid();
        foreach (var slot in (ReadOnlySpan<int>)[0, 1, 3, 4])
            grid.Put(slot, new Stack(_blocks.Dirt, 1));

        Assert.True(_recipes.Match(grid).IsNothing);
    }

    [Fact]
    public void crafting_takes_one_from_every_slot_and_leaves_the_rest()
    {
        var grid = Grid();
        foreach (var slot in (ReadOnlySpan<int>)[0, 1, 3, 4])
            grid.Put(slot, new Stack(_blocks.Stone, 5));

        Recipes.Spend(grid);

        foreach (var slot in (ReadOnlySpan<int>)[0, 1, 3, 4])
            Assert.Equal(4, grid.At(slot).Count);

        // And the same arrangement still makes the same thing, which is what makes a grid left
        // set up worth leaving set up.
        Assert.Equal(_blocks.StoneBrick, _recipes.Match(grid).Block);
    }

    [Fact]
    public void crafting_the_last_of_an_ingredient_empties_its_slot()
    {
        var grid = Grid();
        grid.Put(4, new Stack(_blocks.Log, 1));

        Recipes.Spend(grid);

        Assert.True(grid.At(4).IsNothing);
        Assert.True(_recipes.Match(grid).IsNothing);
    }

    [Fact]
    public void nine_ingots_pack_into_one_block()
    {
        var grid = Grid();
        for (var slot = 0; slot < Recipes.GridSize; slot++)
            grid.Put(slot, new Stack(_blocks.IronIngot, 1));

        var made = _recipes.Match(grid);

        Assert.Equal(_blocks.IronBlock, made.Block);
        Assert.Equal(1, made.Count);
    }

    [Fact]
    public void no_arrangement_of_ore_makes_anything_without_a_smelter()
    {
        // The claim that gives the smelter a reason to exist. Ore out of the ground is dead
        // weight until one is built, and a stray recipe that packed it straight into a block
        // would quietly make the whole machine optional.
        foreach (var ore in (ReadOnlySpan<BlockId>)[_blocks.CopperOre, _blocks.IronOre])
        {
            Assert.DoesNotContain(
                _recipes.All,
                recipe => recipe.Pattern.Contains(ore)
            );
        }
    }

    [Fact]
    public void every_recipe_states_a_pattern_the_size_it_claims()
    {
        foreach (var recipe in _recipes.All)
        {
            Assert.Equal(recipe.Width * recipe.Height, recipe.Pattern.Count);
            Assert.InRange(recipe.Width, 1, Recipes.GridWidth);
            Assert.InRange(recipe.Height, 1, Recipes.GridHeight);
        }
    }

    [Fact]
    public void a_pattern_that_is_not_the_size_it_claims_is_refused()
    {
        Assert.Throws<ArgumentException>(
            () => new Recipe("wrong", 2, 2, [_blocks.Stone], _blocks.StoneBrick, 1)
        );
    }

    private static Slots Grid() => new(Recipes.GridSize);

    private Slots Lantern()
    {
        var grid = Grid();
        grid.Put(1, new Stack(_blocks.Planks, 1));
        grid.Put(3, new Stack(_blocks.Planks, 1));
        grid.Put(4, new Stack(_blocks.EmberOre, 1));
        grid.Put(5, new Stack(_blocks.Planks, 1));
        grid.Put(7, new Stack(_blocks.Planks, 1));
        return grid;
    }
}
