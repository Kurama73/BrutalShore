namespace RustSpace.Core;

/// <summary>
/// Simulated time, and how fast it runs compared to the wall clock.
/// </summary>
/// <remarks>
/// <para>
/// Time warp here is a multiplier on elapsed seconds and nothing else, because the bodies are
/// propagated analytically: asking for the state a thousand years from now costs exactly what
/// asking for the state one second from now costs, and carries no accumulated stepping error.
/// The moment anything is integrated — a burn, a contact, a re-entry — warp stops being free and
/// has to be capped against the step size that integration needs. That is a deliberate boundary,
/// not an oversight.
/// </para>
/// <para>
/// Elapsed time is held as f64 seconds since the epoch. At the largest warp here, a session runs
/// to a few thousand years, where an f64 second still resolves to well under a microsecond.
/// </para>
/// </remarks>
public sealed class SimulationClock
{
    /// <summary>
    /// The J2000.0 epoch: 2000-01-01 12:00:00 TT, which the mean orbital elements are given at.
    /// </summary>
    public static readonly DateTime J2000Epoch = new(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The warp factors offered, each a step the player can reach rather than a free slider, so a
    /// chosen rate is reproducible and nameable.
    /// </summary>
    public static readonly double[] WarpSteps =
    [
        0.0, 1.0, 10.0, 100.0, 1_000.0, 10_000.0, 100_000.0, 1_000_000.0, 10_000_000.0,
    ];

    private int _warpIndex = 1;

    /// <summary>Simulated seconds since <see cref="J2000Epoch"/>.</summary>
    public double ElapsedSeconds { get; private set; }

    /// <summary>How many simulated seconds pass per wall-clock second.</summary>
    public double WarpFactor => WarpSteps[_warpIndex];

    /// <summary>The index into <see cref="WarpSteps"/> currently selected.</summary>
    public int WarpIndex
    {
        get => _warpIndex;
        set => _warpIndex = Math.Clamp(value, 0, WarpSteps.Length - 1);
    }

    /// <summary>Whether simulated time is standing still.</summary>
    public bool IsPaused => WarpFactor == 0.0;

    /// <summary>The simulated instant, as a date.</summary>
    public DateTime Instant => J2000Epoch.AddSeconds(ElapsedSeconds);

    /// <summary>Advances simulated time by one frame of wall-clock time.</summary>
    public void Advance(double realDeltaSeconds) => ElapsedSeconds += realDeltaSeconds * WarpFactor;

    /// <summary>Selects the next faster warp step, if there is one.</summary>
    public void WarpUp() => WarpIndex = _warpIndex + 1;

    /// <summary>Selects the next slower warp step, if there is one.</summary>
    public void WarpDown() => WarpIndex = _warpIndex - 1;

    /// <summary>Puts simulated time back at the epoch, leaving the warp factor alone.</summary>
    public void ResetToEpoch() => ElapsedSeconds = 0.0;

    /// <summary>Jumps simulated time by a fixed amount, which is what a "skip ahead" does.</summary>
    public void Skip(double seconds) => ElapsedSeconds += seconds;
}
