namespace Shorebound.Domain.Time;

/// <summary>How fast a day passes in Shorebound, and what time it is now.</summary>
/// <remarks>
/// <para>
/// The engine's <see cref="BrutalShore.Render.SunCycle" /> knows the shape of a day but holds no
/// clock, because how long a day lasts is a game's decision. This is that decision: twenty
/// minutes of real time, starting mid-morning so a new world opens in good light.
/// </para>
/// <para>
/// It counts in whole days as a <c>double</c> and never wraps, so nothing downstream has to
/// handle a day boundary; the cycle reduces the fraction itself.
/// </para>
/// </remarks>
public sealed class DayClock
{
    /// <summary>How long a whole day takes in real seconds, by default.</summary>
    /// <remarks>
    /// Twenty minutes is Minecraft's day, and it is the number players already have an instinct
    /// for: long enough that noon is not a flicker, short enough to see a sunset without waiting.
    /// </remarks>
    public const double DefaultDayLengthSeconds = 20.0 * 60.0;

    /// <summary>The hour a new world starts at, by default.</summary>
    private const double DefaultStartHour = 8.0;

    /// <summary>Starts the clock.</summary>
    /// <param name="startHour">The hour of the first morning, from 0 to 24.</param>
    /// <param name="dayLengthSeconds">
    /// How long a whole day takes in real seconds. Zero or less stops time, which is what
    /// <c>--day-length 0</c> is for: a fixed sun makes two screenshots comparable.
    /// </param>
    public DayClock(double startHour = DefaultStartHour, double dayLengthSeconds = DefaultDayLengthSeconds)
    {
        DayLengthSeconds = dayLengthSeconds;
        DayFraction = startHour / 24.0;
    }

    /// <summary>
    /// How long a whole day takes in real seconds. Zero or less stops time where it is.
    /// </summary>
    /// <remarks>
    /// Settable, because it is a setting: a player who moves the slider while standing in a
    /// sunset expects the sunset to slow down, not the next one to.
    /// </remarks>
    public double DayLengthSeconds { get; set; }

    /// <summary>How many days have passed, the fraction being the time of day.</summary>
    public double DayFraction { get; private set; }

    /// <summary>The hour of the day, from 0 up to 24.</summary>
    public double Hour => (DayFraction - double.Floor(DayFraction)) * 24.0;

    /// <summary>Whether the sun is up.</summary>
    /// <remarks>
    /// Six to six, which is what the sky is drawn from. It matters to more than the sky: what
    /// comes out of the dark comes out when this is false and burns when it turns true, so a
    /// player learns the hours the same way they learn where the ore is.
    /// </remarks>
    public bool IsDay => Hour is >= 6.0 and < 18.0;

    /// <summary>Whether time stands still.</summary>
    public bool IsFrozen { get; set; }

    /// <summary>Puts the clock at a particular moment, for a world being loaded.</summary>
    /// <param name="dayFraction">How many days have passed, the fraction being the time of day.</param>
    public void SetDayFraction(double dayFraction) => DayFraction = dayFraction;

    /// <summary>Advances the clock by one simulation step.</summary>
    /// <param name="seconds">How much real time the step covers.</param>
    public void Step(double seconds)
    {
        if (IsFrozen || DayLengthSeconds <= 0.0)
            return;

        DayFraction += seconds / DayLengthSeconds;
    }

    /// <summary>The time of day as <c>hh:mm</c>.</summary>
    /// <returns>A twenty-four hour clock reading.</returns>
    public override string ToString()
    {
        var hour = Hour;
        var minute = (int)((hour - double.Floor(hour)) * 60.0);
        return $"{(int)hour:00}:{minute:00}";
    }
}
