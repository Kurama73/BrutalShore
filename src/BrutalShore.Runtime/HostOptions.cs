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
}
