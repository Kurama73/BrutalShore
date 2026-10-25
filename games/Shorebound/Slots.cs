using BrutalShore.Voxel;

namespace Shorebound;

/// <summary>A row of stacks and the handful of moves a player can make between them.</summary>
/// <remarks>
/// <para>
/// Everything a stack can be put in is one of these: what the player carries, and the grid on a
/// bench. They were the same code twice before there was a bench, which is what earned the type —
/// the moves are fiddly, every one of them has to conserve blocks, and the place that gets them
/// wrong is the one that was written second.
/// </para>
/// <para>
/// It knows nothing about what a slot means. Which slot is in hand, which ones are a crafting
/// grid and which are a pack are questions for whatever owns the slots, and keeping them out of
/// here is what lets both owners exist.
/// </para>
/// </remarks>
public sealed class Slots
{
    /// <summary>The most of one block a slot holds.</summary>
    /// <remarks>
    /// Sixty-four, which is the number every game of this kind uses, and for a reason worth
    /// keeping: it is large enough that a session of ordinary digging never fills a slot, and
    /// small enough that a full row is a real amount of carrying rather than a formality.
    /// </remarks>
    public const int StackSize = 64;

    private readonly Stack[] _stacks;

    /// <summary>Builds an empty set of slots.</summary>
    /// <param name="count">How many there are.</param>
    /// <exception cref="ArgumentOutOfRangeException">If there is not at least one.</exception>
    public Slots(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        _stacks = new Stack[count];
    }

    /// <summary>How many slots there are.</summary>
    public int Count => _stacks.Length;

    /// <summary>What is in a slot.</summary>
    /// <param name="index">Which slot, counting from zero.</param>
    /// <returns>Its stack, which is <see cref="Stack.Nothing" /> when the slot is empty.</returns>
    public Stack At(int index) => _stacks[index];

    /// <summary>Puts blocks in, filling part-full stacks of the same kind first.</summary>
    /// <param name="block">What to put in.</param>
    /// <param name="count">How many.</param>
    /// <returns>How many were taken; the rest did not fit.</returns>
    /// <remarks>
    /// Stacking before filling an empty slot is what keeps a small number of slots usable:
    /// without it a player who breaks a block, walks away and comes back has two slots of the
    /// same thing and the rest for everything else.
    /// </remarks>
    public int Add(BlockId block, int count)
    {
        if (block.IsAir || count <= 0)
            return 0;

        var taken = 0;

        for (var pass = 0; pass < 2 && taken < count; pass++)
        {
            for (var index = 0; index < _stacks.Length && taken < count; index++)
            {
                var slot = _stacks[index];
                var matches = pass == 0 ? slot.Count > 0 && slot.Block == block : slot.Count == 0;
                if (!matches)
                    continue;

                var room = StackSize - slot.Count;
                var moved = int.Min(room, count - taken);
                if (moved <= 0)
                    continue;

                _stacks[index] = new Stack(block, slot.Count + moved);
                taken += moved;
            }
        }

        return taken;
    }

    /// <summary>Takes one block out of a slot.</summary>
    /// <param name="index">Which slot.</param>
    /// <returns>Whether there was one to take.</returns>
    public bool TakeOne(int index)
    {
        var slot = _stacks[index];
        if (slot.Count <= 0)
            return false;

        _stacks[index] = slot.Count == 1 ? Stack.Nothing : new Stack(slot.Block, slot.Count - 1);
        return true;
    }

    /// <summary>Picks a whole stack up out of a slot, leaving it empty.</summary>
    /// <param name="index">Which slot; out-of-range values take nothing.</param>
    /// <returns>What was there.</returns>
    public Stack Take(int index)
    {
        if ((uint)index >= _stacks.Length)
            return Stack.Nothing;

        var slot = _stacks[index];
        _stacks[index] = Stack.Nothing;
        return slot;
    }

