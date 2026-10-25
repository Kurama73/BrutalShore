namespace BrutalShore.Ui;

/// <summary>The colours every panel in the game and the engine is drawn from.</summary>
/// <remarks>
/// <para>
/// One palette rather than a colour per call site. An interface made of literal colours drifts
/// the moment there are two screens: the same grey ends up written four ways, and changing it
/// means finding all four. These are the names the rest of the code asks for.
/// </para>
/// <para>
/// The hues come from the world the game draws — a cold blue-grey for everything structural, the
/// warm sand of a shoreline for whatever is active. That is why they are here rather than in a
/// general-purpose widget library: an interface that looks like it belongs to its game is worth
/// a file of constants.
/// </para>
/// </remarks>
public static class UiPalette
{
    /// <summary>The text of a heading, and of anything in the foreground.</summary>
    public static float4 Text { get; } = Srgb(0xE8EDF2, 1f);

    /// <summary>Text that is present but not the point: a path, a hint, an unselected slot.</summary>
    public static float4 MutedText { get; } = Srgb(0x8A97A6, 1f);

    /// <summary>The background of a panel.</summary>
    public static float4 Panel { get; } = Srgb(0x11161C, 0.94f);

    /// <summary>The background of a panel that floats over the world rather than replacing it.</summary>
    public static float4 Overlay { get; } = Srgb(0x11161C, 0.72f);

    /// <summary>A control at rest: a button, a slider's trough, a box.</summary>
    public static float4 Control { get; } = Srgb(0x1E2832, 1f);

    /// <summary>A control under the pointer.</summary>
    public static float4 Hovered { get; } = Srgb(0x2B3A49, 1f);

    /// <summary>A control being pressed.</summary>
    public static float4 Pressed { get; } = Srgb(0x3A4D60, 1f);

    /// <summary>What marks the one thing that is chosen, selected or in hand.</summary>
    public static float4 Accent { get; } = Srgb(0xE0B472, 1f);

    /// <summary>The accent under the pointer.</summary>
    public static float4 AccentHovered { get; } = Srgb(0xF0C98E, 1f);

    /// <summary>A hairline between sections.</summary>
    public static float4 Divider { get; } = Srgb(0x2A3540, 1f);

    /// <summary>What a destructive or final choice is tinted with.</summary>
    public static float4 Warning { get; } = Srgb(0xD98468, 1f);

    /// <summary>The same colour at a different opacity, for a panel that fades.</summary>
    /// <param name="color">The colour to fade.</param>
    /// <param name="opacity">How much of it remains, 0 to 1.</param>
    /// <returns>The colour with its alpha scaled.</returns>
    public static float4 Fade(float4 color, float opacity) =>
        new(color.X, color.Y, color.Z, color.W * opacity);

    /// <summary>
    /// A colour written the way a person writes one — as a hex triple in sRGB — turned into the
    /// linear-ish floats ImGui blends with.
    /// </summary>
    /// <remarks>
    /// Dear ImGui blends in whatever space it is handed, and the swapchain here is sRGB, so the
    /// bytes go through unchanged: a divide by 255 and nothing else. Stated rather than assumed,
    /// because the day the swapchain stops being sRGB this is the line that has to change.
    /// </remarks>
    private static float4 Srgb(uint hex, float alpha) =>
        new(
            ((hex >> 16) & 0xFF) / 255f,
            ((hex >> 8) & 0xFF) / 255f,
            (hex & 0xFF) / 255f,
            alpha
        );
}
