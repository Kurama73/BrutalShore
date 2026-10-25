using Brutal.ImGuiApi;
using BrutalShore.Ui;

namespace Shorebound;

/// <summary>What the player sees over the world: the crosshair, the hotbar, and the menus.</summary>
/// <remarks>
/// <para>
/// Drawn rather than laid out. Every panel here is an ImGui window with no decoration and no
/// background, filled in with <see cref="UiCanvas" /> and <see cref="UiControls" />, because a
/// title screen is the first thing anyone sees and a default widget set says, correctly, that
/// nobody looked at it.
/// </para>
/// <para>
/// It draws and reports; it never acts. A button returns a <see cref="MenuChoice" /> and the game
/// decides what that means, because loading a world and quitting are not decisions an interface
/// should be making on its own. The settings screen is the one half-exception: it writes the
/// values a player moved straight into the settings it was handed, and still leaves saving them,
/// and acting on them, to the game.
/// </para>
/// </remarks>
public sealed class GameUi
{
    /// <summary>How tall the wordmark is, in pixels, before the interface scale.</summary>
    private const float TitleSize = 52f;

    /// <summary>How much space goes between the wordmark's letters, before the scale.</summary>
    private const float TitleTracking = 7f;

    /// <summary>How wide the menu column is, before the scale.</summary>
    private const float MenuWidth = 300f;

    /// <summary>How wide the settings panel is, before the scale.</summary>
    private const float SettingsWidth = 760f;

    /// <summary>How wide the settings panel's category rail is, before the scale.</summary>
    private const float RailWidth = 180f;

    private const ImGuiWindowFlags Bare =
        ImGuiWindowFlags.NoDecoration
        | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoSavedSettings
        | ImGuiWindowFlags.NoFocusOnAppearing
        | ImGuiWindowFlags.NoNav
        | ImGuiWindowFlags.NoBackground
        | ImGuiWindowFlags.NoScrollbar;

    private readonly UiControls _controls = new();
    private SettingsSection _section = SettingsSection.World;

    /// <summary>Which group of settings the panel is showing.</summary>
    private enum SettingsSection
    {
        World,
        Sight,
        Controls,
        Interface,
    }

    /// <summary>Forgets every animation, so a screen opens already settled rather than mid-move.</summary>
    public void Reset() => _controls.Reset();

    /// <summary>
    /// Draws the crosshair, which grows and takes the accent when something is in reach.
    /// </summary>
    /// <param name="aiming">Whether a block is within reach of it.</param>
    /// <remarks>
    /// Four strokes around a gap rather than a glyph. A letter drawn at the middle of the screen
    /// is a letter whose weight and spacing were decided for prose; a crosshair has to stay
    /// readable over sand and over a night sky, which is what the gap and the dot are for.
    /// </remarks>
    public void DrawCrosshair(bool aiming)
    {
        var viewport = ImGui.GetMainViewport();
        var middle = viewport.Pos + (viewport.Size * 0.5f);

        FullScreen("crosshair"u8);

        var scale = ImGui.GetStyle().FontScaleMain;
        var grown = aiming ? 1f : 0f;
        var gap = (4f + grown) * scale;
        var arm = (7f + (2f * grown)) * scale;
        var thickness = 1.6f * scale;
        var color = aiming ? UiPalette.Accent : UiPalette.Fade(UiPalette.Text, 0.78f);

        UiCanvas.Line(new float2(middle.X - gap - arm, middle.Y), new float2(middle.X - gap, middle.Y), color, thickness);
        UiCanvas.Line(new float2(middle.X + gap, middle.Y), new float2(middle.X + gap + arm, middle.Y), color, thickness);
        UiCanvas.Line(new float2(middle.X, middle.Y - gap - arm), new float2(middle.X, middle.Y - gap), color, thickness);
        UiCanvas.Line(new float2(middle.X, middle.Y + gap), new float2(middle.X, middle.Y + gap + arm), color, thickness);
        UiCanvas.Circle(middle, 1.2f * scale, color);

        ImGui.End();
    }

