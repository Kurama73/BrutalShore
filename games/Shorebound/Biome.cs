using BrutalShore.Voxel;

namespace Shorebound;

/// <summary>What one stretch of this world is made of.</summary>
/// <remarks>
/// Deliberately thin. A biome here decides what the ground is and how much grows on it, and
/// nothing else — not the height of the land, because a height that changed between biomes would
/// put a cliff along every border unless something blended it, and blending heights is a much
/// larger piece of work than this world needs to stop looking like one field.
/// </remarks>
public readonly record struct Biome
{
    /// <summary>What it is called, for the debug overlay.</summary>
    public required string Name { get; init; }

    /// <summary>The block the top of the ground is made of.</summary>
    public required BlockId Surface { get; init; }

    /// <summary>The block the few metres under that are made of.</summary>
    public required BlockId Subsurface { get; init; }

    /// <summary>How much of the usual forest grows here, from 0 for none to 1 for all of it.</summary>
    public required float TreeChance { get; init; }
}
