namespace Shorebound.Tests;

/// <summary>
/// The crafting grid as a thing a player uses: what crafting spends, what the shortcut moves, and
/// what happens to the grid when the screen closes.
/// </summary>
/// <remarks>
/// Every claim here is a conservation claim. Blocks can move between the grid and the inventory
/// and can be spent by a recipe, and the ways to lose one are all quiet: a failed shortcut that
/// half-filled the grid, a close that dropped it, a craft that took from the wrong place.
/// </remarks>
public sealed class BenchTests
{
    private readonly GameBlocks _blocks = new(new BlockRegistry());
    private readonly Recipes _recipes;
    private readonly Bench _bench;

    public BenchTests()
    {
        _recipes = new Recipes(_blocks);
        _bench = new Bench(_recipes);
    }

    [Fact]
    public void an_empty_grid_crafts_nothing()
    {
        Assert.True(_bench.Craft().IsNothing);
    }

    [Fact]
    public void crafting_hands_over_the_result_and_spends_the_grid()
    {
        _bench.Grid.Put(4, new Stack(_blocks.Log, 2));

        var made = _bench.Craft();

        Assert.Equal(_blocks.Planks, made.Block);
        Assert.Equal(4, made.Count);
        Assert.Equal(1, _bench.Grid.At(4).Count);
    }

    [Fact]
    public void the_shortcut_lays_a_recipe_out_from_what_is_carried()
    {
        var inventory = new Inventory(_blocks);
        inventory.Add(_blocks.Stone, 8);

        Assert.True(_bench.Fill(Recipe("stone brick"), inventory));

        Assert.Equal(_blocks.StoneBrick, _bench.Result.Block);
        Assert.Equal(4, inventory.CountOf(_blocks.Stone));
    }

    [Fact]
    public void the_shortcut_refuses_rather_than_half_filling_the_grid()
    {
        // The case that matters: three of the four stone. A shortcut that laid out what it could
        // would leave the player with a grid that makes nothing and three stone they have to put
        // back by hand.
        var inventory = new Inventory(_blocks);
        inventory.Add(_blocks.Stone, 3);

        Assert.False(_bench.Fill(Recipe("stone brick"), inventory));

        Assert.True(_bench.Result.IsNothing);
        Assert.Equal(3, inventory.CountOf(_blocks.Stone));

        for (var slot = 0; slot < Recipes.GridSize; slot++)
            Assert.True(_bench.Grid.At(slot).IsNothing);
    }

    [Fact]
    public void the_shortcut_puts_back_what_was_already_on_the_grid()
    {
        var inventory = new Inventory(_blocks);
        inventory.Add(_blocks.Stone, 4);
        _bench.Grid.Put(0, new Stack(_blocks.Dirt, 6));

        Assert.True(_bench.Fill(Recipe("stone brick"), inventory));

        Assert.Equal(6, inventory.CountOf(_blocks.Dirt));
    }

    [Fact]
    public void the_shortcut_can_be_used_twice_in_a_row()
    {
        var inventory = new Inventory(_blocks);
        inventory.Add(_blocks.Stone, 4);
        inventory.Add(_blocks.Log, 1);

        Assert.True(_bench.Fill(Recipe("stone brick"), inventory));
        Assert.True(_bench.Fill(Recipe("planks"), inventory));

        Assert.Equal(_blocks.Planks, _bench.Result.Block);
        Assert.Equal(4, inventory.CountOf(_blocks.Stone));
    }

    [Fact]
    public void the_lantern_can_be_laid_out_and_made()
    {
        // End to end, because this is the chain the whole resource layer exists for: ember out of
        // a deep seam, planks out of a log, and light to go back down with.
        var inventory = new Inventory(_blocks);
        inventory.Add(_blocks.EmberOre, 1);
        inventory.Add(_blocks.Planks, 4);

        Assert.True(_bench.Fill(Recipe("lantern"), inventory));

        var made = _bench.Craft();

        Assert.Equal(_blocks.Lantern, made.Block);
        Assert.Equal(2, made.Count);
        Assert.Equal(0, inventory.CountOf(_blocks.EmberOre));
    }

    [Fact]
    public void closing_the_screen_puts_the_grid_back_rather_than_dropping_it()
    {
        var inventory = new Inventory(_blocks);
        _bench.Grid.Put(2, new Stack(_blocks.Sand, 9));
        _bench.Grid.Put(5, new Stack(_blocks.Log, 2));

        _bench.ReturnTo(inventory);

        Assert.Equal(9, inventory.CountOf(_blocks.Sand));
        Assert.Equal(2, inventory.CountOf(_blocks.Log));
        Assert.True(_bench.Grid.At(2).IsNothing);
        Assert.True(_bench.Grid.At(5).IsNothing);
    }

    [Fact]
    public void a_full_inventory_leaves_the_grid_holding_what_would_not_fit()
    {
        // Costing a player a trip is acceptable; costing them a stack is not.
        var inventory = new Inventory(_blocks);
        for (var slot = 0; slot < Inventory.SlotCount; slot++)
            inventory.Add(_blocks.Stone, Inventory.StackSize);

        _bench.Grid.Put(0, new Stack(_blocks.Log, 3));
        _bench.ReturnTo(inventory);

        Assert.Equal(3, _bench.Grid.At(0).Count);
    }

    private Recipe Recipe(string name) => _recipes.All.Single(recipe => recipe.Name == name);
}