    /// <summary>Draws the row of blocks along the bottom of the screen.</summary>
    /// <param name="hotbar">The row to draw.</param>
    /// <param name="colorOf">What colour a slot's block is, for its swatch.</param>
    /// <remarks>
    /// A row of swatches rather than a row of words. The thing a player is choosing between is
    /// blocks, and a block is a colour in this game — so the colour is the label, and the name
    /// appears once, over whichever one is in hand.
    /// </remarks>
    public void DrawHotbar(Hotbar hotbar, Func<int, float4> colorOf)
    {
        var viewport = ImGui.GetMainViewport();
        var scale = ImGui.GetStyle().FontScaleMain;

        var slot = 46f * scale;
        var gap = 6f * scale;
        var totalWidth = (slot * hotbar.Count) + (gap * (hotbar.Count - 1));
        var left = viewport.Pos.X + ((viewport.Size.X - totalWidth) * 0.5f);
        var bottom = viewport.Pos.Y + viewport.Size.Y - (34f * scale);
        var top = bottom - slot;

        FullScreen("hotbar"u8);

        var size = ImGui.GetFontSize();
        var name = hotbar.SelectedName;
        var nameWidth = UiCanvas.Measure(name, size).X;
        UiCanvas.Text(
            new float2(viewport.Pos.X + ((viewport.Size.X - nameWidth) * 0.5f), top - size - (10f * scale)),
            UiPalette.Text,
            name,
            size
        );

        for (var index = 0; index < hotbar.Count; index++)
        {
            var chosen = index == hotbar.SelectedIndex;
            var x = left + (index * (slot + gap));
            var lift = chosen ? 4f * scale : 0f;
            var minimum = new float2(x, top - lift);
            var maximum = new float2(x + slot, bottom - lift);

            UiCanvas.Rect(minimum, maximum, UiPalette.Fade(UiPalette.Scrim, chosen ? 0.82f : 0.6f), 5f * scale);

            var inset = 7f * scale;
            UiCanvas.Rect(
                minimum + new float2(inset, inset),
                maximum - new float2(inset, inset),
                UiPalette.Fade(colorOf(index), chosen ? 1f : 0.85f),
                3f * scale
            );

            UiCanvas.Outline(
                minimum,
                maximum,
                chosen ? UiPalette.Accent : UiPalette.Fade(UiPalette.Text, 0.14f),
                5f * scale,
                chosen ? 2f * scale : 1f
            );

            // Under the slot rather than inside it. A number in the corner of a swatch is a
            // number on top of whatever colour that block happens to be, and half the blocks in
            // this game are pale.
            var number = $"{index + 1}";
            var numberSize = size * 0.78f;
            var numberWidth = UiCanvas.Measure(number, numberSize).X;
            UiCanvas.Text(
                new float2(x + ((slot - numberWidth) * 0.5f), bottom + (6f * scale)),
                chosen ? UiPalette.Accent : UiPalette.Fade(UiPalette.MutedText, 0.75f),
                number,
                numberSize
            );
        }

        ImGui.End();
    }