    /// <summary>Picks up half a slot, rounded up, the way a right click does.</summary>
    /// <param name="index">Which slot; out-of-range values take nothing.</param>
    /// <returns>The half that was picked up.</returns>
    /// <remarks>
    /// Rounded up so that half of one is one rather than nothing: a click that does nothing reads
    /// as a click that was missed.
    /// </remarks>
    public Stack TakeHalf(int index)
    {
        if ((uint)index >= _stacks.Length || _stacks[index].Count <= 0)
            return Stack.Nothing;

        var slot = _stacks[index];
        var half = (slot.Count + 1) / 2;
        _stacks[index] = half == slot.Count ? Stack.Nothing : new Stack(slot.Block, slot.Count - half);
        return new Stack(slot.Block, half);
    }

    /// <summary>
    /// Puts a held stack into a slot, merging it, filling it, or swapping with what is there.
    /// </summary>
    /// <param name="index">Which slot; out-of-range values take nothing.</param>
    /// <param name="held">What is being carried on the cursor.</param>
    /// <returns>What is left on the cursor afterwards.</returns>
    /// <remarks>
    /// The three outcomes are the three a player expects and they are decided by the slot, not by
    /// which button was pressed: the same thing onto the same thing merges as far as the stack
    /// will go, anything onto an empty slot fills it, and anything onto something else swaps. A
    /// merge that does not fit leaves the remainder on the cursor rather than spilling it.
    /// </remarks>
    public Stack Put(int index, Stack held)
    {
        if ((uint)index >= _stacks.Length || held.IsNothing)
            return held;

        var slot = _stacks[index];

        if (slot.Count > 0 && slot.Block == held.Block)
        {
            var moved = int.Min(StackSize - slot.Count, held.Count);
            _stacks[index] = new Stack(slot.Block, slot.Count + moved);
            return held.Count == moved ? Stack.Nothing : new Stack(held.Block, held.Count - moved);
        }

        _stacks[index] = new Stack(held.Block, int.Min(held.Count, StackSize));
        return slot.Count > 0 ? slot : Stack.Nothing;
    }

    /// <summary>Puts one block of a held stack into a slot, the way a right click does.</summary>
    /// <param name="index">Which slot; out-of-range values take nothing.</param>
    /// <param name="held">What is being carried on the cursor.</param>
    /// <returns>What is left on the cursor afterwards.</returns>
    public Stack PutOne(int index, Stack held)
    {
        if ((uint)index >= _stacks.Length || held.IsNothing)
            return held;

        var slot = _stacks[index];
        if (slot.Count >= StackSize || (slot.Count > 0 && slot.Block != held.Block))
            return held;

        _stacks[index] = new Stack(held.Block, slot.Count + 1);
        return held.Count == 1 ? Stack.Nothing : new Stack(held.Block, held.Count - 1);
    }

    /// <summary>How many of one block are in here altogether.</summary>
    /// <param name="block">The block to count.</param>
    /// <returns>The total across every slot.</returns>
    public int CountOf(BlockId block)
    {
        var total = 0;
        foreach (var slot in _stacks)
        {
            if (slot.Count > 0 && slot.Block == block)
                total += slot.Count;
        }

        return total;
    }

    /// <summary>Takes one of a particular block from wherever it is.</summary>
    /// <param name="block">What to take.</param>
    /// <returns>Whether there was one to take.</returns>
    /// <remarks>
    /// From the smallest stack rather than the first one found, so that filling a grid from the
    /// pack tidies up as it goes instead of breaking into a full stack while a slot holds three.
    /// </remarks>
    public bool TakeOneOf(BlockId block)
    {
        var from = -1;

        for (var index = 0; index < _stacks.Length; index++)
        {
            var slot = _stacks[index];
            if (slot.Count <= 0 || slot.Block != block)
                continue;

            if (from < 0 || slot.Count < _stacks[from].Count)
                from = index;
        }

        return from >= 0 && TakeOne(from);
    }

    /// <summary>Empties every slot.</summary>
    public void Clear() => Array.Clear(_stacks);
}
