using Brutal.ImGuiApi;

namespace BrutalShore.Ui;

/// <summary>The controls a menu is built from, drawn rather than laid out.</summary>
/// <remarks>
/// <para>
/// Each one is the same two halves: an invisible button of the right size, which is what gives
/// hit testing, focus and the cursor advancing for free, and a few primitives drawn over the
/// rectangle it claimed. Nothing here fights Dear ImGui — it uses the half of it that is good at
/// input and replaces the half that decides what a button looks like.
/// </para>
/// <para>
/// Everything moves. A control's hover is a value chased over about a tenth of a second rather
/// than a flag, because the difference between an interface that feels made and one that feels
/// assembled is almost entirely in whether anything eases.
/// </para>
/// </remarks>
public sealed class UiControls
{
    /// <summary>How tall a menu entry is before the interface scale, in pixels.</summary>
    public const float EntryHeight = 44f;

    /// <summary>How tall a settings row is before the interface scale, in pixels.</summary>
    public const float RowHeight = 34f;

    private readonly UiMotion _motion = new();

    /// <summary>Forgets every animation, so the next frame draws the screen already settled.</summary>
    public void Reset() => _motion.Reset();

    /// <summary>
    /// Opens a column of controls at a point on the screen, which the ones after it flow down.
    /// </summary>
    /// <param name="name">What to call it; unique within the panel.</param>
    /// <param name="position">Where its top left corner goes, in screen pixels.</param>
    /// <param name="size">How much room it has, in pixels.</param>
    /// <remarks>
    /// A child window, because that is the only thing that moves where Dear ImGui thinks the left
    /// margin is. Setting the cursor before a control moves that one control; the next one goes
    /// back to the window's own margin, which on a panel drawn at absolute positions is the far
    /// left of the screen.
    /// </remarks>
    public static void BeginColumn(string name, float2 position, float2 size)
    {
        ImGui.SetCursorScreenPos(position);
        ImGui.BeginChild(name, size, ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar);
    }

    /// <summary>Closes the column <see cref="BeginColumn" /> opened.</summary>
    public static void EndColumn() => ImGui.EndChild();

    /// <summary>
    /// One line of a menu: a label that lights and slides as the pointer crosses it, with a bar
    /// growing down its left edge.
    /// </summary>
    /// <param name="label">What it says.</param>
    /// <param name="width">How wide the entry is, in pixels.</param>
    /// <param name="emphasis">
    /// Whether this is the obvious thing to do on the screen, which tints it with the accent even
    /// before it is hovered.
    /// </param>
    /// <returns>Whether it was clicked.</returns>
    public bool Entry(string label, float width, bool emphasis = false)
    {
        var scale = ImGui.GetStyle().FontScaleMain;
        var height = EntryHeight * scale;
        var origin = ImGui.GetCursorScreenPos();

        ImGui.InvisibleButton(label, new float2(width, height));
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();
        var pressed = ImGui.IsItemClicked();

        var lit = _motion.Toward(label, hovered || held ? 1f : 0f, ImGui.GetIO().DeltaTime);
        var eased = UiMotion.Ease(lit);

        var minimum = origin;
        var maximum = origin + new float2(width, height);

        // The wash fades in from the left rather than appearing all at once, so the bar reads as
        // the source of the highlight instead of as a separate thing that happened at the edge.
        var wash = UiPalette.Fade(emphasis ? UiPalette.Accent : UiPalette.Text, 0.1f * eased);
        UiCanvas.HorizontalGradient(minimum, maximum, wash, UiPalette.Fade(wash, 0f));

        var barHeight = height * (0.3f + (0.7f * eased));
        var barMiddle = origin.Y + (height * 0.5f);
        UiCanvas.Rect(
            new float2(minimum.X, barMiddle - (barHeight * 0.5f)),
            new float2(minimum.X + (3f * scale), barMiddle + (barHeight * 0.5f)),
            emphasis ? UiPalette.Accent : UiPalette.Fade(UiPalette.Text, 0.25f + (0.75f * eased))
        );

        var textColor = emphasis
            ? UiPalette.Accent
            : LerpColor(UiPalette.MutedText, UiPalette.Text, eased);

        var size = ImGui.GetFontSize();
        var slide = 14f * scale * eased;
        UiCanvas.Text(
            new float2(minimum.X + (16f * scale) + slide, barMiddle - (size * 0.5f)),
            textColor,
            label,
            size
        );

        return pressed;
    }

