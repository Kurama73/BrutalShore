namespace BrutalShore.Render;

/// <summary>
/// Turns a fraction of a day into the sun's position and the colours that follow from it.
/// </summary>
/// <remarks>
/// <para>
/// Only the shape of a day lives here — the arc the sun travels, and the palette the sky moves
/// through along it. How long a day lasts and what hour a world starts at are a game's business,
/// so this takes a fraction and keeps no clock of its own.
/// </para>
/// <para>
/// The palette is a set of constants rather than something passed in. A second game that wants a
/// different sky is the moment to lift them out into a value it can supply; until then a knob
/// with one caller is a knob nobody has tried.
/// </para>
/// </remarks>
public static class SunCycle
{
    /// <summary>The fraction of a day at which the sun crosses the eastern horizon.</summary>
    public const float Sunrise = 0.25f;

    /// <summary>The fraction of a day at which the sun crosses the western horizon.</summary>
    public const float Sunset = 0.75f;

    /// <summary>How far short of the zenith the sun passes at noon, in radians.</summary>
    /// <remarks>
    /// A sun that crosses the exact zenith lights all four vertical sides of a block equally for
    /// the hour around noon, and the world reads flat while it does. Leaning the arc keeps one
    /// side brighter than the other all day, which is what makes voxel terrain legible.
    /// </remarks>
    public const float DefaultNoonTiltRadians = 0.38f;

    /// <summary>
    /// The sun's elevation, as a sine, at which the night is taken to be fully over.
    /// </summary>
    /// <remarks>
    /// -0.105 is the sine of -6 degrees, the end of civil twilight: the point at which the sky
    /// stops being lit enough to read by. The daylight factor is zero below it.
    /// </remarks>
    private const float TwilightElevation = -0.105f;

    /// <summary>The sun's elevation, as a sine, at which daylight has reached full strength.</summary>
    private const float FullDaylightElevation = 0.21f;

    /// <summary>How close to the horizon the sun has to be for the horizon to catch fire.</summary>
    private const float DuskBand = 0.24f;

    private static readonly float3 DayZenith = new(0.17f, 0.38f, 0.72f);
    private static readonly float3 DayHorizon = new(0.50f, 0.66f, 0.88f);
    private static readonly float3 NightZenith = new(0.013f, 0.020f, 0.055f);
    private static readonly float3 NightHorizon = new(0.045f, 0.055f, 0.105f);
    private static readonly float3 DuskHorizon = new(0.95f, 0.44f, 0.20f);

    private static readonly float3 HighSun = new(0.80f, 0.776f, 0.736f);
    private static readonly float3 LowSun = new(0.82f, 0.46f, 0.25f);
    private static readonly float3 FullMoon = new(0.10f, 0.13f, 0.22f);

    private static readonly float3 DayAmbient = new(0.30f, 0.34f, 0.40f);
    private static readonly float3 NightAmbient = new(0.040f, 0.050f, 0.082f);

    /// <summary>Samples the cycle with the default tilt.</summary>
    /// <param name="dayFraction">
    /// Where in the day to sample: 0 is midnight, 0.25 sunrise, 0.5 noon, 0.75 sunset. Values
    /// outside one day wrap, so a clock that only ever counts up is a valid argument.
    /// </param>
    /// <returns>The light and colour of the sky at that moment.</returns>
    public static SkyLight At(double dayFraction) => At(dayFraction, DefaultNoonTiltRadians);

    /// <summary>Samples the cycle.</summary>
    /// <param name="dayFraction">
    /// Where in the day to sample: 0 is midnight, 0.25 sunrise, 0.5 noon, 0.75 sunset. Values
    /// outside one day wrap, so a clock that only ever counts up is a valid argument.
    /// </param>
    /// <param name="noonTiltRadians">
    /// How far short of the zenith the sun passes at noon; see
    /// <see cref="DefaultNoonTiltRadians"/>.
    /// </param>
    /// <returns>The light and colour of the sky at that moment.</returns>
    public static SkyLight At(double dayFraction, float noonTiltRadians)
    {
        var sun = SunDirectionAt(dayFraction, noonTiltRadians);
        var moon = -sun;

        var daylight = Smoothstep(TwilightElevation, FullDaylightElevation, sun.Y);

        // The sun reddens as it nears the horizon from either side, and the same fall-off drives
        // the band of colour the sky gets at dawn and dusk.
        var low = 1f - Saturate(float.Abs(sun.Y) / DuskBand);

        var sunStrength = Saturate(sun.Y / FullDaylightElevation);
        var moonStrength = Saturate(moon.Y / FullDaylightElevation);

        return new SkyLight
        {
            SunDirection = sun,
            SunColor = float3.Lerp(LowSun, HighSun, Saturate(sun.Y / DuskBand)) * sunStrength,
            MoonDirection = moon,
            MoonColor = FullMoon * moonStrength,
            AmbientColor = float3.Lerp(NightAmbient, DayAmbient, daylight),
            ZenithColor = float3.Lerp(NightZenith, DayZenith, daylight),
            HorizonColor = float3.Lerp(
                float3.Lerp(NightHorizon, DayHorizon, daylight),
                DuskHorizon,
                low * daylight
            ),
            Daylight = daylight,
        };
    }

    /// <summary>The unit direction towards the sun at a moment of the day.</summary>
    /// <param name="dayFraction">Where in the day to sample; see <see cref="At(double)"/>.</param>
    /// <param name="noonTiltRadians">
    /// How far short of the zenith the sun passes at noon; see
    /// <see cref="DefaultNoonTiltRadians"/>.
    /// </param>
    /// <returns>A unit vector pointing from the world towards the sun.</returns>
    /// <remarks>
    /// The arc is the great circle through due east and due west, rotated about the east-west
    /// axis by the tilt. That keeps sunrise and sunset exactly on the horizon whatever the tilt,
    /// and makes the tilt mean one readable thing: how far from overhead noon is.
    /// </remarks>
    public static float3 SunDirectionAt(double dayFraction, float noonTiltRadians)
    {
        // Reducing to one day before narrowing matters: a clock that only counts up would
        // otherwise hand f32 an angle large enough to quantise the sun's position visibly.
        var fraction = dayFraction - double.Floor(dayFraction);
        var angle = (float)((fraction - Sunrise) * double.Tau);
        var (sinAngle, cosAngle) = float.SinCos(angle);
        var (sinTilt, cosTilt) = float.SinCos(noonTiltRadians);
        return new float3(cosAngle, sinAngle * cosTilt, sinAngle * sinTilt);
    }

    private static float Saturate(float value) => float.Clamp(value, 0f, 1f);

    private static float Smoothstep(float edge0, float edge1, float value)
    {
        var t = Saturate((value - edge0) / (edge1 - edge0));
        return t * t * (3f - (2f * t));
    }
}
