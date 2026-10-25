namespace Shorebound;

/// <summary>The crafting grid: what is laid out on it, and what that currently makes.</summary>
/// <remarks>
/// <para>
/// A grid the player carries rather than a block they stand at. There is no workbench to build
/// and no reason to walk back to one: the interesting decisions in this game are about where to
/// dig and what to spend, and a trip home to use a table is neither of them.
/// </para>
/// <para>
/// It owns the two things the grid alone cannot do. Taking the result has to spend the grid in
/// the same breath, or a player can craft the same lantern forever; and emptying the grid back
/// into the inventory has to happen when the screen closes, or the blocks laid out on it vanish
/// with it.
/// </para>
/// </remarks>
public sealed class Bench
{
    private readonly Recipes _recipes;

    /// <summary>Builds an empty grid.</summary>
    /// <param name="recipes">The table that says what an arrangement makes.</param>
    public Bench(Recipes recipes) => _recipes = recipes;

    /// <summary>The slots the player lays blocks out in.</summary>
    public Slots Grid { get; } = new(Recipes.GridSize);

    /// <summary>What the grid currently makes, or nothing.</summary>
    public Stack Result => _recipes.Match(Grid);

    /// <summary>Makes one of whatever the grid makes, spending what it took.</summary>
    /// <returns>What was made, or <see cref="Stack.Nothing" /> if the grid makes nothing.</returns>
    public Stack Craft()
    {
        var made = Result;
        if (made.IsNothing)
            return Stack.Nothing;

        Recipes.Spend(Grid);
        return made;
    }

    /// <summary>Lays a recipe out on the grid, taking what it needs from what is carried.</summary>
    /// <param name="recipe">The recipe to lay out.</param>
    /// <param name="inventory">Where the blocks come from, and where the old grid goes.</param>
    /// <returns>Whether there was enough to lay it out.</returns>
    /// <remarks>
    /// <para>
    /// The shortcut that makes a recipe list worth having: a player who knows what they want
    /// should not have to remember where the planks go. It is checked before anything is moved,
    /// so a recipe that cannot be laid out leaves both the grid and the inventory alone — a
    /// half-filled grid after a failed attempt is worse than no shortcut at all.
    /// </para>
    /// <para>
    /// What was already on the grid goes back into the inventory first, which is what makes the
    /// shortcut usable twice in a row.
    /// </para>
    /// </remarks>
    public bool Fill(Recipe recipe, Inventory inventory)
    {
        ReturnTo(inventory);

        // Counted per distinct block rather than per slot, because a recipe that needs two planks
        // and finds one would otherwise fill the first slot and fail on the second.
        foreach (var block in recipe.Pattern.Where(block => !block.IsAir).Distinct())
        {
            var needed = recipe.Pattern.Count(wanted => wanted == block);
            if (inventory.CountOf(block) < needed)
                return false;
        }

        for (var row = 0; row < recipe.Height; row++)
        for (var column = 0; column < recipe.Width; column++)
        {
            var block = recipe.Pattern[(row * recipe.Width) + column];
            if (block.IsAir)
                continue;

            inventory.TakeOneOf(block);
            Grid.Put((row * Recipes.GridWidth) + column, new Stack(block, 1));
        }

        return true;
    }

    /// <summary>Empties the grid back into what the player is carrying.</summary>
    /// <param name="inventory">Where it goes.</param>
    /// <remarks>
    /// Anything that does not fit is left on the grid rather than dropped, so a full inventory
    /// costs the player a trip and never a stack.
    /// </remarks>
    public void ReturnTo(Inventory inventory)
    {
        for (var index = 0; index < Grid.Count; index++)
        {
            var slot = Grid.At(index);
            if (slot.IsNothing)
                continue;

            var taken = inventory.Add(slot.Block, slot.Count);
            Grid.Take(index);

            if (taken < slot.Count)
                Grid.Put(index, new Stack(slot.Block, slot.Count - taken));
        }
    }
}