    /// <summary>A labelled slider: the name on the left, the value on the right, a track between.</summary>
    /// <param name="label">What the value is.</param>
    /// <param name="value">The value, written back as it is dragged.</param>
    /// <param name="minimum">The lowest it goes.</param>
    /// <param name="maximum">The highest it goes.</param>
    /// <param name="width">How wide the whole row is, in pixels.</param>
    /// <param name="reading">How the value reads, already formatted with its unit.</param>
    /// <returns>Whether the value changed this frame.</returns>
    public bool Slider(string label, ref float value, float minimum, float maximum, float width, string reading)
    {
        var scale = ImGui.GetStyle().FontScaleMain;
        var height = RowHeight * scale;
        var origin = ImGui.GetCursorScreenPos();

        ImGui.InvisibleButton(label, new float2(width, height));
        var hovered = ImGui.IsItemHovered();
        var held = ImGui.IsItemActive();

        var size = ImGui.GetFontSize();
        var readingWidth = UiCanvas.Measure(reading, size).X;
        var labelWidth = width * 0.42f;
        var trackLeft = origin.X + labelWidth;
        var trackRight = origin.X + width - readingWidth - (14f * scale);
        var middleY = origin.Y + (height * 0.5f);

        if (held && trackRight > trackLeft)
        {
            var along = (ImGui.GetMousePos().X - trackLeft) / (trackRight - trackLeft);
            value = float.Lerp(minimum, maximum, float.Clamp(along, 0f, 1f));
        }

        var lit = _motion.Toward(label, hovered || held ? 1f : 0f, ImGui.GetIO().DeltaTime);
        var filled = maximum > minimum ? float.Clamp((value - minimum) / (maximum - minimum), 0f, 1f) : 0f;
        var grabX = float.Lerp(trackLeft, trackRight, filled);

        UiCanvas.Text(
            new float2(origin.X, middleY - (size * 0.5f)),
            LerpColor(UiPalette.MutedText, UiPalette.Text, lit),
            label,
            size
        );

        var trackHeight = 4f * scale;
        UiCanvas.Rect(
            new float2(trackLeft, middleY - (trackHeight * 0.5f)),
            new float2(trackRight, middleY + (trackHeight * 0.5f)),
            UiPalette.Control,
            trackHeight * 0.5f
        );
        UiCanvas.Rect(
            new float2(trackLeft, middleY - (trackHeight * 0.5f)),
            new float2(grabX, middleY + (trackHeight * 0.5f)),
            UiPalette.Accent,
            trackHeight * 0.5f
        );

        // The grab grows under the pointer rather than changing colour, which keeps a row of them
        // reading as one row with one thing happening in it.
        UiCanvas.Circle(new float2(grabX, middleY), (5f + (2.5f * lit)) * scale, UiPalette.AccentHovered);

        UiCanvas.Text(
            new float2(origin.X + width - readingWidth, middleY - (size * 0.5f)),
            UiPalette.Text,
            reading,
            size
        );

        return held;
    }

    /// <summary>A labelled switch: a pill with a knob that slides across as it turns on.</summary>
    /// <param name="label">What it turns on.</param>
    /// <param name="value">The state, written back when it is clicked.</param>
    /// <param name="width">How wide the whole row is, in pixels.</param>
    /// <returns>Whether it changed.</returns>
    public bool Toggle(string label, ref bool value, float width)
    {
        var scale = ImGui.GetStyle().FontScaleMain;
        var height = RowHeight * scale;
        var origin = ImGui.GetCursorScreenPos();

        ImGui.InvisibleButton(label, new float2(width, height));
        var hovered = ImGui.IsItemHovered();
        var changed = ImGui.IsItemClicked();

        if (changed)
            value = !value;

        var lit = _motion.Toward(label, hovered ? 1f : 0f, ImGui.GetIO().DeltaTime);
        var on = _motion.Toward($"{label}##on", value ? 1f : 0f, ImGui.GetIO().DeltaTime);

        var size = ImGui.GetFontSize();
        var middleY = origin.Y + (height * 0.5f);
        var pillHeight = 18f * scale;
        var pillWidth = 36f * scale;
        var pillRight = origin.X + width;
        var pillLeft = pillRight - pillWidth;

        UiCanvas.Text(
            new float2(origin.X, middleY - (size * 0.5f)),
            LerpColor(UiPalette.MutedText, UiPalette.Text, lit),
            label,
            size
        );

        UiCanvas.Rect(
            new float2(pillLeft, middleY - (pillHeight * 0.5f)),
            new float2(pillRight, middleY + (pillHeight * 0.5f)),
            LerpColor(UiPalette.Control, UiPalette.Accent, UiMotion.Ease(on)),
            pillHeight * 0.5f
        );

        var knobRadius = (pillHeight * 0.5f) - (3f * scale);
        var knobX = float.Lerp(pillLeft + knobRadius + (3f * scale), pillRight - knobRadius - (3f * scale), UiMotion.Ease(on));
        UiCanvas.Circle(new float2(knobX, middleY), knobRadius, value ? UiPalette.Panel : UiPalette.Text);

        return changed;
    }