    /// <summary>Draws the title screen and reports what was clicked.</summary>
    /// <param name="hasSave">Whether there is a world to continue.</param>
    /// <param name="footer">A line of small print along the bottom: the seed, the version.</param>
    /// <returns>What the player chose, or <see cref="MenuChoice.None" />.</returns>
    /// <remarks>
    /// Left-aligned over a live world rather than centred over a picture of one. The world behind
    /// it is the point, and a panel across the middle of the screen is a panel saying so about
    /// itself instead.
    /// </remarks>
    public MenuChoice DrawTitle(bool hasSave, string footer)
    {
        var viewport = ImGui.GetMainViewport();
        var scale = ImGui.GetStyle().FontScaleMain;
        var choice = MenuChoice.None;

        FullScreen("title"u8);

        UiCanvas.Scrim(0.75f);

        // A band down the left for the menu to sit on. The vignette alone is not enough contrast
        // for small text over a sunlit field, and a band is what every game that puts its menu
        // over a live scene ends up drawing.
        UiCanvas.HorizontalGradient(
            viewport.Pos,
            viewport.Pos + new float2(viewport.Size.X * 0.46f, viewport.Size.Y),
            UiPalette.Fade(UiPalette.Scrim, 0.86f),
            UiPalette.Fade(UiPalette.Scrim, 0f)
        );

        var left = viewport.Pos.X + float.Max(72f * scale, viewport.Size.X * 0.09f);
        var top = viewport.Pos.Y + (viewport.Size.Y * 0.3f);

        var markSize = TitleSize * scale;
        var markHeight = UiCanvas.Measure("SHOREBOUND", markSize).Y;
        var markWidth = UiCanvas.TrackedText(
            new float2(left, top),
            UiPalette.Text,
            "SHOREBOUND",
            markSize,
            TitleTracking * scale
        );

        // Measured rather than assumed: a font's line box is taller than its capitals, and a rule
        // placed at the nominal size floats away from the word it belongs to.
        var ruleY = top + markHeight + (10f * scale);
        UiCanvas.Rect(
            new float2(left, ruleY),
            new float2(left + markWidth, ruleY + (3f * scale)),
            UiPalette.Accent
        );

        var subtitleY = ruleY + (18f * scale);
        UiCanvas.TrackedText(
            new float2(left, subtitleY),
            UiPalette.Text,
            "A VOXEL WORLD ON BRUTAL",
            ImGui.GetFontSize() * 0.86f,
            2.2f * scale
        );

        var width = MenuWidth * scale;
        UiControls.BeginColumn(
            "titlemenu",
            new float2(left - (16f * scale), subtitleY + (48f * scale)),
            new float2(width, UiControls.EntryHeight * scale * 4.4f)
        );

        if (hasSave)
        {
            if (_controls.Entry("Continue", width, emphasis: true))
                choice = MenuChoice.Continue;

            if (_controls.Entry("New world", width))
                choice = MenuChoice.NewWorld;
        }
        else if (_controls.Entry("Play", width, emphasis: true))
        {
            choice = MenuChoice.NewWorld;
        }

        if (_controls.Entry("Settings", width))
            choice = MenuChoice.Settings;

        if (_controls.Entry("Quit", width))
            choice = MenuChoice.Quit;

        UiControls.EndColumn();

        var footerSize = ImGui.GetFontSize() * 0.84f;
        UiCanvas.Text(
            new float2(left, viewport.Pos.Y + viewport.Size.Y - footerSize - (28f * scale)),
            UiPalette.MutedText,
            footer,
            footerSize
        );

        ImGui.End();
        return choice;
    }

    /// <summary>Draws the pause menu and reports what was clicked.</summary>
    /// <param name="savePath">Where the world would be written, for the player to see.</param>
    /// <returns>What the player chose, or <see cref="MenuChoice.None" />.</returns>
    public MenuChoice DrawPause(string savePath)
    {
        var viewport = ImGui.GetMainViewport();
        var scale = ImGui.GetStyle().FontScaleMain;
        var choice = MenuChoice.None;

        FullScreen("pause"u8);

        UiCanvas.Scrim(1f);

        var width = MenuWidth * scale;
        var height = 318f * scale;
        var padding = 28f * scale;
        var left = viewport.Pos.X + ((viewport.Size.X - width) * 0.5f);
        var top = viewport.Pos.Y + ((viewport.Size.Y - height) * 0.5f);

        // A card, rather than text floating over the world. A pause menu has to look like the
        // game stopped, and nothing on a transparent background ever does.
        var cardMinimum = new float2(left - padding, top - padding);
        var cardMaximum = new float2(left + width + padding, top + height + padding);
        UiCanvas.PanelShadow(cardMinimum, cardMaximum, 10f * scale, 26f * scale);
        UiCanvas.Rect(cardMinimum, cardMaximum, UiPalette.Panel, 10f * scale);
        UiCanvas.Outline(cardMinimum, cardMaximum, UiPalette.Divider, 10f * scale);

        var headingSize = ImGui.GetFontSize() * 1.05f;
        UiCanvas.TrackedText(
            new float2(left, top),
            UiPalette.Accent,
            "PAUSED",
            headingSize,
            3f * scale
        );
        UiCanvas.Line(
            new float2(left, top + headingSize + (12f * scale)),
            new float2(left + width, top + headingSize + (12f * scale)),
            UiPalette.Divider
        );

        var entriesTop = top + headingSize + (26f * scale);
        UiControls.BeginColumn(
            "pausemenu",
            new float2(left, entriesTop),
            new float2(width, UiControls.EntryHeight * scale * 4.4f)
        );

        if (_controls.Entry("Resume", width, emphasis: true))
            choice = MenuChoice.Resume;

        if (_controls.Entry("Settings", width))
            choice = MenuChoice.Settings;

        if (_controls.Entry("Save world", width))
            choice = MenuChoice.Save;

        if (_controls.Entry("Save and quit", width))
            choice = MenuChoice.SaveAndQuit;

        UiControls.EndColumn();

        var noteSize = ImGui.GetFontSize() * 0.84f;
        UiCanvas.Text(
            new float2(left, entriesTop + (UiControls.EntryHeight * scale * 4.4f) + (10f * scale)),
            UiPalette.MutedText,
            savePath,
            noteSize
        );

        ImGui.End();
        return choice;
    }

