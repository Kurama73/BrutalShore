using Brutal.ImGuiApi;

namespace BrutalShore.Ui;

/// <summary>Drawing straight into the interface's draw list: rectangles, rules, text, scrims.</summary>
/// <remarks>
/// <para>
/// Dear ImGui's own widgets are made for tools, and they look it. They are still the right answer
/// for the debug overlay — a developer wants a dense panel that costs nothing to add a row to —
/// and the wrong one for a title screen, which is the first thing anyone sees and has to look
/// like it was drawn on purpose.
/// </para>
/// <para>
/// So the menus are drawn rather than laid out: a control here is an invisible button for the
/// input and a handful of primitives for the pixels. That is the usual way to build an interface
/// on ImGui that does not look like ImGui, and it costs nothing — the primitives are the same
/// ones every widget is already made of.
/// </para>
/// </remarks>
public static class UiCanvas
{
    /// <summary>The colour ImGui wants, from the colour everything else here is written in.</summary>
    /// <param name="color">A linear colour from <see cref="UiPalette" />.</param>
    /// <returns>The same colour packed the way a draw list takes it.</returns>
    public static ImColor8 Packed(float4 color) => (ImColor8)color;

    /// <summary>A filled rectangle, optionally rounded.</summary>
    /// <param name="minimum">Its top left corner, in screen pixels.</param>
    /// <param name="maximum">Its bottom right corner.</param>
    /// <param name="color">What to fill it with.</param>
    /// <param name="rounding">How round its corners are, in pixels.</param>
    public static void Rect(float2 minimum, float2 maximum, float4 color, float rounding = 0f) =>
        ImGui.GetWindowDrawList().AddRectFilled(minimum, maximum, Packed(color), rounding);

    /// <summary>The outline of a rectangle.</summary>
    /// <param name="minimum">Its top left corner, in screen pixels.</param>
    /// <param name="maximum">Its bottom right corner.</param>
    /// <param name="color">What to draw it in.</param>
    /// <param name="rounding">How round its corners are, in pixels.</param>
    /// <param name="thickness">How thick the line is, in pixels.</param>
    public static void Outline(
        float2 minimum,
        float2 maximum,
        float4 color,
        float rounding = 0f,
        float thickness = 1f
    ) => ImGui.GetWindowDrawList().AddRect(minimum, maximum, Packed(color), rounding, ImDrawFlags.None, thickness);

    /// <summary>A rectangle fading from one colour at the top to another at the bottom.</summary>
    /// <param name="minimum">Its top left corner, in screen pixels.</param>
    /// <param name="maximum">Its bottom right corner.</param>
    /// <param name="top">The colour along its top edge.</param>
    /// <param name="bottom">The colour along its bottom edge.</param>
    public static void VerticalGradient(float2 minimum, float2 maximum, float4 top, float4 bottom)
    {
        var up = ImGui.ColorConvertFloat4ToU32(top);
        var down = ImGui.ColorConvertFloat4ToU32(bottom);
        ImGui.GetWindowDrawList().AddRectFilledMultiColor(minimum, maximum, up, up, down, down);
    }

    /// <summary>A rectangle fading from one colour on the left to another on the right.</summary>
    /// <param name="minimum">Its top left corner, in screen pixels.</param>
    /// <param name="maximum">Its bottom right corner.</param>
    /// <param name="left">The colour along its left edge.</param>
    /// <param name="right">The colour along its right edge.</param>
    public static void HorizontalGradient(float2 minimum, float2 maximum, float4 left, float4 right)
    {
        var near = ImGui.ColorConvertFloat4ToU32(left);
        var far = ImGui.ColorConvertFloat4ToU32(right);
        ImGui.GetWindowDrawList().AddRectFilledMultiColor(minimum, maximum, near, far, far, near);
    }

    /// <summary>A straight line.</summary>
    /// <param name="from">Where it starts, in screen pixels.</param>
    /// <param name="to">Where it ends.</param>
    /// <param name="color">What to draw it in.</param>
    /// <param name="thickness">How thick it is, in pixels.</param>
    public static void Line(float2 from, float2 to, float4 color, float thickness = 1f) =>
        ImGui.GetWindowDrawList().AddLine(from, to, Packed(color), thickness);

    /// <summary>A filled circle.</summary>
    /// <param name="middle">Where its centre is, in screen pixels.</param>
    /// <param name="radius">How large it is, in pixels.</param>
    /// <param name="color">What to fill it with.</param>
    public static void Circle(float2 middle, float radius, float4 color) =>
        ImGui.GetWindowDrawList().AddCircleFilled(middle, radius, Packed(color), 0);

