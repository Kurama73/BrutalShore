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
/// The slots are one <see cref="Slots" />, not two. The first <see cref="RowCount" /> of them are
/// the row along the bottom of the screen, and the rest are the pack behind it; what makes them
/// different is where they are drawn and that only the row can be in hand. Keeping them in one
/// set is what lets a stack move between the two by writing two slots, and lets a block picked up
/// off the ground walk the whole of what is carried without knowing there is a division in it.
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
    public const int StackSize = Slots.StackSize;

    private readonly GameBlocks _blocks;
    private readonly Slots _slots = new(SlotCount);

    /// <summary>Builds an empty inventory.</summary>
    /// <param name="blocks">The game's registered block types, for names and colours.</param>
    public Inventory(GameBlocks blocks) => _blocks = blocks;

    /// <summary>How many slots there are altogether.</summary>
    public int Count => SlotCount;

    /// <summary>Which slot of the row is in hand, counting from zero.</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>The block the next placement would put down, or air when the slot is empty.</summary>
    public BlockId Selected => _slots.At(SelectedIndex).Block;

    /// <summary>Whether there is anything in hand to place.</summary>
    public bool HasSelection => _slots.At(SelectedIndex).Count > 0;

    /// <summary>The name of what is in hand, or nothing when the slot is empty.</summary>
    public string SelectedName => HasSelection ? NameOf(SelectedIndex) : "Empty-handed";

    /// <summary>What light the block in hand gives off, 0 for most of them and up to 15.</summary>
    /// <remarks>
    /// Holding a lantern lights the way. Taken from the block type rather than stated here, so a
    /// second glowing block is lit by the same rule the day it is registered.
    /// </remarks>
    public byte SelectedEmission =>
        HasSelection ? _blocks.Registry[Selected].Emission : (byte)0;

    /// <summary>The name of a slot's block, for drawing the row.</summary>
    /// <param name="index">Which slot, counting from zero.</param>
    /// <returns>Its block's name, or an empty string.</returns>
    public string NameOf(int index) =>
        _slots.At(index).Count > 0 ? _blocks.Registry[_slots.At(index).Block].Name : string.Empty;

    /// <summary>The name of a block, for labelling a slot the player is pointing at.</summary>
    /// <param name="block">The block to name.</param>
    /// <returns>Its registered name.</returns>
    public string NameOf(BlockId block) => _blocks.Registry[block].Name;

    /// <summary>What is in a slot and how much of it.</summary>
    /// <param name="index">Which slot, counting from zero.</param>
    /// <returns>The block and the count, which is zero for an empty slot.</returns>
    public (BlockId Block, int Count) At(int index) => (_slots.At(index).Block, _slots.At(index).Count);

    /// <summary>The colour of a slot's block, for the swatch the interface draws.</summary>
    /// <param name="index">Which slot, counting from zero.</param>
    /// <returns>Its block's colour, in the linear light the interface draws in.</returns>
    public float4 ColorOf(int index) =>
        _slots.At(index).Count <= 0 ? float4.Zero : ColorOf(_slots.At(index).Block);

    /// <summary>The colour of a block, for the swatch the interface draws.</summary>
    /// <param name="block">The block to colour.</param>
    /// <returns>Its colour, in the linear light the interface draws in.</returns>
    public float4 ColorOf(BlockId block)
    {
        var color = _blocks.Registry[block].Color;
        return UiPalette.FromSrgb(color.X, color.Y, color.Z);
    }

    /// <summary>Puts blocks into the inventory, filling the row before the pack.</summary>
    /// <param name="block">What to put in.</param>
    /// <param name="count">How many.</param>
    /// <returns>How many were taken; the rest did not fit.</returns>
    /// <remarks>
    /// The row fills first because <see cref="Slots.Add" /> runs from slot zero and the row is at
    /// the front. That is what makes a block picked up off the ground immediately placeable,
    /// which is the whole of what the row is for.
    /// </remarks>
    public int Add(BlockId block, int count) => _slots.Add(block, count);

    /// <summary>Takes one block out of the slot in hand.</summary>
    /// <returns>Whether there was one to take.</returns>
    public bool TakeOne() => _slots.TakeOne(SelectedIndex);

    /// <summary>Picks a whole stack up out of a slot, leaving it empty.</summary>
    /// <param name="index">Which slot; out-of-range values take nothing.</param>
    /// <returns>What was there.</returns>
    public Stack Take(int index) => _slots.Take(index);

    /// <summary>Picks up half a slot, rounded up, the way a right click does.</summary>
    /// <param name="index">Which slot; out-of-range values take nothing.</param>
    /// <returns>The half that was picked up.</returns>
    public Stack TakeHalf(int index) => _slots.TakeHalf(index);

    /// <summary>Puts a held stack into a slot, merging, filling or swapping.</summary>
    /// <param name="index">Which slot; out-of-range values take nothing.</param>
    /// <param name="held">What is being carried on the cursor.</param>
    /// <returns>What is left on the cursor afterwards.</returns>
    public Stack Put(int index, Stack held) => _slots.Put(index, held);

    /// <summary>Puts one block of a held stack into a slot, the way a right click does.</summary>
    /// <param name="index">Which slot; out-of-range values take nothing.</param>
    /// <param name="held">What is being carried on the cursor.</param>
    /// <returns>What is left on the cursor afterwards.</returns>
    public Stack PutOne(int index, Stack held) => _slots.PutOne(index, held);

    /// <summary>Empties every slot, as starting a fresh world does.</summary>
    public void Clear() => _slots.Clear();

    /// <summary>What is in every slot, by block name, for a save file.</summary>
    /// <param name="registry">The registry that gives each block its name.</param>
    /// <returns>A name and a count per slot, empty slots included, in slot order.</returns>
    public IReadOnlyList<(string Block, int Count)> Carried(BlockRegistry registry)
    {
        var carried = new List<(string, int)>(SlotCount);
        for (var index = 0; index < SlotCount; index++)
        {
            var slot = _slots.At(index);
            carried.Add((slot.Count > 0 ? registry[slot.Block].Name : string.Empty, slot.Count));
        }

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
                _slots.Put(index, new Stack(block, int.Min(count, StackSize)));
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
}
