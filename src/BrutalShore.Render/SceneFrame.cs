namespace BrutalShore.Render;

/// <summary>Everything the renderer needs to be told once, for one frame.</summary>
/// <remarks>
/// <para>
/// A value rather than a long parameter list, because the list kept growing: the camera, then the
/// sun, then the fog, then the light's matrix, then what the eye is submerged in. Each of those
/// is one frame's answer to one question, and naming them at the point they are set is worth more
/// than the brevity of passing them positionally.
/// </para>
/// <para>
/// The ones with defaults are the ones a game can ignore and the renderer still looks right. They
/// are also the knobs a debug interface reaches for — turning shadows off to see what they cost,
/// pushing fog away to see how far the world really goes — which is exactly why they are settings
/// rather than constants somewhere inside.
/// </para>
/// </remarks>
public readonly record struct SceneFrame
{
    /// <summary>The camera's view-projection, in render space.</summary>
    public required float4x4 ViewProjection { get; init; }

    /// <summary>Render space to the shadow map's clip space; see <see cref="SunShadow" />.</summary>
    public required float4x4 LightViewProjection { get; init; }

    /// <summary>The sun, the moon and the sky's colours at this moment.</summary>
    public required SkyLight Sky { get; init; }

    /// <summary>How far away fog begins, in metres.</summary>
    public required float FogStartMeters { get; init; }

    /// <summary>How far away fog is total, in metres.</summary>
    public required float FogEndMeters { get; init; }

    /// <summary>
    /// rgb: the colour everything takes on while the eye is inside a liquid. w: how much of it
    /// applies. All zero when the eye is in the open.
    /// </summary>
    public float4 Submerged { get; init; }

    /// <summary>How much of its direct light a fully shadowed surface loses.</summary>
    /// <remarks>
    /// Not quite all of it by default. The ambient term already keeps a shadow from going black,
    /// but a sliver of direct light left in means a surface still shows which way it faces inside
    /// one, which is what keeps a shadowed cliff from reading as a flat silhouette. Zero turns
    /// shadows off without taking the pass out of the frame, which is what makes it useful for
    /// telling what they are worth.
    /// </remarks>
    public float ShadowStrength { get; init; } = 0.9f;

    /// <summary>How far along its own normal a surface is nudged before it is looked up.</summary>
    /// <remarks>
    /// Six centimetres, against blocks a metre across. Every surface in this world is an axis
    /// aligned plane, so moving the lookup off the surface is worth more than any depth bias:
    /// it scales with nothing and cannot detach a shadow from the thing casting it.
    /// </remarks>
    public float ShadowNormalOffsetMeters { get; init; } = 0.06f;

    /// <summary>How much every light is scaled before anything is drawn with it, 1 being none.</summary>
    /// <remarks>
    /// Applied to the light rather than to the finished pixel, so what it moves is the exposure
    /// of the scene and not the strength of the fog in front of it. The sky is scaled by the same
    /// number in its own shader, because the far edge of the world fades into the sky and the two
    /// have to agree or the horizon grows a band.
    /// </remarks>
    public float Brightness { get; init; } = 1f;

    /// <summary>The light the player is carrying, if any.</summary>
    public HeldLight Held { get; init; } = HeldLight.None;

    /// <summary>Builds a frame with the settings that are not required at their defaults.</summary>
    public SceneFrame() { }
}
