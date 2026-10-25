using BrutalShore.Ui;
using BrutalShore.Voxel;

namespace Shorebound;

/// <summary>The blocks the player can place, and which one is in hand.</summary>
/// <remarks>
/// A fixed row rather than an inventory: nothing is collected, nothing runs out, and the only
/// question is which block the next click puts down. An inventory is a different game's problem
/// and would arrive with its own reasons.
/// </remarks>
public sealed class Hotbar
{
    private readonly (string Name, BlockId Block, float4 Color, byte Emission)[] _slots;

    /// <summary>Builds the row from this game's blocks.</summary>
    /// <param name="blocks">The game's registered block types.</param>
    public Hotbar(GameBlocks blocks)
    {
        // The colour is taken once, here, because the interface draws a swatch of it every frame
        // and a block's colour is the one thing about it that never changes.
        (string, BlockId, float4, byte) Slot(string name, BlockId block)
        {
            var type = blocks.Registry[block];
            var color = type.Color;
            return (name, block, UiPalette.FromSrgb(color.X, color.Y, color.Z), type.Emission);
        }

        // Water is left out on purpose: it is drawn as a solid block, so placing one would make a
        // cube of sea rather than anything that flows.
        _slots =
        [
            Slot("Stone", blocks.Stone),
            Slot("Dirt", blocks.Dirt),
            Slot("Grass", blocks.Grass),
            Slot("Sand", blocks.Sand),
            Slot("Snow", blocks.Snow),
            Slot("Log", blocks.Log),
            Slot("Leaves", blocks.Leaves),
            Slot("Lantern", blocks.Lantern),
            Slot("Bedrock", blocks.Bedrock),
        ];
    }

    /// <summary>How many slots there are.</summary>
    public int Count => _slots.Length;

    /// <summary>Which slot is in hand, counting from zero.</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>The block the next placement would put down.</summary>
    public BlockId Selected => _slots[SelectedIndex].Block;

    /// <summary>The name of the block in hand.</summary>
    public string SelectedName => _slots[SelectedIndex].Name;

    /// <summary>The name of a slot, for drawing the row.</summary>
    /// <param name="index">Which slot, counting from zero.</param>
    /// <returns>Its block's name.</returns>
    public string NameOf(int index) => _slots[index].Name;

    /// <summary>What light the block in hand gives off, 0 for most of them and up to 15.</summary>
    /// <remarks>
    /// Holding a lantern lights the way. Taken from the block type rather than stated here, so a
    /// second glowing block is lit by the same rule the day it is registered.
    /// </remarks>
    public byte SelectedEmission => _slots[SelectedIndex].Emission;

    /// <summary>The colour of a slot's block, for the swatch the interface draws.</summary>
    /// <param name="index">Which slot, counting from zero.</param>
    /// <returns>Its block's colour, in the linear light the interface draws in.</returns>
    public float4 ColorOf(int index) => _slots[index].Color;

    /// <summary>Puts a particular slot in hand.</summary>
    /// <param name="index">Which slot; out-of-range values are ignored.</param>
    public void Select(int index)
    {
        if ((uint)index < _slots.Length)
            SelectedIndex = index;
    }

    /// <summary>Moves along the row, wrapping at either end.</summary>
    /// <param name="by">How many slots to move; negative goes left.</param>
    public void Cycle(int by) =>
        SelectedIndex = (int)(((SelectedIndex + by) % _slots.Length + _slots.Length) % _slots.Length);
}
