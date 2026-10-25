using BrutalShore.Voxel;

namespace Shorebound;

/// <summary>How far through the game the player is, and what that lets them make.</summary>
/// <remarks>
/// <para>
/// A line rather than a tree. A tree of unlocks is a promise of choice, and a choice between two
/// branches is only interesting when both are worth taking — which takes far more content than
/// this game has. A line that each step is paid for with the output of the last one gives the
/// same sense of getting somewhere and never has a dead branch in it.
/// </para>
/// <para>
/// Only the number of steps taken is state. Everything else — what is open, what is next, what it
/// costs — is read off the table, so adding a milestone changes one list and nothing else, and a
/// save file holds a single integer.
/// </para>
/// </remarks>
public sealed class Progression
{
    private readonly Milestone[] _milestones;
    private readonly HashSet<string> _freeFromTheStart;

    /// <summary>Builds the progression for a game's blocks.</summary>
    /// <param name="blocks">The game's registered block types.</param>
    public Progression(GameBlocks blocks)
    {
        // Wood and stone are not a tier. A game that locked the first plank behind a milestone
        // would be a game whose first five minutes are a menu.
        _freeFromTheStart = ["planks", "stone brick"];

        _milestones =
        [
            new Milestone(
                "Make landfall",
                "You came ashore with nothing. Cut wood, break stone, and build something that "
                    + "will still be standing tomorrow.",
                [(blocks.Planks, 16), (blocks.StoneBrick, 24)],
                ["smelter"]
            ),
            new Milestone(
                "First smoke",
                "Rock gives up metal when it is made hot enough. Everything after this is a "
                    + "question of how much heat you can keep going at once.",
                [(blocks.CopperIngot, 20)],
                ["copper block"]
            ),
            new Milestone(
                "Into the dark",
                "The seams worth having are below the ones you can see. Take light with you; "
                    + "there is none down there and nothing down there makes any.",
                [(blocks.EmberOre, 6)],
                ["lantern"]
            ),
            new Milestone(
                "The line",
                "Carrying it yourself is the part that does not scale. Lay something down that "
                    + "carries it for you, and the shore stops being a walk and starts being a works.",
                [(blocks.CopperIngot, 24), (blocks.Planks, 24)],
                ["conveyor"]
            ),
            new Milestone(
                "Ironworks",
                "Iron is at the bottom, under the copper and under the coal. Reaching it is the "
                    + "last thing this shore asks of you that it has not already asked twice.",
                [(blocks.IronIngot, 24)],
                ["iron block"]
            ),
            new Milestone(
                "The long way up",
                "Everything worth having is below everything you want to build on. Carry it up "
                    + "once by hand and you will understand why nobody does it twice.",
                [(blocks.IronIngot, 16), (blocks.Planks, 32)],
                ["lift"]
            ),
            new Milestone(
                "Hands off",
                "A drill does not get tired and does not need to see. Put one on a seam, run a "
                    + "belt to a fire, and the shore will work itself while you sleep.",
                [(blocks.IronIngot, 40), (blocks.StoneBrick, 32)],
                ["miner"]
            ),
            new Milestone(
                "Current",
                "A fire under every machine is a fire you have to walk to. Put one fire "
                    + "somewhere sensible and run the heat to the rest on a wire.",
                [(blocks.CopperIngot, 48), (blocks.IronBlock, 4)],
                ["cable", "generator"]
            ),
            new Milestone(
                "Stand watch",
                "The louder the works, the more comes out of the dark for it. You cannot build "
                    + "less and you will not stay awake. Build something that does not sleep.",
                [(blocks.IronBlock, 8), (blocks.CopperBlock, 4)],
                ["turret"]
            ),
        ];
    }

    /// <summary>How many steps have been paid for.</summary>
    public int Reached { get; private set; }

    /// <summary>Every step, in order, for a screen that shows the whole line.</summary>
    public IReadOnlyList<Milestone> All => _milestones;

    /// <summary>Whether there is anything left to do.</summary>
    public bool IsFinished => Reached >= _milestones.Length;

    /// <summary>The step currently being worked towards, or nothing when they are all done.</summary>
    public Milestone? Next => IsFinished ? null : _milestones[Reached];

    /// <summary>Whether a recipe may be made yet.</summary>
    /// <param name="recipe">The recipe's name.</param>
    /// <returns><see langword="true" /> if it is free from the start or has been unlocked.</returns>
    public bool IsUnlocked(string recipe)
    {
        if (_freeFromTheStart.Contains(recipe))
            return true;

        for (var step = 0; step < Reached; step++)
        {
            if (_milestones[step].Unlocks.Contains(recipe))
                return true;
        }

        return false;
    }

    /// <summary>Whether the player is carrying enough to pay for the next step.</summary>
    /// <param name="inventory">What they are carrying.</param>
    /// <returns><see langword="true" /> if every part of the cost is covered.</returns>
    public bool CanAfford(Inventory inventory)
    {
        if (Next is not { } milestone)
            return false;

        foreach (var (block, count) in milestone.Cost)
        {
            if (inventory.CountOf(block) < count)
                return false;
        }

        return true;
    }

    /// <summary>Pays for the next step, taking the cost out of what is carried.</summary>
    /// <param name="inventory">What they are carrying, which this takes from.</param>
    /// <returns>The step that was taken, or nothing if it could not be paid for.</returns>
    /// <remarks>
    /// Checked in full before anything is taken, so a player who is one block short keeps all of
    /// the others. Taking what it can and asking for the rest later would be the same bug the
    /// crafting shortcut was written to avoid.
    /// </remarks>
    public Milestone? HandIn(Inventory inventory)
    {
        if (Next is not { } milestone || !CanAfford(inventory))
            return null;

        foreach (var (block, count) in milestone.Cost)
        {
            for (var one = 0; one < count; one++)
                inventory.TakeOneOf(block);
        }

        Reached++;
        return milestone;
    }

    /// <summary>Puts the player back at a step a save file recorded.</summary>
    /// <param name="reached">How many steps had been paid for.</param>
    /// <remarks>
    /// Clamped rather than trusted. A file written by a later build may name more steps than this
    /// one has, and the honest answer to that is to open the world with everything this build
    /// knows about unlocked.
    /// </remarks>
    public void Restore(int reached) => Reached = int.Clamp(reached, 0, _milestones.Length);
}