    /// <summary>
    /// Draws the settings, writing anything the player moves straight back into
    /// <paramref name="settings" />.
    /// </summary>
    /// <param name="settings">The settings to show and change.</param>
    /// <param name="changed">Whether anything on this frame was moved.</param>
    /// <returns><see cref="MenuChoice.Back" /> if the player is done, otherwise nothing.</returns>
    /// <remarks>
    /// Written back as it moves rather than on a button, because every one of these applies to
    /// the world being drawn behind the panel: a field of view you have to confirm to see is a
    /// field of view set by trial and error.
    /// </remarks>
    public MenuChoice DrawSettings(ref Settings settings, out bool changed)
    {
        var viewport = ImGui.GetMainViewport();
        var scale = ImGui.GetStyle().FontScaleMain;
        var choice = MenuChoice.None;
        changed = false;

        FullScreen("settings"u8);

        UiCanvas.Scrim(1f);

        var width = float.Min(SettingsWidth * scale, viewport.Size.X - (80f * scale));
        var height = float.Min(520f * scale, viewport.Size.Y - (80f * scale));
        var left = viewport.Pos.X + ((viewport.Size.X - width) * 0.5f);
        var top = viewport.Pos.Y + ((viewport.Size.Y - height) * 0.5f);
        var rail = RailWidth * scale;

        UiCanvas.PanelShadow(
            new float2(left, top),
            new float2(left + width, top + height),
            10f * scale,
            26f * scale
        );
        UiCanvas.Rect(
            new float2(left, top),
            new float2(left + width, top + height),
            UiPalette.Panel,
            10f * scale
        );
        UiCanvas.Outline(
            new float2(left, top),
            new float2(left + width, top + height),
            UiPalette.Divider,
            10f * scale
        );

        // The rail gets its own slightly darker field, which is what tells a glance that the left
        // column chooses and the right column changes.
        UiCanvas.Rect(
            new float2(left, top),
            new float2(left + rail, top + height),
            UiPalette.Fade(UiPalette.Scrim, 0.45f),
            10f * scale
        );
        UiCanvas.Line(new float2(left + rail, top), new float2(left + rail, top + height), UiPalette.Divider);

        var headingSize = ImGui.GetFontSize() * 1.05f;
        UiCanvas.TrackedText(
            new float2(left + (20f * scale), top + (22f * scale)),
            UiPalette.Accent,
            "SETTINGS",
            headingSize,
            3f * scale
        );

        DrawRail(left, top, rail, height, scale);
        changed = DrawSection(ref settings, left + rail, top, width - rail, height, scale);

        var footerTop = top + height - (58f * scale);

        UiControls.BeginColumn(
            "settingsback",
            new float2(left + rail + (28f * scale), footerTop),
            new float2(180f * scale, UiControls.EntryHeight * scale * 1.1f)
        );
        if (_controls.Entry("Back", 180f * scale, emphasis: true))
            choice = MenuChoice.Back;
        UiControls.EndColumn();

        UiControls.BeginColumn(
            "settingsdefaults",
            new float2(left + width - (218f * scale), footerTop),
            new float2(190f * scale, UiControls.EntryHeight * scale * 1.1f)
        );
        if (_controls.Entry("Restore defaults", 190f * scale))
        {
            settings = Settings.Default;
            changed = true;
        }

        UiControls.EndColumn();

        ImGui.End();
        return choice;
    }

