namespace BrutalShore.Gpu;

/// <summary>An input source that plays a script instead of reading a keyboard.</summary>
/// <remarks>
/// <para>
/// The second implementation of <see cref="IInputSource" />, and the one the seam exists for. A
/// game that reads its input through the interface can be run from top to bottom with nobody at
/// the machine — which is how the things that only break when the whole program runs together
/// get found, and those are the ones no unit test reaches.
/// </para>
/// <para>
/// It wraps the real input rather than replacing it, so a scripted session is still a session: a
/// person can take over, the window still closes, and anything the script does not touch behaves
/// as it always did. The two are merged rather than switched between — a key the script is
/// holding and a key a person is holding are both held.
/// </para>
/// <para>
/// Steps are applied by wall-clock position in the script, not by frame, and every step between
/// the last frame and this one is applied. That is what makes a script mean the same thing at
/// four frames a second as at two hundred, which matters because the machine a problem happens on
/// is rarely the one it was recorded on.
/// </para>
/// </remarks>
public sealed class ScriptedInput : IInputSource
{
    private readonly IInputSource _real;
    private readonly SessionScript _script;
    private readonly HashSet<GlfwKey> _keysDown = [];
    private readonly HashSet<GlfwKey> _keysPressed = [];
    private readonly HashSet<GlfwMouseButton> _buttonsDown = [];
    private readonly HashSet<GlfwMouseButton> _buttonsPressed = [];
    private readonly Action<string> _log;

    private int _next;
    private double _elapsedSeconds;
    private double2 _look;
    private double _scroll;

    /// <summary>Starts a session at the beginning of a script.</summary>
    /// <param name="real">The window underneath, whose input is merged with the script's.</param>
    /// <param name="script">What to do and when.</param>
    /// <param name="log">Where a mark goes; the console, normally.</param>
    public ScriptedInput(IInputSource real, SessionScript script, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(real);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(log);

        _real = real;
        _script = script;
        _log = log;
    }

    /// <summary>Whether every step has been applied.</summary>
    public bool IsFinished => _next >= _script.Steps.Count;

    /// <summary>How far into the script the session has got, in seconds.</summary>
    public double ElapsedSeconds => _elapsedSeconds;

    /// <summary>Applies everything the script says to do by now.</summary>
    /// <param name="seconds">How long this frame is.</param>
    /// <remarks>
    /// Called once a frame, before the game reads anything. What was pressed <em>this</em> frame
    /// is cleared first, so that a tap is seen by exactly one frame however long that frame is —
    /// the same contract the window keeps.
    /// </remarks>
    public void Advance(double seconds)
    {
        _keysPressed.Clear();
        _buttonsPressed.Clear();
        _look = double2.Zero;
        _scroll = 0.0;

        _elapsedSeconds += seconds;

        while (_next < _script.Steps.Count && _script.Steps[_next].AtSeconds <= _elapsedSeconds)
            Apply(_script.Steps[_next++]);
    }

    /// <inheritdoc />
    public double2 CursorDelta => _real.CursorDelta + _look;

    /// <inheritdoc />
    public double ScrollDelta => _real.ScrollDelta + _scroll;

    /// <inheritdoc />
    public bool IsMouseCaptured => _real.IsMouseCaptured;

    /// <inheritdoc />
    public bool IsKeyDown(GlfwKey key) => _keysDown.Contains(key) || _real.IsKeyDown(key);

    /// <inheritdoc />
    public bool WasKeyPressed(GlfwKey key) => _keysPressed.Contains(key) || _real.WasKeyPressed(key);

    /// <inheritdoc />
    public bool IsMouseButtonDown(GlfwMouseButton button) =>
        _buttonsDown.Contains(button) || _real.IsMouseButtonDown(button);

    /// <inheritdoc />
    public bool WasMouseButtonPressed(GlfwMouseButton button) =>
        _buttonsPressed.Contains(button) || _real.WasMouseButtonPressed(button);

    /// <inheritdoc />
    public void SetMouseCaptured(bool captured) => _real.SetMouseCaptured(captured);

    private void Apply(SessionStep step)
    {
        switch (step.Kind)
        {
            case SessionAction.KeyDown:
                _keysDown.Add(step.Key);
                _keysPressed.Add(step.Key);
                break;

            case SessionAction.KeyUp:
                _keysDown.Remove(step.Key);
                break;

            case SessionAction.Tap:
                // Pressed but not held: a tap is a thing one frame sees and the next does not.
                _keysPressed.Add(step.Key);
                break;

            case SessionAction.ButtonDown:
                _buttonsDown.Add(step.Button);
                _buttonsPressed.Add(step.Button);
                break;

            case SessionAction.ButtonUp:
                _buttonsDown.Remove(step.Button);
                break;

            case SessionAction.Click:
                _buttonsPressed.Add(step.Button);
                break;

            case SessionAction.Look:
                _look += step.Amount;
                break;

            case SessionAction.Scroll:
                _scroll += step.Amount.Y;
                break;

            case SessionAction.Mark:
                _log($"[{_elapsedSeconds,6:F1}s] {step.Text}");
                break;
        }
    }
}
