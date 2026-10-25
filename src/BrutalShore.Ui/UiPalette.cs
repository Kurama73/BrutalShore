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
    /// <remarks>
    /// Nearly opaque rather than fully. A sliver of the world behind a settings panel is what
    /// says the game is still there and still running; any more than a sliver and small text over
    /// a sunlit field stops being readable.
    /// </remarks>
    public static float4 Panel { get; } = Srgb(0x11161C, 0.975f);

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

    /// <summary>What the world is darkened with behind a menu.</summary>
    /// <remarks>
    /// Not black. A scrim in the panel's own near-black blue keeps the picture behind it reading
    /// as the same scene in lower light, where black reads as a sheet laid over it.
    /// </remarks>
    public static float4 Scrim { get; } = Srgb(0x070A0E, 1f);

    /// <summary>
    /// A colour written as sRGB bytes — the way a block's colour is written — turned into what
    /// the interface draws with.
    /// </summary>
    /// <param name="red">Its red channel, 0 to 255.</param>
    /// <param name="green">Its green channel.</param>
    /// <param name="blue">Its blue channel.</param>
    /// <param name="alpha">How opaque it is, 0 to 1.</param>
    /// <returns>The same colour in linear light.</returns>
    /// <remarks>
    /// For showing something from outside the palette: a block's own colour in a hotbar slot, a
    /// biome's on a map. Anything the interface itself is made of belongs in the palette instead.
    /// </remarks>
    public static float4 FromSrgb(byte red, byte green, byte blue, float alpha = 1f) =>
        new(ToLinear(red / 255f), ToLinear(green / 255f), ToLinear(blue / 255f), alpha);

    /// <summary>The same colour at a different opacity, for a panel that fades.</summary>
    /// <param name="color">The colour to fade.</param>
    /// <param name="opacity">How much of it remains, 0 to 1.</param>
    /// <returns>The colour with its alpha scaled.</returns>
    public static float4 Fade(float4 color, float opacity) =>
        new(color.X, color.Y, color.Z, color.W * opacity);

    /// <summary>
    /// A colour written the way a person writes one — as a hex triple in sRGB — turned into the
    /// linear light the interface is actually blended and written in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The conversion is not optional and it is not cosmetic. The swapchain is an sRGB format, so
    /// the hardware encodes on write and everything handed to it is read as linear. A colour
    /// passed through unchanged therefore arrives about twice as bright as it was typed: a panel
    /// meant to be nearly black comes out mid-grey, which is exactly what happened the first time
    /// this interface was drawn.
    /// </para>
    /// <para>
    /// Alpha is not converted. It is coverage rather than light, and encoding it would make a
    /// panel at 94% opacity blend as though it were at 98%.
    /// </para>
    /// </remarks>
    private static float4 Srgb(uint hex, float alpha) =>
        new(
            ToLinear(((hex >> 16) & 0xFF) / 255f),
            ToLinear(((hex >> 8) & 0xFF) / 255f),
            ToLinear((hex & 0xFF) / 255f),
            alpha
        );

    // The sRGB transfer function, as the specification writes it: a short linear segment near
    // black and a power curve above it.
    private static float ToLinear(float encoded) =>
        encoded <= 0.04045f ? encoded / 12.92f : float.Pow((encoded + 0.055f) / 1.055f, 2.4f);
}
