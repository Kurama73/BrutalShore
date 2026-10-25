namespace BrutalShore.Ui;

/// <summary>The colours every panel in the game and the engine is drawn from.</summary>
/// <remarks>
/// <para>
/// One palette rather than a colour per call site. An interface made of literal colours drifts
/// the moment there are two screens: the same grey ends up written four ways, and changing it
/// means finding all four. These are the names the rest of the code asks for.
/// </para>
/// <para>
/// The look is an industrial one: a cold near-black steel for everything structural and a single
/// hot amber for everything that is live, acting, selected or dangerous. One accent and no second
/// hue is what makes a dense screen readable at a glance — the eye goes to the amber because
/// nothing else in the frame is warm.
/// </para>
/// <para>
/// There are two greys above the panel and two below it on purpose. A recess and a raised face
/// have to be told apart without a shadow, because a draw list has no blur to make one with; a
/// dark fill under a light hairline reads as cut into the panel, and the reverse reads as sitting
/// on it. Every control here is built out of that one trick.
/// </para>
/// </remarks>
public static class UiPalette
{
    /// <summary>The text of a heading, and of anything in the foreground.</summary>
    public static float4 Text { get; } = Srgb(0xE9EDF1, 1f);

    /// <summary>Text that is present but not the point: a path, a hint, an unselected slot.</summary>
    public static float4 MutedText { get; } = Srgb(0x8D9BA8, 1f);

    /// <summary>The background of a panel.</summary>
    /// <remarks>
    /// Nearly opaque rather than fully. A sliver of the world behind a settings panel is what
    /// says the game is still there and still running; any more than a sliver and small text over
    /// a sunlit field stops being readable.
    /// </remarks>
    public static float4 Panel { get; } = Srgb(0x121619, 0.97f);

    /// <summary>The background of a panel that floats over the world rather than replacing it.</summary>
    public static float4 Overlay { get; } = Srgb(0x121619, 0.72f);

    /// <summary>A control at rest: a button, a slider's trough, a box.</summary>
    public static float4 Control { get; } = Srgb(0x1E262C, 1f);

    /// <summary>A control under the pointer.</summary>
    public static float4 Hovered { get; } = Srgb(0x2A353D, 1f);

    /// <summary>A control being pressed.</summary>
    public static float4 Pressed { get; } = Srgb(0x3A4851, 1f);

    /// <summary>What marks the one thing that is chosen, selected or in hand.</summary>
    public static float4 Accent { get; } = Srgb(0xFA9549, 1f);

    /// <summary>The accent under the pointer.</summary>
    public static float4 AccentHovered { get; } = Srgb(0xFFB474, 1f);

    /// <summary>A hairline between sections.</summary>
    public static float4 Divider { get; } = Srgb(0x333F48, 1f);

    /// <summary>What a destructive or final choice is tinted with.</summary>
    public static float4 Warning { get; } = Srgb(0xE0574B, 1f);

    /// <summary>The strip a panel's name sits in, one step up from the panel itself.</summary>
    public static float4 Header { get; } = Srgb(0x1A2126, 1f);

    /// <summary>The recess a slot or a slider's track is cut into, darker than the panel.</summary>
    public static float4 Recess { get; } = Srgb(0x0C1013, 1f);

    /// <summary>The hairline along the lit edge of a raised face.</summary>
    /// <remarks>
    /// One pixel of this along the top of a control and one of <see cref="Scrim" /> along the
    /// bottom is the whole of the relief here. It is cheap, it survives any background, and it
    /// is what a drawn interface has instead of a shadow.
    /// </remarks>
    public static float4 Edge { get; } = Srgb(0x4E5E6B, 1f);

    /// <summary>What the world is darkened with behind a menu.</summary>
    /// <remarks>
    /// Not black. A scrim in the panel's own near-black blue keeps the picture behind it reading
    /// as the same scene in lower light, where black reads as a sheet laid over it.
    /// </remarks>
    public static float4 Scrim { get; } = Srgb(0x05080A, 1f);

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
