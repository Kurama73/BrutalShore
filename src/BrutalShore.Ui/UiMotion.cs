namespace BrutalShore.Ui;

/// <summary>How far along every animation in an interface is, remembered between frames.</summary>
/// <remarks>
/// <para>
/// An immediate-mode interface keeps no objects, which is most of why it is pleasant to write
/// and all of why nothing in it can move: a button that should light up over a tenth of a second
/// has nowhere to keep how far through that tenth it is. This is that somewhere.
/// </para>
/// <para>
/// Keyed by whatever the caller names the thing — a label is usually already unique on a screen —
/// and eased exponentially rather than linearly, so a value chases its target quickly at first
/// and settles rather than arriving with a corner. Entries for controls that stop being drawn are
/// left where they are; a screen has tens of them, not thousands, and forgetting one would make a
/// menu reopen mid-animation.
/// </para>
/// </remarks>
public sealed class UiMotion
{
    /// <summary>How quickly a value gets most of the way to its target, in seconds.</summary>
    /// <remarks>
    /// A tenth of a second: long enough to read as movement, short enough that a pointer sweeping
    /// across a menu does not leave a trail of controls still lighting up behind it.
    /// </remarks>
    public const float DefaultSeconds = 0.1f;

    private readonly Dictionary<string, float> _values = [];

    /// <summary>Moves a named value towards a target and gives back where it now is.</summary>
    /// <param name="key">What is moving; unique within the screen drawing it.</param>
    /// <param name="target">Where it is heading, usually 0 or 1.</param>
    /// <param name="deltaSeconds">How much time has passed since the last frame.</param>
    /// <param name="seconds">How long it takes to get most of the way there.</param>
    /// <returns>The value for this frame.</returns>
    public float Toward(string key, float target, float deltaSeconds, float seconds = DefaultSeconds)
    {
        var current = _values.GetValueOrDefault(key, target);

        // Framerate independent: the fraction remaining is halved every so often in real time,
        // rather than by a fixed amount per frame, so the same animation takes the same time at
        // thirty frames a second as at three hundred.
        var caught = seconds <= 0f ? 1f : 1f - float.Exp(-deltaSeconds / (seconds / 3f));
        var moved = float.Lerp(current, target, float.Clamp(caught, 0f, 1f));

        _values[key] = moved;
        return moved;
    }

    /// <summary>Forgets every value, so the next frame draws the interface already settled.</summary>
    /// <remarks>
    /// For opening a screen: without it, a menu shown for the second time starts from wherever it
    /// was when it closed, which looks like a glitch rather than like a memory.
    /// </remarks>
    public void Reset() => _values.Clear();

    /// <summary>An ease that starts fast and settles, for anything moving a distance.</summary>
    /// <param name="amount">How far through, from 0 to 1.</param>
    /// <returns>The eased position, from 0 to 1.</returns>
    public static float Ease(float amount)
    {
        var clamped = float.Clamp(amount, 0f, 1f);
        return 1f - ((1f - clamped) * (1f - clamped) * (1f - clamped));
    }
}
