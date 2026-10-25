namespace BrutalShore.Render;

/// <summary>
/// Where the sun and the moon are, and what colour the sky is, at one moment of a day.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of what the renderer is told about the time of day. The scene uniform block
/// is filled straight from it, and nothing downstream knows which hour produced these numbers.
/// What belongs here is anything a shader reads once per frame and cannot derive; what does not
/// belong here is the clock itself, which is a game's business.
/// </para>
/// <para>
/// Both directions point <em>towards</em> the body they name, because that is the form a shader
/// wants: a Lambert term is a dot product against the direction the light arrives from.
/// </para>
/// </remarks>
public readonly record struct SkyLight
{
    /// <summary>The unit direction towards the sun.</summary>
    public required float3 SunDirection { get; init; }

    /// <summary>
    /// Colour and strength of the sunlight, already zero once the sun is below the horizon.
    /// </summary>
    public required float3 SunColor { get; init; }

    /// <summary>The unit direction towards the moon.</summary>
    public required float3 MoonDirection { get; init; }

    /// <summary>
    /// Colour and strength of the moonlight, already zero once the moon is below the horizon.
    /// </summary>
    public required float3 MoonColor { get; init; }

    /// <summary>Colour and strength of the light the sky bounces back into faces nothing reaches.</summary>
    public required float3 AmbientColor { get; init; }

    /// <summary>The sky's colour straight overhead.</summary>
    public required float3 ZenithColor { get; init; }

    /// <summary>The sky's colour at the horizon, which is where a sunset shows.</summary>
    public required float3 HorizonColor { get; init; }

    /// <summary>How far the day has risen: 0 through the night, 1 once the sun is properly up.</summary>
    public required float Daylight { get; init; }

    /// <summary>
    /// The direction the shadow-casting light comes from — the sun while it is up, the moon
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// One shadow map is cast per frame, so the two lights have to be reduced to one. Taking
    /// whichever is above the horizon avoids the frame at dusk where a sun just below the horizon
    /// would stretch every shadow to the edge of the map.
    /// </remarks>
    public float3 KeyDirection => SunDirection.Y >= 0f ? SunDirection : MoonDirection;

    /// <summary>How visible the stars are: 1 through the night, 0 in full day.</summary>
    public float StarOpacity => 1f - Daylight;
}
