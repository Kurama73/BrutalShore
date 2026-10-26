namespace BrutalShore.Voxel;

/// <summary>What a block's surface looks like closer than a whole block.</summary>
/// <remarks>
/// <para>
/// A pattern, not a texture. The shader works the detail out from where it is on the face and
/// which block in the world the face belongs to, so there is no image to load, no atlas to pack,
/// no filtering to get wrong at a distance, and nothing in memory — and the detail is as fine as
/// the screen rather than as fine as a texel.
/// </para>
/// <para>
/// Named after what the surface does rather than after the block, because the point of the list
/// is that a different game reuses it: stone is mottled, so is concrete, so is the inside of an
/// asteroid. A game that wants a pattern nothing here describes adds one to the shader and a name
/// to this list, and the four bits a vertex gives it bound that list at sixteen.
/// </para>
/// </remarks>
public enum BlockPattern : byte
{
    /// <summary>One flat colour, which is what everything was before there were patterns.</summary>
    Smooth = 0,

    /// <summary>Patches a few shades apart, as rock is.</summary>
    Mottled = 1,

    /// <summary>Loose grains at the size of a pebble, as earth and gravel are.</summary>
    Crumbly = 2,

    /// <summary>Fine upright strokes, as grass is.</summary>
    Bladed = 3,

    /// <summary>Long lines along one axis with knots in them, as cut wood is.</summary>
    Grained = 4,

    /// <summary>Clumps with gaps between them, as a canopy is.</summary>
    Scattered = 5,

    /// <summary>Flat and pale with the odd bright fleck, as snow is.</summary>
    Glinting = 6,

    /// <summary>Slow overlapping waves, as still water is.</summary>
    Rippled = 7,

    /// <summary>Bright at the middle and banded, as something giving off light is.</summary>
    Radiant = 8,

    /// <summary>Fine even dots, as sand is.</summary>
    Grainy = 9,
}
