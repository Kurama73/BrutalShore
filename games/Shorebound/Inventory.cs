using BrutalShore.Ui;
using BrutalShore.Voxel;

namespace Shorebound;

/// <summary>Every slot the player carries blocks in, and which of them is in hand.</summary>
/// <remarks>
/// <para>
/// Stacks rather than a palette. It used to be a palette — nine blocks that never ran out — and
/// that made placing a block free, which made breaking one pointless: a world where anything can
/// be put anywhere at any time is a world with nothing to do in it.
/// </para>
/// <para>
/// So a block comes from somewhere. Breaking one drops it, walking over it picks it up, placing
/// one spends it, and it all starts empty.
/// </para>
/// <para>
/// The slots are one array, not two. The first <see cref="RowCount" /> of them are the row along
/// the bottom of the screen, and the rest are the pack behind it; what makes them different is
/// where they are drawn and that only the row can be in hand. Keeping them in one array is what
/// lets a stack move between the two by writing two slots, and lets <see cref="Add" /> walk the
/// whole of what is carried without knowing there is a division in it.
/// </para>
/// </remarks>
public sealed class Inventory
{
    /// <summary>How many slots are in the row along the bottom of the screen.</summary>
    public const int RowCount = 9;

    /// <summary>How many slots there are altogether, the row included.</summary>
    /// <remarks>
    /// Four rows of nine. One row is not enough to come back from a morning's digging with
    /// anything but stone, and a player who has to throw away what they dug up stops digging; far
    /// more than four and the screen stops being readable at a glance, which is the only thing a
    /// grid of coloured squares has going for it.
    /// </remarks>
    public const int SlotCount = RowCount * 4;

    /// <summary>The most of one block a slot holds.</summary>
    /// <remarks>
    /// Sixty-four, which is the number every game of this kind uses, and for a reason worth
    /// keeping: it is large enough that a session of ordinary digging never fills a slot, and
    /// small enough that a full row is a real amount of carrying rather than a formality.
    /// </remarks>
    public const int StackSize = 64;

    private readonly GameBlocks _blocks;
    private readonly Slot[] _slots = new Slot[SlotCount];

    /// <summary>Builds an empty row.</summary>
    /// <param name="blocks">The game's registered block types, for names and colours.</param>
    public Inventory(GameBlocks blocks) => _blocks = blocks;

    /// <summary>How many slots there are altogether.</summary>
    public int Count => SlotCount;

    /// <summary>Which slot is in hand, counting from zero.</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>The block the next placement would put down, or air when the slot is empty.</summary>
    public BlockId Selected => _slots[SelectedIndex].Block;

    /// <summary>Whether there is anything in hand to place.</summary>
    public bool HasSelection => _slots[SelectedIndex].Count > 0;

    /// <summary>The name of what is in hand, or nothing when the slot is empty.</summary>
    public string SelectedName => HasSelection ? NameOf(SelectedIndex) : "Empty-handed";

    /// <summary>What light the block in hand gives off, 0 for most of them and up to 15.</summary>
    /// <remarks>
    /// Holding a lantern lights the way. Taken from the block type rather than stated here, so a
    /// second glowing block is lit by the same rule the day it is registered.
    /// </remarks>
    public byte SelectedEmission =>
        HasSelection ? _blocks.Registry[_slots[SelectedIndex].Block].Emission : (byte)0;

    /// <summary>The name of a slot's block, for drawing the row.</summary>
    /// <param name="index">Which slot, counting from zero.</param>
    /// <returns>Its block's name, or an empty string.</returns>
    public string NameOf(int index) =>
        _slots[index].Count > 0 ? _blocks.Registry[_slots[index].Block].Name : string.Empty;

    /// <summary>What is in a slot and how much of it.</summary>
    /// <param name="index">Which slot, counting from zero.</param>
    /// <returns>The block and the count, which is zero for an empty slot.</returns>
    public (BlockId Block, int Count) At(int index) => (_slots[index].Block, _slots[index].Count);

    /// <summary>The name of a block, for labelling a slot the player is pointing at.</summary>
    /// <param name="block">The block to name.</param>
    /// <returns>Its registered name.</returns>
    public string NameOf(BlockId block) => _blocks.Registry[block].Name;

    /// <summary>The colour of a block, for the swatch the interface draws.</summary>
    /// <param name="block">The block to colour.</param>
    /// <returns>Its colour, in the linear light the interface draws in.</returns>
    public float4 ColorOf(BlockId block)
    {
        var color = _blocks.Registry[block].Color;
        return UiPalette.FromSrgb(color.X, color.Y, color.Z);
    }

    /// <summary>The colour of a slot's block, for the swatch the interface draws.</summary>
    /// <param name="index">Which slot, counting from zero.</param>
    /// <returns>Its block's colour, in the linear light the interface draws in.</returns>
    public float4 ColorOf(int index)
    {
        if (_slots[index].Count <= 0)
            return float4.Zero;

        var color = _blocks.Registry[_slots[index].Block].Color;
        return UiPalette.FromSrgb(color.X, color.Y, color.Z);
    }

