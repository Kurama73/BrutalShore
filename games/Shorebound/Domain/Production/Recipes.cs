using BrutalShore.Voxel;
using Shorebound.Domain.Player;
using Shorebound.Domain.World;

namespace Shorebound.Domain.Production;

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
/// <para>
/// What is deliberately <em>not</em> here is any way to turn ore into metal. That is the
/// smelter's job, and keeping it out of the grid is what gives a player a reason to build one:
/// the first seam of copper they find is dead weight until they do.
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

            // The smelter: a ring of brick around nothing, which is what a furnace is. It is the
            // one recipe here that unlocks the rest of the game rather than a block — ore melts
            // into metal and nothing else does, so until this is built every seam found is worth
            // carrying and worth nothing else.
            new Recipe(
                "smelter",
                3,
                3,
                [
                    blocks.StoneBrick, blocks.StoneBrick, blocks.StoneBrick,
                    blocks.StoneBrick, air, blocks.StoneBrick,
                    blocks.StoneBrick, blocks.StoneBrick, blocks.StoneBrick,
                ],
                blocks.Smelter,
                1
            ),

            // A belt: three planks under three copper. Cheap on purpose — a factory is made of
            // hundreds of these and a player who has to think about the cost of one is a player
            // laying out a line one block at a time.
            new Recipe(
                "conveyor",
                3,
                2,
                [
                    blocks.CopperIngot, blocks.CopperIngot, blocks.CopperIngot,
                    blocks.Planks, blocks.Planks, blocks.Planks,
                ],
                blocks.Conveyor,
                6
            ),

            // A drill: iron over a smelter's worth of brick, with copper at the shoulders. The
            // expensive one, because one of these replaces the player at the rock face.
            new Recipe(
                "miner",
                3,
                3,
                [
                    blocks.IronIngot, blocks.IronIngot, blocks.IronIngot,
                    blocks.CopperIngot, blocks.StoneBrick, blocks.CopperIngot,
                    blocks.StoneBrick, blocks.StoneBrick, blocks.StoneBrick,
                ],
                blocks.Miner,
                1
            ),

            // Cable: copper in a line, which is the cheapest thing in the game by some way.
            // A grid is made of hundreds of metres of this and nobody should count it.
            new Recipe(
                "cable",
                3,
                1,
                [blocks.CopperIngot, blocks.CopperIngot, blocks.CopperIngot],
                blocks.Cable,
                12
            ),

            // A lift: planks in a column with iron through the middle.
            new Recipe(
                "lift",
                1,
                3,
                [blocks.Planks, blocks.IronIngot, blocks.Planks],
                blocks.Lift,
                4
            ),

            // A generator: a firebox of brick with iron around it and copper to take the
            // current away.
            new Recipe(
                "generator",
                3,
                3,
                [
                    blocks.IronIngot, blocks.CopperIngot, blocks.IronIngot,
                    blocks.StoneBrick, air, blocks.StoneBrick,
                    blocks.StoneBrick, blocks.StoneBrick, blocks.StoneBrick,
                ],
                blocks.Generator,
                1
            ),

            // A turret: iron over a stand of brick, with copper for the works. The most
            // expensive thing here, because one of these replaces standing watch.
            new Recipe(
                "turret",
                3,
                3,
                [
                    air, blocks.IronIngot, air,
                    blocks.IronIngot, blocks.CopperIngot, blocks.IronIngot,
                    blocks.StoneBrick, blocks.IronBlock, blocks.StoneBrick,
                ],
                blocks.Turret,
                1
            ),

            // Nine ingots into one block. Packing, so that a long session at a smelter comes back
            // as something that fits in the row rather than filling the pack. Ingots and not ore,
            // because ore that could be packed straight into a block would make the smelter an
            // optional convenience rather than the thing the ore is for.
            new Recipe("copper block", 3, 3, Nine(blocks.CopperIngot), blocks.CopperBlock, 1),
            new Recipe("iron block", 3, 3, Nine(blocks.IronIngot), blocks.IronBlock, 1),

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
    /// <param name="progression">
    /// How far the player has got, which decides what they are allowed to make. A locked recipe
    /// is refused here and not merely hidden from the list: hiding it alone would mean a player
    /// who laid the pattern out by guessing got the thing anyway, which makes the whole
    /// progression an inconvenience rather than a gate.
    /// </param>
    /// <returns>What would come out, or <see cref="Stack.Nothing" />.</returns>
    /// <exception cref="ArgumentException">If the grid is not <see cref="GridSize" /> slots.</exception>
    public Stack Match(Slots grid, Progression.Progression progression)
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
                return progression.IsUnlocked(recipe.Name) ? new Stack(recipe.Result, recipe.Count) : Stack.Nothing;
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
