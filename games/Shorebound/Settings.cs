namespace Shorebound;

/// <summary>Everything a player can change about how the game looks and handles.</summary>
/// <remarks>
/// <para>
/// Settings and <see cref="Options" /> are not the same thing and are deliberately separate.
/// Options are how one run was started — a seed, a save path, a fixed camera for a screenshot —
/// and they are forgotten when it ends. Settings are the player's, they outlive the run, and
/// every one of them can be changed while the game is open.
/// </para>
/// <para>
/// That second part is the constraint this type is shaped by: there is no "apply" button and no
/// restart. A value here is read where it is used, every frame, so moving a slider changes the
/// world behind the menu while the slider is still being held.
/// </para>
/// </remarks>
public sealed record Settings
{
    /// <summary>What a player starts with, and what a missing or broken settings file falls back to.</summary>
    public static Settings Default { get; } = new();

    /// <summary>How far chunks are kept loaded, in chunks.</summary>
    /// <remarks>
    /// The one setting that costs memory rather than frame time: a radius of r keeps
    /// <c>(2r+1)² × 8</c> chunks resident, at 64 KiB of block ids each.
    /// </remarks>
    public int ViewRadiusChunks { get; init; } = 8;

    /// <summary>How wide the view is, in degrees across.</summary>
    public float FieldOfViewDegrees { get; init; } = 75f;

    /// <summary>How fast the view turns, on a scale where 50 is the default.</summary>
    /// <remarks>
    /// A plain number rather than the radians per pixel it becomes, because radians per pixel is
    /// a quantity nobody has an opinion about and "a bit faster than before" is.
    /// </remarks>
    public float LookSensitivity { get; init; } = 50f;

    /// <summary>Whether the sun and moon cast shadows.</summary>
    public bool Shadows { get; init; } = true;

    /// <summary>How dark a shadow is, from none at 0 to full at 1.</summary>
    public float ShadowStrength { get; init; } = 0.9f;

    /// <summary>Whether the far edge of the loaded world fades into the sky.</summary>
    /// <remarks>
    /// Worth turning off, and not only for a screenshot: with fog off the edge of what is loaded
    /// is visible, which is the quickest way to see what streaming is actually doing.
    /// </remarks>
    public bool Fog { get; init; } = true;

    /// <summary>How bright everything is drawn, 1 being as the sky lights it.</summary>
    public float Brightness { get; init; } = 1f;

    /// <summary>How large the interface is drawn, 1 being the designed size.</summary>
    public float UiScale { get; init; } = 1f;

    /// <summary>Whether the debug overlay is shown; F3 toggles it too.</summary>
    public bool ShowOverlay { get; init; } = true;

    /// <summary>How long a whole day takes in real seconds; zero stops the sun where it is.</summary>
    public double DayLengthSeconds { get; init; } = DayClock.DefaultDayLengthSeconds;

    /// <summary>How fast the view turns, in radians per pixel of mouse movement.</summary>
    /// <remarks>
    /// The scale is linear and 50 is 0.0022 radians a pixel, which is roughly 400 pixels for a
    /// quarter turn at the default field of view.
    /// </remarks>
    public float RadiansPerPixel => 0.0022f * (LookSensitivity / 50f);

    /// <summary>The field of view in radians, which is what a projection matrix is built from.</summary>
    public float FieldOfViewRadians => float.DegreesToRadians(FieldOfViewDegrees);

    /// <summary>
    /// The same settings with every value forced into the range the interface offers.
    /// </summary>
    /// <remarks>
    /// Applied to whatever is read from disk. A settings file is a text file a person can edit,
    /// so it can say a view radius of four thousand, and the difference between a clamp here and
    /// a crash later is this method.
    /// </remarks>
    public Settings Clamped() =>
        this with
        {
            ViewRadiusChunks = int.Clamp(ViewRadiusChunks, 2, 24),
            FieldOfViewDegrees = float.Clamp(FieldOfViewDegrees, 50f, 110f),
            LookSensitivity = float.Clamp(LookSensitivity, 5f, 200f),
            ShadowStrength = float.Clamp(ShadowStrength, 0f, 1f),
            Brightness = float.Clamp(Brightness, 0.5f, 1.8f),
            UiScale = float.Clamp(UiScale, 0.75f, 2f),
            DayLengthSeconds = double.Clamp(DayLengthSeconds, 0.0, 3600.0),
        };
}