    /// <summary>One tab of a vertical rail, for a settings screen's categories.</summary>
    /// <param name="label">What the category is.</param>
    /// <param name="selected">Whether it is the one being shown.</param>
    /// <param name="width">How wide the rail is, in pixels.</param>
    /// <returns>Whether it was clicked.</returns>
    public bool Tab(string label, bool selected, float width)
    {
        var scale = ImGui.GetStyle().FontScaleMain;
        var height = (EntryHeight * 0.78f) * scale;
        var origin = ImGui.GetCursorScreenPos();

        ImGui.InvisibleButton($"tab:{label}", new float2(width, height));
        var hovered = ImGui.IsItemHovered();
        var pressed = ImGui.IsItemClicked();

        var chosen = _motion.Toward($"tab:{label}", selected ? 1f : 0f, ImGui.GetIO().DeltaTime);
        var lit = _motion.Toward($"tablit:{label}", hovered ? 1f : 0f, ImGui.GetIO().DeltaTime);
        var eased = UiMotion.Ease(chosen);

        var minimum = origin;
        var maximum = origin + new float2(width, height);
        var size = ImGui.GetFontSize();

        UiCanvas.Rect(minimum, maximum, UiPalette.Fade(UiPalette.Text, 0.07f * float.Max(eased, lit * 0.6f)), 4f * scale);
        UiCanvas.Rect(
            minimum,
            new float2(minimum.X + (2f * scale), maximum.Y),
            UiPalette.Fade(UiPalette.Accent, eased)
        );

        UiCanvas.Text(
            new float2(minimum.X + (14f * scale), origin.Y + (height * 0.5f) - (size * 0.5f)),
            LerpColor(UiPalette.MutedText, UiPalette.Text, float.Max(eased, lit)),
            label,
            size
        );

        return pressed;
    }

    /// <summary>A line of explanation under a control.</summary>
    /// <param name="text">What it says.</param>
    /// <remarks>
    /// Smaller than the control above it and in the muted colour, so a pane of them reads as one
    /// column of controls with notes rather than as two columns of text.
    /// </remarks>
    public static void Note(string text)
    {
        var scale = ImGui.GetStyle().FontScaleMain;
        var size = ImGui.GetFontSize() * 0.88f;
        var origin = ImGui.GetCursorScreenPos();

        ImGui.Dummy(new float2(0f, size + (6f * scale)));
        UiCanvas.Text(new float2(origin.X, origin.Y + (2f * scale)), UiPalette.MutedText, text, size);
    }

    /// <summary>A section's name over a hairline that runs to the right edge.</summary>
    /// <param name="text">What the section is.</param>
    /// <param name="width">How far the rule runs, in pixels.</param>
    public static void Heading(string text, float width)
    {
        var scale = ImGui.GetStyle().FontScaleMain;
        var size = ImGui.GetFontSize() * 0.9f;
        var origin = ImGui.GetCursorScreenPos();

        ImGui.Dummy(new float2(width, size + (16f * scale)));

        var textWidth = UiCanvas.TrackedText(
            new float2(origin.X, origin.Y + (8f * scale)),
            UiPalette.Accent,
            text.ToUpperInvariant(),
            size,
            1.5f * scale
        );

        var ruleY = origin.Y + (8f * scale) + (size * 0.5f);
        UiCanvas.Line(
            new float2(origin.X + textWidth + (12f * scale), ruleY),
            new float2(origin.X + width, ruleY),
            UiPalette.Divider
        );
    }

    private static float4 LerpColor(float4 from, float4 to, float amount) =>
        new(
            float.Lerp(from.X, to.X, amount),
            float.Lerp(from.Y, to.Y, amount),
            float.Lerp(from.Z, to.Z, amount),
            float.Lerp(from.W, to.W, amount)
        );
}
