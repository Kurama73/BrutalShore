using BrutalShore.Voxel;

namespace Shorebound;

/// <summary>How far through the game the player is, and what that lets them make.</summary>
/// <remarks>
/// <para>
/// A tree, but a tree that closes again. The objection to a branching progression is that a
/// choice between two branches is only worth offering when both are worth taking, and a game
/// without the content for that ends up with a dead branch nobody picks. The answer used here is
/// to branch and then rejoin: after the first smelt the shore opens two ways at once — light, to
/// go down, and belts, to stop carrying — and the drill at the far end needs what both of them
/// gave. The player chooses the order, both orders are played, and no content is written twice.
/// </para>
/// <para>
/// Which steps are done is the only state. What is open, what it costs and what it unlocks are
/// read off the table and off <see cref="Milestone.Requires" />, so changing the shape of the
/// game means editing one list — and the shape is checked when it is built, because a
/// requirement naming a step that does not exist would otherwise be a branch nobody can reach
/// and nothing would say so.
/// </para>
/// </remarks>
public sealed class Progression
{
    private readonly Milestone[] _milestones;
    private readonly HashSet<string> _freeFromTheStart;
    private readonly HashSet<string> _done = new(StringComparer.Ordinal);

    /// <summary>Builds the progression for a game's blocks.</summary>
    /// <param name="blocks">The game's registered block types.</param>
    /// <exception cref="InvalidOperationException">
    /// The table names a step twice, requires a step that does not exist, or requires itself
    /// around a loop.
    /// </exception>
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
                [],
                [(blocks.Planks, 16), (blocks.StoneBrick, 24)],
                ["smelter"]
            ),
            new Milestone(
                "First smoke",
                "Rock gives up metal when it is made hot enough. Everything after this is a "
                    + "question of how much heat you can keep going at once.",
                ["Make landfall"],
                [(blocks.CopperIngot, 20)],
                ["copper block"]
            ),
            new Milestone(
                "Into the dark",
                "The seams worth having are below the ones you can see. Take light with you; "
                    + "there is none down there and nothing down there makes any.",
                ["First smoke"],
                [(blocks.EmberOre, 6)],
                ["lantern"]
            ),
            new Milestone(
                "The line",
                "Carrying it yourself is the part that does not scale. Lay something down that "
                    + "carries it for you, and the shore stops being a walk and starts being a works.",
                ["First smoke"],
                [(blocks.CopperIngot, 24), (blocks.Planks, 24)],
                ["conveyor"]
            ),
            new Milestone(
                "Ironworks",
                "Iron is at the bottom, under the copper and under the coal. Reaching it is the "
                    + "last thing this shore asks of you that it has not already asked twice.",
                ["Into the dark"],
                [(blocks.IronIngot, 24)],
                ["iron block"]
            ),
            new Milestone(
                "The long way up",
                "Everything worth having is below everything you want to build on. Carry it up "
                    + "once by hand and you will understand why nobody does it twice.",
                ["The line"],
                [(blocks.CopperIngot, 16), (blocks.Planks, 32)],
                ["lift"]
            ),
            new Milestone(
                "Hands off",
                "A drill does not get tired and does not need to see. Put one on a seam, run a "
                    + "belt to a fire, and the shore will work itself while you sleep.",
                ["Ironworks", "The long way up"],
                [(blocks.IronIngot, 40), (blocks.StoneBrick, 32)],
                ["miner"]
            ),
            new Milestone(
                "Current",
                "A fire under every machine is a fire you have to walk to. Put one fire "
                    + "somewhere sensible and run the heat to the rest on a wire.",
                ["Hands off"],
                [(blocks.CopperIngot, 48), (blocks.IronBlock, 4)],
                ["cable", "generator"]
            ),
            new Milestone(
                "Stand watch",
                "The louder the works, the more comes out of the dark for it. You cannot build "
                    + "less and you will not stay awake. Build something that does not sleep.",
                ["Current"],
                [(blocks.IronBlock, 8), (blocks.CopperBlock, 4)],
                ["turret"]
            ),
        ];

        Check(_milestones);
    }

    /// <summary>How many steps have been paid for.</summary>
    public int DoneCount => _done.Count;

    /// <summary>Every step, in the order they are written down, for a screen that shows the tree.</summary>
    public IReadOnlyList<Milestone> All => _milestones;

    /// <summary>Whether there is anything left to do.</summary>
    public bool IsFinished => _done.Count >= _milestones.Length;

    /// <summary>The steps that can be worked towards now: not done, and everything they need is.</summary>
    /// <remarks>
    /// More than one of them for most of the game, which is the point of the tree. In the order
    /// they are written down, so a screen listing them lists them the same way twice running.
    /// </remarks>
    public IEnumerable<Milestone> Open => _milestones.Where(IsOpen);

    /// <summary>Whether a step has been paid for.</summary>
    /// <param name="name">The step's name.</param>
    /// <returns><see langword="true" /> if it is done.</returns>
    public bool IsDone(string name) => _done.Contains(name);

    /// <summary>Whether a step can be worked towards now.</summary>
    /// <param name="milestone">The step.</param>
    /// <returns><see langword="true" /> if it is not done and everything it needs is.</returns>
    public bool IsOpen(Milestone milestone) =>
        !_done.Contains(milestone.Name) && milestone.Requires.All(_done.Contains);

    /// <summary>What a step is still waiting for.</summary>
    /// <param name="milestone">The step.</param>
    /// <returns>The names of the steps it needs that are not done yet.</returns>
    /// <remarks>
    /// For the screen: a locked row that says what it is locked behind is a reason to go and do
    /// something else, and a locked row that does not is a dead end.
    /// </remarks>
    public IEnumerable<string> Waiting(Milestone milestone) =>
        milestone.Requires.Where(required => !_done.Contains(required));

    /// <summary>Whether a recipe may be made yet.</summary>
    /// <param name="recipe">The recipe's name.</param>
    /// <returns><see langword="true" /> if it is free from the start or has been unlocked.</returns>
    public bool IsUnlocked(string recipe)
    {
        if (_freeFromTheStart.Contains(recipe))
            return true;

        foreach (var milestone in _milestones)
        {
            if (_done.Contains(milestone.Name) && milestone.Unlocks.Contains(recipe))
                return true;
        }

        return false;
    }

    /// <summary>Whether the player is carrying enough to pay for a step.</summary>
    /// <param name="milestone">The step they want to pay for.</param>
    /// <param name="inventory">What they are carrying.</param>
    /// <returns><see langword="true" /> if it is open and every part of the cost is covered.</returns>
    public bool CanAfford(Milestone milestone, Inventory inventory)
    {
        if (!IsOpen(milestone))
            return false;

        foreach (var (block, count) in milestone.Cost)
        {
            if (inventory.CountOf(block) < count)
                return false;
        }

        return true;
    }

    /// <summary>Pays for a step, taking the cost out of what is carried.</summary>
    /// <param name="milestone">The step to pay for.</param>
    /// <param name="inventory">What they are carrying, which this takes from.</param>
    /// <returns>The step that was taken, or nothing if it could not be paid for.</returns>
    /// <remarks>
    /// Checked in full before anything is taken, so a player who is one block short keeps all of
    /// the others. Taking what it can and asking for the rest later would be the same bug the
    /// crafting shortcut was written to avoid.
    /// </remarks>
    public Milestone? HandIn(Milestone milestone, Inventory inventory)
    {
        if (!CanAfford(milestone, inventory))
            return null;

        foreach (var (block, count) in milestone.Cost)
        {
            for (var one = 0; one < count; one++)
                inventory.TakeOneOf(block);
        }

        _done.Add(milestone.Name);
        return milestone;
    }

    /// <summary>Puts the player back at the steps a save file recorded.</summary>
    /// <param name="done">The names of the steps that had been paid for.</param>
    /// <remarks>
    /// A name this build does not know about is dropped rather than refused. A world saved by a
    /// later build may name steps that have since been renamed or removed, and the honest answer
    /// to that is to open it with everything this build does recognise.
    /// </remarks>
    public void Restore(IEnumerable<string> done)
    {
        _done.Clear();

        foreach (var name in done)
        {
            if (_milestones.Any(milestone => string.Equals(milestone.Name, name, StringComparison.Ordinal)))
                _done.Add(name);
        }
    }

    /// <summary>Puts the player back at a count of steps, the way the progression used to be saved.</summary>
    /// <param name="count">How many steps had been paid for.</param>
    /// <remarks>
    /// The save format held one number while the progression was a line. The first steps in the
    /// order they are written down are the ones that line took, so reading an old world means
    /// marking that many of them done — which is why the table's order still matters even though
    /// nothing else reads it.
    /// </remarks>
    public void RestoreCount(int count)
    {
        _done.Clear();

        foreach (var milestone in _milestones.Take(int.Clamp(count, 0, _milestones.Length)))
            _done.Add(milestone.Name);
    }

    /// <summary>The names of the steps that are done, for a save file.</summary>
    /// <returns>Them in the order they are written down, so that two saves of one world match.</returns>
    public IReadOnlyList<string> DoneNames() =>
        _milestones.Where(milestone => _done.Contains(milestone.Name)).Select(milestone => milestone.Name).ToArray();

    /// <summary>Checks that the table describes a tree something can actually be reached through.</summary>
    /// <param name="milestones">The table as written.</param>
    /// <exception cref="InvalidOperationException">It does not.</exception>
    /// <remarks>
    /// Done by playing the tree out: take every step whose requirements are already taken, and
    /// repeat until nothing more can be. Whatever is left over is unreachable, which covers a
    /// misspelled requirement and a loop with the same check and the same message.
    /// </remarks>
    private static void Check(Milestone[] milestones)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var milestone in milestones)
        {
            if (!names.Add(milestone.Name))
                throw new InvalidOperationException($"Two milestones are called '{milestone.Name}'.");
        }

        var reached = new HashSet<string>(StringComparer.Ordinal);
        bool moved;

        do
        {
            moved = false;

            foreach (var milestone in milestones)
            {
                if (!reached.Contains(milestone.Name) && milestone.Requires.All(reached.Contains))
                    moved |= reached.Add(milestone.Name);
            }
        } while (moved);

        var stranded = milestones.Where(milestone => !reached.Contains(milestone.Name)).ToArray();
        if (stranded.Length > 0)
        {
            throw new InvalidOperationException(
                $"'{stranded[0].Name}' cannot be reached: it requires "
                    + $"'{string.Join("', '", stranded[0].Requires.Where(required => !reached.Contains(required)))}'."
            );
        }
    }
}
