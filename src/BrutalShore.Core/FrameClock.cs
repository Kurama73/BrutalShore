namespace BrutalShore.Core;

/// <summary>
/// Turns the wall clock into a fixed simulation step plus a variable render frame.
/// </summary>
/// <remarks>
/// <para>
/// The split exists because the two want different things. Simulation wants a step that never
/// changes, so that the same inputs produce the same world whatever the frame rate; rendering wants
/// every frame it can get. So real elapsed time accumulates here, and whole steps are handed out
/// while the remainder stays for the next frame.
/// </para>
/// <para>
/// <see cref="MaxStepsPerFrame"/> is the part worth understanding. If a frame takes longer than the
/// steps it owes — a stall, a breakpoint, a chunk upload that blocked — then catching up fully
/// would take longer still, owe even more steps next frame, and never recover. Capping the
/// catch-up lets simulated time fall behind the wall clock instead, which is the failure everyone
/// prefers.
/// </para>
/// </remarks>
public sealed class FrameClock
{
    /// <summary>Simulation steps per second.</summary>
    public const int StepsPerSecond = 60;

    /// <summary>The length of one simulation step, in seconds.</summary>
    public const double StepSeconds = 1.0 / StepsPerSecond;

    /// <summary>
    /// The most steps one frame may run before simulated time is allowed to fall behind.
    /// </summary>
    private const int MaxStepsPerFrame = 8;

    private double _accumulator;
    private long _lastTimestamp;

    /// <summary>Seconds of wall-clock time the last frame took.</summary>
    public double FrameSeconds { get; private set; }

    /// <summary>Total frames rendered since the clock started.</summary>
    public long FrameCount { get; private set; }

    /// <summary>Total simulation steps run since the clock started.</summary>
    public long StepCount { get; private set; }

    /// <summary>Frames per second, smoothed over roughly the last half second.</summary>
    public double FramesPerSecond { get; private set; }

    /// <summary>
    /// Begins a frame: measures how long the last one took and returns how many simulation steps
    /// this frame owes.
    /// </summary>
    public int BeginFrame()
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_lastTimestamp == 0)
            _lastTimestamp = now;

        FrameSeconds = (now - _lastTimestamp) / (double)System.Diagnostics.Stopwatch.Frequency;
        _lastTimestamp = now;
        FrameCount++;

        // An exponential average rather than a windowed one: it needs one number of state, and the
        // 0.1 weight settles in about half a second at 60 Hz, which is slow enough to read off a
        // debug overlay and fast enough to show a stall.
        if (FrameSeconds > 0.0)
            FramesPerSecond = FramesPerSecond == 0.0
                ? 1.0 / FrameSeconds
                : FramesPerSecond * 0.9 + 0.1 / FrameSeconds;

        _accumulator += FrameSeconds;

        var steps = 0;
        while (_accumulator >= StepSeconds && steps < MaxStepsPerFrame)
        {
            _accumulator -= StepSeconds;
            steps++;
        }

        // Catch-up was capped, so the backlog is dropped rather than carried: keeping it would
        // guarantee the next frame is late too.
        if (steps == MaxStepsPerFrame)
            _accumulator = 0.0;

        StepCount += steps;
        return steps;
    }

    /// <summary>
    /// How far the current frame sits between the last completed step and the next one, in
    /// <c>[0, 1)</c> — what interpolated rendering needs so that motion is smooth between steps.
    /// </summary>
    public float StepFraction => (float)(_accumulator / StepSeconds);
}