    /// <summary>Draws a line that fades away, for the result of saving or loading.</summary>
    /// <param name="message">What happened.</param>
    /// <param name="opacity">How visible it still is, from 1 down to 0.</param>
    public void DrawNotice(string message, float opacity)
    {
        if (opacity <= 0f)
            return;

        var viewport = ImGui.GetMainViewport();
        var scale = ImGui.GetStyle().FontScaleMain;
        var size = ImGui.GetFontSize();

        FullScreen("notice"u8);

        var textWidth = UiCanvas.Measure(message, size).X;
        var padding = new float2(18f * scale, 10f * scale);
        var middleX = viewport.Pos.X + (viewport.Size.X * 0.5f);
        var top = viewport.Pos.Y + (34f * scale);

        var minimum = new float2(middleX - (textWidth * 0.5f) - padding.X, top - padding.Y);
        var maximum = new float2(middleX + (textWidth * 0.5f) + padding.X, top + size + padding.Y);

        UiCanvas.Rect(minimum, maximum, UiPalette.Fade(UiPalette.Scrim, 0.8f * opacity), 6f * scale);
        UiCanvas.Rect(
            minimum,
            new float2(minimum.X + (3f * scale), maximum.Y),
            UiPalette.Fade(UiPalette.Accent, opacity)
        );
        UiCanvas.Text(
            new float2(middleX - (textWidth * 0.5f), top),
            UiPalette.Fade(UiPalette.Text, opacity),
            message,
            size
        );

        ImGui.End();
    }

