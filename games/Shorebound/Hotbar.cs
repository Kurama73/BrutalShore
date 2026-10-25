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
    private readonly (string Name, BlockId Block)[] _slots;

    /// <summary>Builds the row from this game's blocks.</summary>
    /// <param name="blocks">The game's registered block types.</param>
    public Hotbar(GameBlocks blocks)
    {
        // Water is left out on purpose: it is drawn as a solid block, so placing one would make a
        // cube of sea rather than anything that flows.
        _slots =
        [
            ("Stone", blocks.Stone),
            ("Dirt", blocks.Dirt),
            ("Grass", blocks.Grass),
            ("Sand", blocks.Sand),
            ("Snow", blocks.Snow),
            ("Log", blocks.Log),
            ("Leaves", blocks.Leaves),
            ("Bedrock", blocks.Bedrock),
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
