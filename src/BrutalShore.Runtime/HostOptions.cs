using BrutalShore.Gpu;

namespace BrutalShore.Runtime;

/// <summary>How the host should start: the things true of any game, not of one.</summary>
/// <remarks>
/// A game's own settings — its seed, its view distance, where the player starts — are not here and
/// must not be. The split is the test of whether the engine knows too much about the game: if a
/// field would be meaningless for a different game, it belongs on the other side.
/// </remarks>
public sealed record HostOptions
{
    /// <summary>The window title.</summary>
    public string Title { get; init; } = "BrutalShore";

    /// <summary>The window's size in pixels when it opens.</summary>
    public int2 WindowSize { get; init; } = new(1600, 900);

    /// <summary>
    /// Whether to turn on the Vulkan validation layers, which report API misuse at a large cost in
    /// frame time. Off by default so the default run is representative.
    /// </summary>
    public bool EnableValidation { get; init; }

    /// <summary>How many frames to run before closing, or zero to run until the window is closed.</summary>
    public int FrameLimit { get; init; }

    /// <summary>
    /// Where to save a PNG of the last frame, or <see langword="null" /> for no screenshot.
    /// </summary>
    /// <remarks>
    /// Together with <see cref="FrameLimit" /> this is what makes a run self-verifying: a fixed
    /// number of frames from a fixed starting state leaves an image that can be compared against
    /// the one before the change.
    /// </remarks>
    public string? ScreenshotPath { get; init; }

    /// <summary>Whether the debug overlay starts visible.</summary>
    /// <remarks>
    /// F3 still toggles it either way. Starting hidden is for making a picture of the world
    /// rather than of the numbers over it, which a fixed-frame run has no way to ask for
    /// otherwise — it never gets to a keyboard.
    /// </remarks>
    public bool ShowOverlay { get; init; } = true;

    /// <summary>Where to write a table of what the run cost, or nothing.</summary>
    /// <remarks>
    /// The other half of a scripted session. The script makes a run repeatable and the report
    /// makes it measurable, and the two together turn a playtest into something that can be put
    /// beside the same playtest from before a change.
    /// </remarks>
    public string? ProfilePath { get; init; }

    /// <summary>A session to play instead of waiting for a person, or nothing.</summary>
    /// <remarks>
    /// What makes an automated playtest possible: the host feeds the script to the game as
    /// though someone were typing it. Combined with a frame limit and a screenshot, one command
    /// plays a whole session and leaves a picture of where it got to.
    /// </remarks>
    public SessionScript? Script { get; init; }

    /// <summary>How large the interface is drawn, 1 being the designed size.</summary>
    /// <remarks>
    /// Here rather than in a game because the host is what creates the interface's context, and
    /// a theme is set on a context rather than passed to a panel. A game that lets a player
    /// change it calls <see cref="BrutalShore.Ui.UiTheme.Apply" /> again with the new value.
    /// </remarks>
    public float UiScale { get; init; } = 1f;
}