    /// <summary>Opens an invisible window covering the viewport, to draw into.</summary>
    /// <remarks>
    /// Everything here is drawn at absolute screen positions, so the window is only there to give
    /// a draw list and, where there are controls, somewhere for ImGui to track them. It takes
    /// input only when it holds something that wants it.
    /// </remarks>
    private static void FullScreen(ReadOnlySpan<byte> name)
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.Begin(name, Bare);
    }

    private void DrawRail(float left, float top, float rail, float height, float scale)
    {
        UiControls.BeginColumn(
            "rail",
            new float2(left + (10f * scale), top + (72f * scale)),
            new float2(rail - (20f * scale), height - (90f * scale))
        );

        foreach (var section in Enum.GetValues<SettingsSection>())
        {
            if (_controls.Tab(section.ToString(), _section == section, rail - (20f * scale)))
                _section = section;
        }

        UiControls.EndColumn();
    }

    private bool DrawSection(ref Settings settings, float left, float top, float width, float height, float scale)
    {
        var inner = width - (56f * scale);

        UiControls.BeginColumn(
            "section",
            new float2(left + (28f * scale), top + (60f * scale)),
            new float2(inner, height - (130f * scale))
        );

        var changed = _section switch
        {
            SettingsSection.World => DrawWorld(ref settings, inner),
            SettingsSection.Sight => DrawSight(ref settings, inner),
            SettingsSection.Controls => DrawControls(ref settings, inner),
            _ => DrawInterface(ref settings, inner),
        };

        UiControls.EndColumn();
        return changed;
    }

    private bool DrawWorld(ref Settings settings, float width)
    {
        UiControls.Heading("World", width);

        var changed = false;
        var viewRadius = (float)settings.ViewRadiusChunks;
        var dayLength = (float)settings.DayLengthSeconds;

        if (_controls.Slider("View distance", ref viewRadius, 2f, 24f, width, $"{(int)viewRadius} chunks"))
        {
            settings = settings with { ViewRadiusChunks = (int)float.Round(viewRadius) };
            changed = true;
        }

        var resident = ((2 * settings.ViewRadiusChunks) + 1) * ((2 * settings.ViewRadiusChunks) + 1) * 8;
        UiControls.Note($"{settings.ViewRadiusChunks * 32} m of world, {resident} chunks held in memory");

        if (_controls.Slider("Day length", ref dayLength, 0f, 3600f, width, Minutes(dayLength)))
        {
            settings = settings with { DayLengthSeconds = dayLength };
            changed = true;
        }

        UiControls.Note(dayLength <= 0f ? "The sun stays where it is" : "How long one sunrise is from the next");

        return changed;
    }

    private bool DrawSight(ref Settings settings, float width)
    {
        UiControls.Heading("Sight", width);

        var changed = false;
        var fieldOfView = settings.FieldOfViewDegrees;
        var brightness = settings.Brightness;
        var shadows = settings.Shadows;
        var shadowStrength = settings.ShadowStrength;
        var fog = settings.Fog;

        if (_controls.Slider("Field of view", ref fieldOfView, 50f, 110f, width, $"{fieldOfView:F0}°"))
        {
            settings = settings with { FieldOfViewDegrees = fieldOfView };
            changed = true;
        }

        if (_controls.Slider("Brightness", ref brightness, 0.5f, 1.8f, width, $"{brightness:F2}×"))
        {
            settings = settings with { Brightness = brightness };
            changed = true;
        }

        if (_controls.Toggle("Shadows", ref shadows, width))
        {
            settings = settings with { Shadows = shadows };
            changed = true;
        }

        if (shadows && _controls.Slider("Shadow strength", ref shadowStrength, 0f, 1f, width, $"{shadowStrength:F2}"))
        {
            settings = settings with { ShadowStrength = shadowStrength };
            changed = true;
        }

        if (_controls.Toggle("Distance fog", ref fog, width))
        {
            settings = settings with { Fog = fog };
            changed = true;
        }

        UiControls.Note("With fog off the edge of the loaded world is visible");

        return changed;
    }

    private bool DrawControls(ref Settings settings, float width)
    {
        UiControls.Heading("Controls", width);

        var changed = false;
        var sensitivity = settings.LookSensitivity;

        if (_controls.Slider("Mouse sensitivity", ref sensitivity, 5f, 200f, width, $"{sensitivity:F0}"))
        {
            settings = settings with { LookSensitivity = sensitivity };
            changed = true;
        }

        UiControls.Note("50 is the speed the game ships with");

        UiControls.Heading("Keys", width);
        UiControls.Note("WASD to move, space to jump, shift to sprint");
        UiControls.Note("F flies, space and ctrl go up and down while flying");
        UiControls.Note("Left click breaks, right click places, 1 to 9 or Q and E pick");
        UiControls.Note("Escape opens this menu, F3 shows what the engine is doing");

        return changed;
    }

    private bool DrawInterface(ref Settings settings, float width)
    {
        UiControls.Heading("Interface", width);

        var changed = false;
        var uiScale = settings.UiScale;
        var overlay = settings.ShowOverlay;

        if (_controls.Slider("Interface scale", ref uiScale, UiTheme.MinimumScale, UiTheme.MaximumScale, width, $"{uiScale:F2}×"))
        {
            settings = settings with { UiScale = uiScale };
            changed = true;
        }

        if (_controls.Toggle("Debug overlay", ref overlay, width))
        {
            settings = settings with { ShowOverlay = overlay };
            changed = true;
        }

        UiControls.Note("F3 toggles it too");

        return changed;
    }

    private static string Minutes(float seconds) =>
        seconds <= 0f ? "held" : $"{seconds / 60f:F1} min";
}

/// <summary>What a menu was asked to do.</summary>
public enum MenuChoice
{
    /// <summary>Nothing was clicked.</summary>
    None,

    /// <summary>Start a fresh world from the seed the game was given.</summary>
    NewWorld,

    /// <summary>Load the world on disk.</summary>
    Continue,

    /// <summary>Go back to the world.</summary>
    Resume,

    /// <summary>Open the settings.</summary>
    Settings,

    /// <summary>Leave the settings for whichever screen opened them.</summary>
    Back,

    /// <summary>Write the world out and stay in it.</summary>
    Save,

    /// <summary>Write the world out and close the game.</summary>
    SaveAndQuit,

    /// <summary>Close the game without saving.</summary>
    Quit,
}
