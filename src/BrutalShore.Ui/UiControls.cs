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
/// They are built like machine panels: a chamfered face cut out of the panel behind it, a lit
/// hairline along its top edge, and one amber marker saying which one is live. Every control
/// repeats those three things at a different size, which is what makes a screen of them read as
/// one piece of equipment rather than a list of widgets.
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
    public const float RowHeight = 38f;

    /// <summary>How far a control's corners are cut back before the scale, in pixels.</summary>
    /// <remarks>
    /// One number for the whole interface. A chamfer that changes size from control to control
    /// reads as several shapes; one that does not reads as one material cut by one machine.
    /// </remarks>
    public const float Cut = 6f;

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
    /// One line of a menu: a chamfered plate with an amber marker down its left edge that grows
    /// as the pointer crosses it.
    /// </summary>
    /// <param name="label">What it says.</param>
    /// <param name="width">How wide the entry is, in pixels.</param>
    /// <param name="emphasis">
    /// Whether this is the obvious thing to do on the screen, which fills it with the accent
    /// rather than marking its edge.
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

        var lit = UiMotion.Ease(_motion.Toward(label, hovered || held ? 1f : 0f, ImGui.GetIO().DeltaTime));

        var minimum = origin;
        var maximum = origin + new float2(width, height);
        var cut = Cut * scale;
        var marker = 5f * scale;

        var face = emphasis
            ? LerpColor(UiPalette.Accent, UiPalette.AccentHovered, lit)
            : LerpColor(UiPalette.Control, UiPalette.Hovered, lit);

        UiCanvas.Chamfer(minimum, maximum, held ? LerpColor(face, UiPalette.Pressed, 0.4f) : face, cut);

        // The lit top edge, which is what makes the plate read as raised rather than painted. It
        // stops short of the cuts so that it does not draw a line across them.
        UiCanvas.Line(
            new float2(minimum.X + cut, minimum.Y + (0.5f * scale)),
            new float2(maximum.X - cut, minimum.Y + (0.5f * scale)),
            UiPalette.Fade(emphasis ? UiPalette.Text : UiPalette.Edge, 0.35f + (0.4f * lit)),
            scale
        );

        if (!emphasis)
        {
            // The marker runs the full height and widens, rather than growing from the middle: a
            // bar that changes length reads as a progress bar, and this is not one.
            UiCanvas.Chamfer(
                minimum,
                new float2(minimum.X + (marker * (0.55f + (0.45f * lit))), maximum.Y),
                UiPalette.Fade(UiPalette.Accent, 0.5f + (0.5f * lit)),
                cut * 0.5f
            );
        }

        var textColor = emphasis ? UiPalette.Scrim : LerpColor(UiPalette.MutedText, UiPalette.Text, lit);
        var tracking = 1.6f * scale;
        var margin = 22f * scale;
        var size = Fitted(label, ImGui.GetFontSize() * 0.92f, tracking, width - (margin * 2f));

        // The label does not move when the pointer crosses it. It used to slide a few pixels to
        // the right, which reads as a nudge on one button and as the whole menu shivering when a
        // pointer sweeps down a column of them — and it makes the word unreadable for exactly as
        // long as it takes to read. What animates instead is the plate under it and the marker
        // beside it, neither of which anybody is trying to read.
        UiCanvas.TrackedText(
            new float2(minimum.X + margin, origin.Y + (height * 0.5f) - (size * 0.5f)),
            textColor,
            label.ToUpperInvariant(),
            size,
            tracking
        );

        return pressed;
    }

    /// <summary>A labelled slider: the name on the left, the value boxed on the right.</summary>
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

        var size = ImGui.GetFontSize() * 0.9f;
        var cut = (Cut - 2f) * scale;
        var boxWidth = 76f * scale;
        var labelWidth = width * 0.38f;
        var trackLeft = origin.X + labelWidth;
        var trackRight = origin.X + width - boxWidth - (12f * scale);
        var middleY = origin.Y + (height * 0.5f);

        if (held && trackRight > trackLeft)
        {
            var along = (ImGui.GetMousePos().X - trackLeft) / (trackRight - trackLeft);
            value = float.Lerp(minimum, maximum, float.Clamp(along, 0f, 1f));
        }

        var lit = UiMotion.Ease(_motion.Toward(label, hovered || held ? 1f : 0f, ImGui.GetIO().DeltaTime));
        var filled = maximum > minimum ? float.Clamp((value - minimum) / (maximum - minimum), 0f, 1f) : 0f;
        var grabX = float.Lerp(trackLeft, trackRight, filled);

        UiCanvas.Text(
            new float2(origin.X, middleY - (size * 0.5f)),
            LerpColor(UiPalette.MutedText, UiPalette.Text, lit),
            label,
            size
        );

        var trackHeight = 10f * scale;
        var trackTop = middleY - (trackHeight * 0.5f);
        var trackBottom = middleY + (trackHeight * 0.5f);

        // A recess, not a line: dark fill, dark outline, and the fill drawn inside it, so the
        // amber reads as something sitting in a channel rather than a stripe on the panel.
        UiCanvas.Chamfer(new float2(trackLeft, trackTop), new float2(trackRight, trackBottom), UiPalette.Recess, cut * 0.6f);

        if (grabX > trackLeft + 1f)
        {
            UiCanvas.Chamfer(
                new float2(trackLeft, trackTop),
                new float2(grabX, trackBottom),
                LerpColor(UiPalette.Accent, UiPalette.AccentHovered, lit),
                cut * 0.6f
            );
        }

        UiCanvas.ChamferOutline(
            new float2(trackLeft, trackTop),
            new float2(trackRight, trackBottom),
            UiPalette.Fade(UiPalette.Edge, 0.5f + (0.5f * lit)),
            cut * 0.6f,
            scale
        );

        // A square grip rather than a knob, because a round handle on a cut track is the one
        // moulded shape on a screen of machined ones.
        var gripWidth = 7f * scale;
        var gripHeight = (trackHeight + (8f * scale)) * (0.9f + (0.1f * lit));
        UiCanvas.Chamfer(
            new float2(grabX - (gripWidth * 0.5f), middleY - (gripHeight * 0.5f)),
            new float2(grabX + (gripWidth * 0.5f), middleY + (gripHeight * 0.5f)),
            LerpColor(UiPalette.Text, UiPalette.AccentHovered, lit),
            3f * scale
        );

        var boxLeft = origin.X + width - boxWidth;
        UiCanvas.Chamfer(
            new float2(boxLeft, trackTop - (3f * scale)),
            new float2(origin.X + width, trackBottom + (3f * scale)),
            UiPalette.Recess,
            cut * 0.6f
        );

        var readingWidth = UiCanvas.Measure(reading, size).X;
        UiCanvas.Text(
            new float2(boxLeft + ((boxWidth - readingWidth) * 0.5f), middleY - (size * 0.5f)),
            held ? UiPalette.AccentHovered : UiPalette.Text,
            reading,
            size
        );

        return held;
    }

    /// <summary>A labelled switch: a chamfered plate that says what it is doing.</summary>
    /// <param name="label">What it turns on.</param>
    /// <param name="value">The state, written back when it is clicked.</param>
    /// <param name="width">How wide the whole row is, in pixels.</param>
    /// <returns>Whether it changed.</returns>
    /// <remarks>
    /// The word is drawn rather than only the position, because a sliding knob says which side it
    /// is on and not which side means on. A switch reading ON is unambiguous at a glance and in a
    /// screenshot, which is where half of these are ever looked at.
    /// </remarks>
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

        var lit = UiMotion.Ease(_motion.Toward(label, hovered ? 1f : 0f, ImGui.GetIO().DeltaTime));
        var on = UiMotion.Ease(_motion.Toward($"{label}##on", value ? 1f : 0f, ImGui.GetIO().DeltaTime));

        var size = ImGui.GetFontSize() * 0.9f;
        var middleY = origin.Y + (height * 0.5f);
        var cut = (Cut - 2f) * scale;
        var plateWidth = 76f * scale;
        var plateHeight = 22f * scale;
        var plateRight = origin.X + width;
        var plateLeft = plateRight - plateWidth;
        var plateTop = middleY - (plateHeight * 0.5f);
        var plateBottom = middleY + (plateHeight * 0.5f);

        UiCanvas.Text(
            new float2(origin.X, middleY - (size * 0.5f)),
            LerpColor(UiPalette.MutedText, UiPalette.Text, float.Max(lit, on)),
            label,
            size
        );

        UiCanvas.Chamfer(new float2(plateLeft, plateTop), new float2(plateRight, plateBottom), UiPalette.Recess, cut * 0.6f);

        // The lit half slides across the recess, so the change reads as a throw rather than a
        // repaint, and the word on it stays put.
        var halfWidth = plateWidth * 0.5f;
        var litLeft = float.Lerp(plateLeft, plateLeft + halfWidth, on);
        UiCanvas.Chamfer(
            new float2(litLeft, plateTop),
            new float2(litLeft + halfWidth, plateBottom),
            LerpColor(UiPalette.Hovered, LerpColor(UiPalette.Accent, UiPalette.AccentHovered, lit), on),
            cut * 0.6f
        );

        UiCanvas.ChamferOutline(
            new float2(plateLeft, plateTop),
            new float2(plateRight, plateBottom),
            UiPalette.Fade(UiPalette.Edge, 0.4f + (0.4f * lit)),
            cut * 0.6f,
            scale
        );

        var word = value ? "ON" : "OFF";
        var wordSize = size * 0.86f;
        var wordWidth = UiCanvas.Measure(word, wordSize).X + (1.4f * scale);
        var wordX = value
            ? plateLeft + halfWidth + ((halfWidth - wordWidth) * 0.5f)
            : plateLeft + ((halfWidth - wordWidth) * 0.5f);

        UiCanvas.TrackedText(
            new float2(wordX, middleY - (wordSize * 0.5f)),
            value ? UiPalette.Scrim : UiPalette.MutedText,
            word,
            wordSize,
            1.4f * scale
        );

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
        var height = (EntryHeight * 0.82f) * scale;
        var origin = ImGui.GetCursorScreenPos();

        ImGui.InvisibleButton($"tab:{label}", new float2(width, height));
        var hovered = ImGui.IsItemHovered();
        var pressed = ImGui.IsItemClicked();

        var chosen = UiMotion.Ease(_motion.Toward($"tab:{label}", selected ? 1f : 0f, ImGui.GetIO().DeltaTime));
        var lit = UiMotion.Ease(_motion.Toward($"tablit:{label}", hovered ? 1f : 0f, ImGui.GetIO().DeltaTime));

        var minimum = origin;
        var maximum = origin + new float2(width, height - (4f * scale));
        var cut = Cut * scale;
        var size = ImGui.GetFontSize() * 0.88f;

        UiCanvas.Chamfer(
            minimum,
            maximum,
            LerpColor(UiPalette.Fade(UiPalette.Control, 0.5f), UiPalette.Control, float.Max(chosen, lit * 0.7f)),
            cut
        );

        UiCanvas.Chamfer(
            minimum,
            new float2(minimum.X + (4f * scale), maximum.Y),
            UiPalette.Fade(UiPalette.Accent, 0.25f + (0.75f * chosen)),
            cut * 0.5f
        );

        UiCanvas.TrackedText(
            new float2(minimum.X + (18f * scale), origin.Y + ((height - (4f * scale)) * 0.5f) - (size * 0.5f)),
            LerpColor(UiPalette.MutedText, UiPalette.Text, float.Max(chosen, lit)),
            label.ToUpperInvariant(),
            size,
            1.4f * scale
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
        var size = ImGui.GetFontSize() * 0.86f;
        var origin = ImGui.GetCursorScreenPos();

        ImGui.Dummy(new float2(0f, size + (8f * scale)));
        UiCanvas.Text(new float2(origin.X + (2f * scale), origin.Y + (2f * scale)), UiPalette.MutedText, text, size);
    }

    /// <summary>A section's name over a band of hazard stripes that runs to the right edge.</summary>
    /// <param name="text">What the section is.</param>
    /// <param name="width">How far the band runs, in pixels.</param>
    public static void Heading(string text, float width)
    {
        var scale = ImGui.GetStyle().FontScaleMain;
        var size = ImGui.GetFontSize() * 0.88f;
        var origin = ImGui.GetCursorScreenPos();

        ImGui.Dummy(new float2(width, size + (20f * scale)));

        var textWidth = UiCanvas.TrackedText(
            new float2(origin.X, origin.Y + (10f * scale)),
            UiPalette.Accent,
            text.ToUpperInvariant(),
            size,
            2f * scale
        );

        var bandLeft = origin.X + textWidth + (14f * scale);
        var bandRight = origin.X + width;
        if (bandRight <= bandLeft)
            return;

        var bandTop = origin.Y + (10f * scale) + (size * 0.5f) - (4f * scale);
        var bandMinimum = new float2(bandLeft, bandTop);
        var bandMaximum = new float2(bandRight, bandTop + (8f * scale));

        UiCanvas.Rect(bandMinimum, bandMaximum, UiPalette.Fade(UiPalette.Recess, 0.8f));
        UiCanvas.HazardStripes(bandMinimum, bandMaximum, UiPalette.Fade(UiPalette.Accent, 0.3f), 18f * scale);
    }

    /// <summary>
    /// The largest size at or below the one asked for at which the label still fits the plate.
    /// </summary>
    /// <remarks>
    /// Shrunk rather than clipped or ellipsised. A menu entry is one phrase chosen by whoever
    /// wrote the screen, and the two honest answers to one that is a word too long are to set it
    /// smaller or to rename it — where an ellipsis hides which of the two happened. The floor is
    /// three quarters, below which the entry would be visibly a different size from its
    /// neighbours and renaming it is the only real answer.
    /// </remarks>
    private static float Fitted(string label, float size, float tracking, float room)
    {
        if (room <= 0f)
            return size;

        var width = UiCanvas.MeasureTracked(label.ToUpperInvariant(), size, tracking);
        return width <= room ? size : float.Max(size * room / width, size * 0.75f);
    }

    private static float4 LerpColor(float4 from, float4 to, float amount) =>
        new(
            float.Lerp(from.X, to.X, amount),
            float.Lerp(from.Y, to.Y, amount),
            float.Lerp(from.Z, to.Z, amount),
            float.Lerp(from.W, to.W, amount)
        );
}
