using BrutalShore.Ui;
using BrutalShore.Voxel;

namespace Shorebound;

/// <summary>The nine slots the player carries blocks in, and which one is in hand.</summary>
/// <remarks>
/// <para>
/// A row of stacks rather than a palette. It used to be a palette — nine blocks that never ran
/// out — and that made placing a block free, which made breaking one pointless: a world where
/// anything can be put anywhere at any time is a world with nothing to do in it.
/// </para>
/// <para>
/// So a block comes from somewhere. Breaking one drops it, walking over it picks it up, placing
/// one spends it, and the row starts empty. That is the whole economy and it is deliberately the
/// whole of it: no chests, no crafting, no tools. Those arrive when they have a reason, and the
/// reason is never "a game like this one has them".
/// </para>
/// </remarks>
public sealed class Inventory
{
    /// <summary>How many slots there are.</summary>
    public const int SlotCount = 9;

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

    /// <summary>How many slots there are.</summary>
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
    /// Stacking before filling an empty slot is what keeps nine slots usable: without it a
    /// player who breaks a block, walks away and comes back has two slots of the same thing and
    /// seven for everything else.
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

    /// <summary>Puts a particular slot in hand.</summary>
    /// <param name="index">Which slot; out-of-range values are ignored.</param>
    public void Select(int index)
    {
        if ((uint)index < SlotCount)
            SelectedIndex = index;
    }

    /// <summary>Moves along the row, wrapping at either end.</summary>
    /// <param name="by">How many slots to move; negative goes left.</param>
    public void Cycle(int by) =>
        SelectedIndex = (int)(((SelectedIndex + by) % SlotCount + SlotCount) % SlotCount);

    /// <summary>One slot: a block and how many of it, or nothing.</summary>
    private readonly record struct Slot(BlockId Block, int Count);
}
