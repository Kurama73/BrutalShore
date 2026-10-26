namespace BrutalShore.Render;

/// <summary>A light that travels with the camera, because something is carrying it.</summary>
/// <remarks>
/// <para>
/// Every other light in this world is a block, and a block's light is worked out when its chunk
/// is meshed and baked into the vertices: exact, occluded correctly, and free every frame after.
/// What that cannot do is move. A chunk is re-meshed in a few milliseconds on a worker, which is
/// fine for a lantern being placed and useless for one being walked with.
/// </para>
/// <para>
/// So there is exactly one of these. One is enough for what it is for — seeing where you are
/// going — and one is what keeps it honest: it needs no shadow map and no ray marching, because
/// a light next to the eye reaches everything the eye can see. A second light somewhere else in
/// the world would need both, and the right answer for that is to make it a block.
/// </para>
/// </remarks>
public readonly record struct HeldLight
{
    /// <summary>Nothing carried, which is what most frames have.</summary>
    public static HeldLight None => default;

    /// <summary>Where it sits relative to the eye, in metres of render space.</summary>
    /// <remarks>
    /// Not at the eye exactly. A light at the eye lights every visible surface head on, which
    /// flattens everything it touches; a hand's length down and to the side gives the same
    /// surfaces a direction to be lit from, and keeps the player's own silhouette out of it.
    /// </remarks>
    public float3 OffsetMeters { get; init; }

    /// <summary>How far it reaches, in metres. Zero means there is nothing to draw.</summary>
    public float RangeMeters { get; init; }

    /// <summary>What colour it burns.</summary>
    public float3 Color { get; init; }

    /// <summary>How bright it is, 1 being as bright as the colour says.</summary>
    public float Strength { get; init; }

    /// <summary>Whether anything is being carried at all.</summary>
    public bool IsLit => RangeMeters > 0f && Strength > 0f;

    /// <summary>xyz: where it is. w: how far it reaches.</summary>
    internal float4 Position => new(OffsetMeters, RangeMeters);

    /// <summary>rgb: what colour it burns. w: how bright.</summary>
    internal float4 Burn => new(Color, Strength);
}
