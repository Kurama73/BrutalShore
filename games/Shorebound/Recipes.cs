using BrutalShore.Voxel;

namespace Shorebound;

/// <summary>Everything that can be made, and what a grid of blocks currently makes.</summary>
/// <remarks>
/// <para>
/// The table and the matching in one place, because the matching rule is part of what a recipe
/// means. The rule is: trim the empty rows and columns off what is in the grid, then look for a
/// recipe of exactly that shape with exactly those blocks. Trimming is what lets a player put a
/// two-by-two arrangement anywhere in the grid and have it work, which is what everyone who has
/// played a game of this kind expects and nobody is ever told.
/// </para>
/// <para>
/// A recipe takes one of each block it names, however many are stacked in the slot. That is what
/// makes a grid left set up between two crafts keep working, and it is why crafting takes from
/// the grid rather than from the inventory: what is laid out is what is spent.
/// </para>
/// <para>
/// The table exists for a reason, and the reason is the lantern. A lantern is registered, lights
/// a cave, and until there was a recipe for it no player could ever hold one — the world does not
/// generate them. Everything else here is built around that: planks out of a log, bricks out of
/// stone, and a lantern out of the ore that only turns up at the bottom of a deep shaft.
/// </para>
/// </remarks>
public sealed class Recipes
{
    /// <summary>How many columns the crafting grid has.</summary>
    public const int GridWidth = 3;

    /// <summary>How many rows it has.</summary>
    public const int GridHeight = 3;

    /// <summary>How many slots that is.</summary>
    public const int GridSize = GridWidth * GridHeight;

    private readonly Recipe[] _recipes;

    /// <summary>Builds the table for a game's blocks.</summary>
    /// <param name="blocks">The game's registered block types.</param>
    public Recipes(GameBlocks blocks)
    {
        var air = BlockId.Air;

        _recipes =
        [
            // A log into four planks, which is the one recipe a player will find without being
            // told: it is one block in one slot, and everyone tries that first.
            new Recipe("planks", 1, 1, [blocks.Log], blocks.Planks, 4),

            // Four stone into four bricks. Not a saving, a finish: the point of it is that the
            // wall you build out of it does not look like the hole you dug.
            new Recipe("stone brick", 2, 2, [blocks.Stone, blocks.Stone, blocks.Stone, blocks.Stone], blocks.StoneBrick, 4),

            // Nine ore into one block. Packing, so that a long session underground comes back up
            // as something that fits in the row rather than filling the pack.
            new Recipe("copper block", 3, 3, Nine(blocks.CopperOre), blocks.CopperBlock, 1),
            new Recipe("iron block", 3, 3, Nine(blocks.IronOre), blocks.IronBlock, 1),

            // The lantern: ember held in a frame of planks. Two of them, because one lantern per
            // deep shaft would make light cost more than the shaft did.
            new Recipe(
                "lantern",
                3,
                3,
                [
                    air, blocks.Planks, air,
                    blocks.Planks, blocks.EmberOre, blocks.Planks,
                    air, blocks.Planks, air,
                ],
                blocks.Lantern,
                2
            ),
        ];
    }

    /// <summary>Every recipe, for a screen that lists them.</summary>
    public IReadOnlyList<Recipe> All => _recipes;

    /// <summary>What a grid of blocks currently makes.</summary>
    /// <param name="grid">The crafting slots, row by row from the top left.</param>
    /// <returns>What would come out, or <see cref="Stack.Nothing" />.</returns>
    /// <exception cref="ArgumentException">If the grid is not <see cref="GridSize" /> slots.</exception>
    public Stack Match(Slots grid)
    {
        if (grid.Count != GridSize)
            throw new ArgumentException($"the grid is {GridSize} slots", nameof(grid));

        if (!Bounds(grid, out var left, out var top, out var width, out var height))
            return Stack.Nothing;

        foreach (var recipe in _recipes)
        {
            if (recipe.Width != width || recipe.Height != height)
                continue;

            if (Fits(grid, recipe, left, top))
                return new Stack(recipe.Result, recipe.Count);
        }

        return Stack.Nothing;
    }

    /// <summary>Takes one of everything the recipe used out of the grid.</summary>
    /// <param name="grid">The crafting slots, which this empties by one each.</param>
    /// <remarks>
    /// Every occupied slot loses one, rather than the recipe being consulted again. The two are
    /// the same thing for any recipe that matched — a recipe uses exactly the slots that are not
    /// empty — and doing it this way means a grid can never be left holding a block the recipe
    /// quietly did not count.
    /// </remarks>
    public static void Spend(Slots grid)
    {
        for (var index = 0; index < grid.Count; index++)
            grid.TakeOne(index);
    }

    /// <summary>The smallest rectangle holding everything in the grid.</summary>
    /// <returns><see langword="false" /> if the grid is empty.</returns>
    private static bool Bounds(Slots grid, out int left, out int top, out int width, out int height)
    {
        left = GridWidth;
        top = GridHeight;
        var right = -1;
        var bottom = -1;

        for (var index = 0; index < GridSize; index++)
        {
            if (grid.At(index).IsNothing)
                continue;

            var column = index % GridWidth;
            var row = index / GridWidth;
            left = int.Min(left, column);
            right = int.Max(right, column);
            top = int.Min(top, row);
            bottom = int.Max(bottom, row);
        }

        width = right - left + 1;
        height = bottom - top + 1;
        return right >= 0;
    }

    /// <summary>Whether a recipe's pattern sits exactly on the grid at an offset.</summary>
    private static bool Fits(Slots grid, Recipe recipe, int left, int top)
    {
        for (var row = 0; row < recipe.Height; row++)
        for (var column = 0; column < recipe.Width; column++)
        {
            var wanted = recipe.Pattern[(row * recipe.Width) + column];
            var slot = grid.At(((top + row) * GridWidth) + left + column);

            if (wanted.IsAir ? !slot.IsNothing : slot.IsNothing || slot.Block != wanted)
                return false;
        }

        return true;
    }

    /// <summary>Nine of one block, for a recipe that fills the grid with it.</summary>
    private static BlockId[] Nine(BlockId block) =>
        [block, block, block, block, block, block, block, block, block];
}