    /// <summary>Puts blocks into the row, filling part-full stacks of the same kind first.</summary>
    /// <param name="block">What to put in.</param>
    /// <param name="count">How many.</param>
    /// <returns>How many were taken; the rest did not fit.</returns>
    /// <remarks>
    /// <para>
    /// Stacking before filling an empty slot is what keeps the slots usable: without it a player
    /// who breaks a block, walks away and comes back has two slots of the same thing and the rest
    /// for everything else.
    /// </para>
    /// <para>
    /// Both passes run from slot zero, so the row fills before the pack does. That is what makes
    /// a block picked up off the ground immediately placeable, which is the whole of what the row
    /// is for.
    /// </para>
    /// </remarks>
    public int Add(BlockId block, int count)
    {
        if (block.IsAir || count <= 0)
            return 0;

        var taken = 0;

        for (var pass = 0; pass < 2 && taken < count; pass++)
        {
            for (var index = 0; index < SlotCount && taken < count; index++)
            {
                var slot = _slots[index];
                var matches = pass == 0 ? slot.Count > 0 && slot.Block == block : slot.Count == 0;
                if (!matches)
                    continue;

                var room = StackSize - slot.Count;
                var moved = int.Min(room, count - taken);
                if (moved <= 0)
                    continue;

                _slots[index] = new Slot(block, slot.Count + moved);
                taken += moved;
            }
        }

        return taken;
    }

    /// <summary>Takes one block out of the slot in hand.</summary>
    /// <returns>Whether there was one to take.</returns>
    public bool TakeOne()
    {
        var slot = _slots[SelectedIndex];
        if (slot.Count <= 0)
            return false;

        _slots[SelectedIndex] = slot.Count == 1 ? default : new Slot(slot.Block, slot.Count - 1);
        return true;
    }

    /// <summary>Empties every slot, as starting a fresh world does.</summary>
    public void Clear() => Array.Clear(_slots);

    /// <summary>What is in every slot, by block name, for a save file.</summary>
    /// <param name="registry">The registry that gives each block its name.</param>
    /// <returns>A name and a count per slot, empty slots included, in slot order.</returns>
    public IReadOnlyList<(string Block, int Count)> Carried(BlockRegistry registry)
    {
        var carried = new List<(string, int)>(SlotCount);
        foreach (var slot in _slots)
            carried.Add((slot.Count > 0 ? registry[slot.Block].Name : string.Empty, slot.Count));

        return carried;
    }

    /// <summary>Puts back what a save file said was being carried.</summary>
    /// <param name="carried">A name and a count per slot, as <see cref="Carried" /> wrote them.</param>
    /// <param name="registry">The registry that resolves each name.</param>
    /// <remarks>
    /// A name this build does not know is dropped rather than refused, the same way an edit to a
    /// block that no longer exists is: a world that will not open because of a block someone took
    /// out between two versions is worse than a world that opens a slot short.
    /// </remarks>
    public void Restore(IReadOnlyList<(string Block, int Count)> carried, BlockRegistry registry)
    {
        Clear();

        for (var index = 0; index < int.Min(carried.Count, SlotCount); index++)
        {
            var (name, count) = carried[index];
            if (count <= 0 || string.IsNullOrEmpty(name))
                continue;

            if (registry.Find(name) is { } block)
                _slots[index] = new Slot(block, int.Min(count, StackSize));
        }
    }

    /// <summary>Puts a particular slot of the row in hand.</summary>
    /// <param name="index">Which slot; anything outside the row is ignored.</param>
    public void Select(int index)
    {
        if ((uint)index < RowCount)
            SelectedIndex = index;
    }

    /// <summary>Moves along the row, wrapping at either end.</summary>
    /// <param name="by">How many slots to move; negative goes left.</param>
    public void Cycle(int by) =>
        SelectedIndex = (int)(((SelectedIndex + by) % RowCount + RowCount) % RowCount);

    /// <summary>Picks a whole stack up out of a slot, leaving it empty.</summary>
    /// <param name="index">Which slot; out-of-range values take nothing.</param>
    /// <returns>What was there, which is <see cref="Stack.Nothing" /> for an empty slot.</returns>
    public Stack Take(int index)
    {
        if ((uint)index >= SlotCount)
            return Stack.Nothing;

        var slot = _slots[index];
        _slots[index] = default;
        return new Stack(slot.Block, slot.Count);
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
        if ((uint)index >= SlotCount || _slots[index].Count <= 0)
            return Stack.Nothing;

        var slot = _slots[index];
        var half = (slot.Count + 1) / 2;
        _slots[index] = half == slot.Count ? default : new Slot(slot.Block, slot.Count - half);
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
        if ((uint)index >= SlotCount || held.IsNothing)
            return held;

        var slot = _slots[index];

        if (slot.Count > 0 && slot.Block == held.Block)
        {
            var moved = int.Min(StackSize - slot.Count, held.Count);
            _slots[index] = new Slot(slot.Block, slot.Count + moved);
            return held.Count == moved ? Stack.Nothing : new Stack(held.Block, held.Count - moved);
        }

        _slots[index] = new Slot(held.Block, int.Min(held.Count, StackSize));
        return slot.Count > 0 ? new Stack(slot.Block, slot.Count) : Stack.Nothing;
    }

    /// <summary>Puts one block of a held stack into a slot, the way a right click does.</summary>
    /// <param name="index">Which slot; out-of-range values take nothing.</param>
    /// <param name="held">What is being carried on the cursor.</param>
    /// <returns>What is left on the cursor afterwards.</returns>
    public Stack PutOne(int index, Stack held)
    {
        if ((uint)index >= SlotCount || held.IsNothing)
            return held;

        var slot = _slots[index];
        if (slot.Count >= StackSize || (slot.Count > 0 && slot.Block != held.Block))
            return held;

        _slots[index] = new Slot(held.Block, slot.Count + 1);
        return held.Count == 1 ? Stack.Nothing : new Stack(held.Block, held.Count - 1);
    }

    /// <summary>One slot: a block and how many of it, or nothing.</summary>
    private readonly record struct Slot(BlockId Block, int Count);
}