    /// <summary>Text at a size of its own, rather than the one the panel is using.</summary>
    /// <param name="position">Where its top left corner goes, in screen pixels.</param>
    /// <param name="color">What to draw it in.</param>
    /// <param name="text">What it says.</param>
    /// <param name="size">How tall the font is, in pixels.</param>
    public static void Text(float2 position, float4 color, string text, float size) =>
        ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), size, position, Packed(color), text);

    /// <summary>Text with space added between its letters.</summary>
    /// <param name="position">Where its top left corner goes, in screen pixels.</param>
    /// <param name="color">What to draw it in.</param>
    /// <param name="text">What it says.</param>
    /// <param name="size">How tall the font is, in pixels.</param>
    /// <param name="tracking">How many extra pixels go between one letter and the next.</param>
    /// <returns>How wide the result came out, in pixels.</returns>
    /// <remarks>
    /// One draw call per letter, which is fine for the handful of words that want it — a wordmark
    /// and a section's name — and is the only way to get tracking out of a renderer whose text
    /// call takes a whole string. Letter spacing is most of what separates a title from a label.
    /// </remarks>
    public static float TrackedText(float2 position, float4 color, string text, float size, float tracking)
    {
        var drawList = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        var packed = Packed(color);
        var x = position.X;

        foreach (var letter in text)
        {
            var glyph = letter.ToString();
            drawList.AddText(font, size, new float2(x, position.Y), packed, glyph);
            x += Measure(glyph, size).X + tracking;
        }

        return x - position.X - tracking;
    }

    /// <summary>How large a piece of text comes out at a size.</summary>
    /// <param name="text">What it says.</param>
    /// <param name="size">How tall the font is, in pixels.</param>
    /// <returns>Its width and height in pixels.</returns>
    public static float2 Measure(string text, float size)
    {
        ImGui.PushFont(ImGui.GetFont(), size);
        var measured = ImGui.CalcTextSize(text);
        ImGui.PopFont();
        return measured;
    }

    /// <summary>A soft darkening around a panel, so it reads as lifted off what is behind it.</summary>
    /// <param name="minimum">The panel's top left corner, in screen pixels.</param>
    /// <param name="maximum">Its bottom right corner.</param>
    /// <param name="rounding">How round its corners are, in pixels.</param>
    /// <param name="reach">How far the shadow spreads, in pixels.</param>
    /// <remarks>
    /// A handful of progressively larger rounded rectangles behind it, each barely visible. There
    /// is no blur to reach for in a draw list, and stacking a few almost-transparent outlines is
    /// what the effect is anyway — a gradient that nobody can point at.
    /// </remarks>
    public static void PanelShadow(float2 minimum, float2 maximum, float rounding, float reach)
    {
        const int Layers = 6;

        for (var layer = Layers; layer >= 1; layer--)
        {
            var spread = reach * layer / Layers;
            Rect(
                minimum - new float2(spread, spread),
                maximum + new float2(spread, spread),
                UiPalette.Fade(UiPalette.Scrim, 0.07f),
                rounding + spread
            );
        }
    }

    /// <summary>
    /// Darkens the whole viewport and pulls the corners down further, so a panel over the world
    /// has something to sit on.
    /// </summary>
    /// <param name="strength">How dark it gets in the middle, 0 for none.</param>
    /// <remarks>
    /// Four gradients from the edges rather than a texture. A flat wash at the strength a menu
    /// needs makes the world behind it look switched off; a vignette leaves the middle of the
    /// picture almost untouched and still gives every edge of the screen enough contrast for
    /// text to sit on.
    /// </remarks>
    public static void Scrim(float strength)
    {
        if (strength <= 0f)
            return;

        var viewport = ImGui.GetMainViewport();
        var minimum = viewport.Pos;
        var maximum = viewport.Pos + viewport.Size;

        Rect(minimum, maximum, UiPalette.Fade(UiPalette.Scrim, strength * 0.55f));

        var clear = UiPalette.Fade(UiPalette.Scrim, 0f);
        var dark = UiPalette.Fade(UiPalette.Scrim, strength * 0.75f);
        var reach = viewport.Size.Y * 0.42f;

        VerticalGradient(minimum, new float2(maximum.X, minimum.Y + reach), dark, clear);
        VerticalGradient(new float2(minimum.X, maximum.Y - reach), maximum, clear, dark);
        HorizontalGradient(minimum, new float2(minimum.X + reach, maximum.Y), dark, clear);
        HorizontalGradient(new float2(maximum.X - reach, minimum.Y), maximum, clear, dark);
    }
}
