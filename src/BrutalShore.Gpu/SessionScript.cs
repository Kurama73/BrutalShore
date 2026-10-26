namespace BrutalShore.Gpu;

/// <summary>One thing a scripted session does at a moment.</summary>
/// <remarks>
/// Everything is stated against a clock rather than against frames, so a script means the same
/// thing on a machine running at four frames a second as on one running at two hundred. That
/// matters more than it sounds: the thing a script is for is reproducing a problem, and a script
/// whose meaning depended on the frame rate would only reproduce it on the machine it was
/// recorded on.
/// </remarks>
/// <param name="AtSeconds">How long into the session it happens.</param>
/// <param name="Kind">What happens.</param>
/// <param name="Key">The key, for a key action.</param>
/// <param name="Button">The button, for a mouse action.</param>
/// <param name="Amount">How far to move or turn, for a look or a scroll.</param>
/// <param name="Text">A note, for a mark.</param>
public readonly record struct SessionStep(
    double AtSeconds,
    SessionAction Kind,
    GlfwKey Key = default,
    GlfwMouseButton Button = default,
    double2 Amount = default,
    string Text = ""
);

/// <summary>What a step of a scripted session does.</summary>
public enum SessionAction
{
    /// <summary>Holds a key down from this moment.</summary>
    KeyDown,

    /// <summary>Lets a key up.</summary>
    KeyUp,

    /// <summary>Presses and releases a key within one frame.</summary>
    Tap,

    /// <summary>Holds a mouse button down from this moment.</summary>
    ButtonDown,

    /// <summary>Lets a mouse button up.</summary>
    ButtonUp,

    /// <summary>Presses and releases a mouse button within one frame.</summary>
    Click,

    /// <summary>Turns the view by some number of pixels of mouse movement.</summary>
    Look,

    /// <summary>Turns the wheel.</summary>
    Scroll,

    /// <summary>Writes a line to the log, so a run can be read against its script.</summary>
    Mark,
}

/// <summary>A whole session written down: what to do, and when.</summary>
/// <remarks>
/// <para>
/// Built in order with a fluent cursor that carries a clock, because the thing anyone writing one
/// of these wants to say is "then, after a second, do this" — and a list of absolute timestamps
/// is a list that has to be renumbered every time a step is inserted in the middle.
/// </para>
/// <para>
/// There is no file format. A script is code, in the project that needs it, which means it is
/// type-checked, refactored with the rest of the program and reviewed like anything else. A text
/// format would buy editing a script without rebuilding, and cost a parser, a schema, errors to
/// report and a second place where key names are spelled.
/// </para>
/// </remarks>
public sealed class SessionScript
{
    private readonly List<SessionStep> _steps = [];

    /// <summary>Where the cursor currently is, in seconds from the start.</summary>
    public double AtSeconds { get; private set; }

    /// <summary>How long the whole session lasts.</summary>
    public double LengthSeconds => _steps.Count == 0 ? 0.0 : _steps[^1].AtSeconds;

    /// <summary>Everything it does, in order.</summary>
    public IReadOnlyList<SessionStep> Steps => _steps;

    /// <summary>Moves the cursor forward.</summary>
    /// <param name="seconds">How long to wait before the next step.</param>
    /// <returns>This script.</returns>
    public SessionScript Wait(double seconds)
    {
        AtSeconds += double.Max(0.0, seconds);
        return this;
    }

    /// <summary>Presses and releases a key.</summary>
    /// <param name="key">Which key.</param>
    /// <returns>This script.</returns>
    public SessionScript Tap(GlfwKey key) => Add(new SessionStep(AtSeconds, SessionAction.Tap, Key: key));

    /// <summary>Holds a key for a while, then lets it up.</summary>
    /// <param name="key">Which key.</param>
    /// <param name="seconds">How long to hold it.</param>
    /// <returns>This script, with the cursor moved past the hold.</returns>
    public SessionScript Hold(GlfwKey key, double seconds)
    {
        Add(new SessionStep(AtSeconds, SessionAction.KeyDown, Key: key));
        Wait(seconds);
        return Add(new SessionStep(AtSeconds, SessionAction.KeyUp, Key: key));
    }

    /// <summary>Presses and releases a mouse button.</summary>
    /// <param name="button">Which button.</param>
    /// <returns>This script.</returns>
    public SessionScript Click(GlfwMouseButton button) =>
        Add(new SessionStep(AtSeconds, SessionAction.Click, Button: button));

    /// <summary>Holds a mouse button for a while, then lets it up.</summary>
    /// <param name="button">Which button.</param>
    /// <param name="seconds">How long to hold it.</param>
    /// <returns>This script, with the cursor moved past the hold.</returns>
    public SessionScript HoldButton(GlfwMouseButton button, double seconds)
    {
        Add(new SessionStep(AtSeconds, SessionAction.ButtonDown, Button: button));
        Wait(seconds);
        return Add(new SessionStep(AtSeconds, SessionAction.ButtonUp, Button: button));
    }

    /// <summary>Turns the view, as moving the mouse does.</summary>
    /// <param name="pixels">How far, in the pixels a mouse would have moved.</param>
    /// <returns>This script.</returns>
    public SessionScript Look(double2 pixels) =>
        Add(new SessionStep(AtSeconds, SessionAction.Look, Amount: pixels));

    /// <summary>Turns the wheel.</summary>
    /// <param name="notches">How far; positive is away from the hand.</param>
    /// <returns>This script.</returns>
    public SessionScript Scroll(double notches) =>
        Add(new SessionStep(AtSeconds, SessionAction.Scroll, Amount: new double2(0.0, notches)));

    /// <summary>Writes a line to the log when the session reaches this point.</summary>
    /// <param name="text">What to write.</param>
    /// <returns>This script.</returns>
    /// <remarks>
    /// What makes a run readable against the script that produced it. A log that says only what
    /// the game did is a log nobody can line up with what it was told to do.
    /// </remarks>
    public SessionScript Mark(string text) =>
        Add(new SessionStep(AtSeconds, SessionAction.Mark, Text: text));

    private SessionScript Add(SessionStep step)
    {
        _steps.Add(step);
        return this;
    }
}
