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
/// <para>
/// The shape vocabulary is four things and they are deliberately few: a chamfered box, a
/// hairline, a corner bracket and a band of hazard stripes. Everything on every screen is built
/// out of those, which is what makes two screens drawn months apart look like the same machine.
/// A rounded rectangle is not among them — a cut corner is a machined one and a rounded corner
/// is a moulded one, and this is meant to look machined.
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

    /// <summary>A box with its corners cut off at forty-five degrees.</summary>
    /// <param name="minimum">Its top left corner, in screen pixels.</param>
    /// <param name="maximum">Its bottom right corner.</param>
    /// <param name="color">What to fill it with.</param>
    /// <param name="cut">How far back each corner is cut, in pixels.</param>
    /// <remarks>
    /// The shape everything here is made of. The cut is clamped to half the shorter side, so a
    /// box asked for a cut larger than it is comes out a diamond rather than inside out.
    /// </remarks>
    public static void Chamfer(float2 minimum, float2 maximum, float4 color, float cut)
    {
        Span<float2> corners = stackalloc float2[8];
        Corners(minimum, maximum, cut, corners);
        ImGui.GetWindowDrawList().AddConvexPolyFilled(corners, Packed(color));
    }

    /// <summary>The outline of a box with its corners cut off.</summary>
    /// <param name="minimum">Its top left corner, in screen pixels.</param>
    /// <param name="maximum">Its bottom right corner.</param>
    /// <param name="color">What to draw it in.</param>
    /// <param name="cut">How far back each corner is cut, in pixels.</param>
    /// <param name="thickness">How thick the line is, in pixels.</param>
    public static void ChamferOutline(
        float2 minimum,
        float2 maximum,
        float4 color,
        float cut,
        float thickness = 1f
    )
    {
        Span<float2> corners = stackalloc float2[8];
        Corners(minimum, maximum, cut, corners);
        ImGui.GetWindowDrawList().AddPolyline(corners, Packed(color), ImDrawFlags.Closed, thickness);
    }

    /// <summary>Short brackets at the four corners of a box, rather than a line all the way round.</summary>
    /// <param name="minimum">Its top left corner, in screen pixels.</param>
    /// <param name="maximum">Its bottom right corner.</param>
    /// <param name="color">What to draw them in.</param>
    /// <param name="reach">How far along each edge a bracket runs, in pixels.</param>
    /// <param name="thickness">How thick the lines are, in pixels.</param>
    /// <remarks>
    /// What marks something out without boxing it in. A full outline around a selected thing
    /// competes with the outline around the thing next to it; four corners say the same and leave
    /// the edges quiet.
    /// </remarks>
    public static void CornerTicks(
        float2 minimum,
        float2 maximum,
        float4 color,
        float reach,
        float thickness = 2f
    )
    {
        Line(minimum, new float2(minimum.X + reach, minimum.Y), color, thickness);
        Line(minimum, new float2(minimum.X, minimum.Y + reach), color, thickness);

        Line(new float2(maximum.X - reach, minimum.Y), new float2(maximum.X, minimum.Y), color, thickness);
        Line(new float2(maximum.X, minimum.Y), new float2(maximum.X, minimum.Y + reach), color, thickness);

        Line(new float2(minimum.X, maximum.Y - reach), new float2(minimum.X, maximum.Y), color, thickness);
        Line(new float2(minimum.X, maximum.Y), new float2(minimum.X + reach, maximum.Y), color, thickness);

        Line(new float2(maximum.X - reach, maximum.Y), maximum, color, thickness);
        Line(new float2(maximum.X, maximum.Y - reach), maximum, color, thickness);
    }

    /// <summary>A band of diagonal stripes, the way a machine marks the edge of something.</summary>
    /// <param name="minimum">The band's top left corner, in screen pixels.</param>
    /// <param name="maximum">Its bottom right corner.</param>
    /// <param name="color">What to draw the stripes in; the gaps are left alone.</param>
    /// <param name="pitch">How far apart two stripes start, in pixels.</param>
    /// <remarks>
    /// Clipped to the band rather than fitted to it, because stripes that stop short of a corner
    /// to stay inside it are stripes that are visibly avoiding the corner. The draw list clips
    /// for free, so the honest version is also the cheap one.
    /// </remarks>
    public static void HazardStripes(float2 minimum, float2 maximum, float4 color, float pitch)
    {
        var drawList = ImGui.GetWindowDrawList();
        var height = maximum.Y - minimum.Y;
        if (height <= 0f || pitch <= 0f)
            return;

        drawList.PushClipRect(minimum, maximum, true);

        var packed = Packed(color);
        var thickness = pitch * 0.5f;

        // Started a band's height to the left of the corner so that the first stripe to cross the
        // top left corner is a whole one.
        for (var x = minimum.X - height; x < maximum.X; x += pitch)
        {
            drawList.AddLine(
                new float2(x, maximum.Y),
                new float2(x + height, minimum.Y),
                packed,
                thickness
            );
        }

        drawList.PopClipRect();
    }

    /// <summary>The eight corners of a chamfered box, clockwise from the top left cut.</summary>
    private static void Corners(float2 minimum, float2 maximum, float cut, Span<float2> into)
    {
        var limit = float.Min(maximum.X - minimum.X, maximum.Y - minimum.Y) * 0.5f;
        var back = float.Clamp(cut, 0f, float.Max(limit, 0f));

        into[0] = new float2(minimum.X + back, minimum.Y);
        into[1] = new float2(maximum.X - back, minimum.Y);
        into[2] = new float2(maximum.X, minimum.Y + back);
        into[3] = new float2(maximum.X, maximum.Y - back);
        into[4] = new float2(maximum.X - back, maximum.Y);
        into[5] = new float2(minimum.X + back, maximum.Y);
        into[6] = new float2(minimum.X, maximum.Y - back);
        into[7] = new float2(minimum.X, minimum.Y + back);
    }

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
    /// <param name="cut">How far its corners are cut back, in pixels.</param>
    /// <param name="reach">How far the shadow spreads, in pixels.</param>
    /// <remarks>
    /// A handful of progressively larger chamfered boxes behind it, each barely visible. There is
    /// no blur to reach for in a draw list, and stacking a few almost-transparent copies is what
    /// the effect is anyway — a gradient that nobody can point at.
    /// </remarks>
    public static void PanelShadow(float2 minimum, float2 maximum, float cut, float reach)
    {
        const int Layers = 6;

        for (var layer = Layers; layer >= 1; layer--)
        {
            var spread = reach * layer / Layers;
            Chamfer(
                minimum - new float2(spread, spread),
                maximum + new float2(spread, spread),
                UiPalette.Fade(UiPalette.Scrim, 0.07f),
                cut + spread
            );
        }
    }

    /// <summary>The frame every panel in the game is drawn in: shadow, face, edge and brackets.</summary>
    /// <param name="minimum">Its top left corner, in screen pixels.</param>
    /// <param name="maximum">Its bottom right corner.</param>
    /// <param name="cut">How far its corners are cut back, in pixels.</param>
    /// <param name="scale">The interface scale, which the hairlines and brackets are drawn at.</param>
    /// <remarks>
    /// Here rather than repeated in each screen because the thing that makes a set of screens
    /// look like one program is that their edges are identical. A screen that wants to differ
    /// does so inside the frame.
    /// </remarks>
    public static void Panel(float2 minimum, float2 maximum, float cut, float scale)
    {
        PanelShadow(minimum, maximum, cut, 26f * scale);
        Chamfer(minimum, maximum, UiPalette.Panel, cut);
        ChamferOutline(minimum, maximum, UiPalette.Divider, cut, scale);

        // Brackets over the outline rather than instead of it: the outline closes the shape and
        // the brackets say which shape is the one being looked at.
        CornerTicks(minimum, maximum, UiPalette.Fade(UiPalette.Accent, 0.85f), 16f * scale, 2f * scale);
    }

    /// <summary>A panel's name on a strip of its own, with a hazard band under it.</summary>
    /// <param name="minimum">The panel's top left corner, in screen pixels.</param>
    /// <param name="width">How wide the panel is, in pixels.</param>
    /// <param name="text">What the panel is; drawn in upper case.</param>
    /// <param name="scale">The interface scale.</param>
    /// <returns>How far down the panel the strip ends, in screen pixels.</returns>
    public static float PanelHeading(float2 minimum, float width, string text, float scale)
    {
        var size = ImGui.GetFontSize() * 1.02f;
        var height = size + (22f * scale);
        var maximum = new float2(minimum.X + width, minimum.Y + height);

        Rect(minimum + new float2(0f, 1f * scale), maximum, UiPalette.Header);

        // A short amber block at the left end, which is where the eye lands first and so is where
        // the one coloured thing on the strip belongs.
        Rect(
            new float2(minimum.X, minimum.Y + (1f * scale)),
            new float2(minimum.X + (5f * scale), maximum.Y),
            UiPalette.Accent
        );

        TrackedText(
            new float2(minimum.X + (18f * scale), minimum.Y + ((height - size) * 0.5f)),
            UiPalette.Text,
            text.ToUpperInvariant(),
            size,
            3f * scale
        );

        var bandTop = maximum.Y;
        var band = 8f * scale;
        Rect(new float2(minimum.X, bandTop), new float2(maximum.X, bandTop + band), UiPalette.Recess);
        HazardStripes(
            new float2(minimum.X, bandTop),
            new float2(maximum.X, bandTop + band),
            UiPalette.Fade(UiPalette.Accent, 0.62f),
            18f * scale
        );

        return bandTop + band;
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
