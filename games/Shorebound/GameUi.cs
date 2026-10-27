using Brutal.ImGuiApi;
using BrutalShore.Ui;
using System.Text;

namespace Shorebound;

/// <summary>What the player sees over the world: the crosshair, the row it carries, and the menus.</summary>
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

    /// <summary>How far a panel's corners are cut back, before the scale.</summary>
    /// <remarks>
    /// Larger than a control's cut, so that a panel reads as a thicker piece of the same stock
    /// rather than as a control that grew.
    /// </remarks>
    private const float PanelCut = 12f;

    private const ImGuiWindowFlags Bare =
        ImGuiWindowFlags.NoDecoration
        | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoSavedSettings
        | ImGuiWindowFlags.NoFocusOnAppearing
        | ImGuiWindowFlags.NoNav
        | ImGuiWindowFlags.NoBackground
        | ImGuiWindowFlags.NoScrollbar;

    /// <summary>
    /// The same, for the panels that are only ever looked at. They cover the whole viewport, so
    /// without this the crosshair would be sitting on top of every menu drawn under it, taking
    /// the clicks meant for them.
    /// </summary>
    private const ImGuiWindowFlags Passive = Bare | ImGuiWindowFlags.NoInputs;

    private readonly UiControls _controls = new();
    private SettingsSection _section = SettingsSection.World;
    private string? _renamingPath;
    private string _renameName = string.Empty;
    private string? _confirmDeletePath;
    private int _terminalFrame = -2;
    private int _terminalLines = -1;

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

    /// <summary>Draws the top menu bar while the pointer is available.</summary>
    /// <param name="worldAvailable">Whether a world has been entered.</param>
    /// <param name="overlayVisible">Whether the host's performance panel is shown.</param>
    /// <param name="overlayVisibility">A requested overlay state, or <see langword="null" />.</param>
    /// <returns>The selected action, if any.</returns>
    public MenuChoice DrawMenuBar(bool worldAvailable, bool overlayVisible, out bool? overlayVisibility)
    {
        overlayVisibility = null;
        var choice = MenuChoice.None;
        var scale = ImGui.GetStyle().FontScaleMain;

        // Roomier than a tool's menu bar, in both directions. The default is sized so that a
        // dozen menus fit across a small window, which is the right trade for an editor and the
        // wrong one here: there are five, and what they should look like is the trim along the
        // top of the machine rather than a row of words crammed into a strip.
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new float2(16f, 11f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new float2(14f, 9f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(10f, 10f) * scale);

        if (!ImGui.BeginMainMenuBar())
        {
            ImGui.PopStyleVar(3);
            return choice;
        }

        DrawMenuBarTrim(scale);
        DrawWordmark(scale);

        if (ImGui.BeginMenu("Game"u8))
        {
            if (ImGui.MenuItem("Main menu"u8, ""u8, false, worldAvailable))
                choice = MenuChoice.MainMenu;
            if (ImGui.MenuItem("Settings"u8))
                choice = MenuChoice.Settings;
            ImGui.Separator();
            if (ImGui.MenuItem("Quit"u8))
                choice = MenuChoice.Quit;
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("World"u8))
        {
            if (ImGui.MenuItem("Create world..."u8))
                choice = MenuChoice.CreateWorld;
            if (ImGui.MenuItem("Load world..."u8))
                choice = MenuChoice.LoadSaves;
            ImGui.Separator();
            if (ImGui.MenuItem("Save world"u8, "Ctrl+S"u8, false, worldAvailable))
                choice = MenuChoice.Save;
            if (ImGui.MenuItem("Save and return to main menu"u8, ""u8, false, worldAvailable))
                choice = MenuChoice.MainMenu;
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("View"u8))
        {
            if (ImGui.MenuItem("Terminal"u8, "T"u8, false, worldAvailable))
                choice = MenuChoice.Terminal;
            if (ImGui.MenuItem("Performance overlay"u8, "F3"u8, overlayVisible))
                overlayVisibility = !overlayVisible;
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Debug"u8, worldAvailable))
        {
            if (ImGui.MenuItem("Teleport to spawn"u8))
                choice = MenuChoice.TeleportToSpawn;
            if (ImGui.MenuItem("Restore health"u8))
                choice = MenuChoice.RestoreHealth;
            ImGui.Separator();
            if (ImGui.MenuItem("Toggle flight"u8))
                choice = MenuChoice.ToggleFlight;
            if (ImGui.MenuItem("Toggle time"u8))
                choice = MenuChoice.ToggleTime;
            if (ImGui.MenuItem("Set time to noon"u8))
                choice = MenuChoice.SetNoon;
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Help"u8))
        {
            ImGui.MenuItem("Move"u8, "WASD"u8, false, false);
            ImGui.MenuItem("Pause"u8, "Esc"u8, false, false);
            ImGui.MenuItem("Carrying"u8, "I"u8, false, false);
            ImGui.MenuItem("Terminal"u8, "T"u8, false, false);
            ImGui.MenuItem("Performance overlay"u8, "F3"u8, false, false);
            ImGui.EndMenu();
        }

        ImGui.EndMainMenuBar();
        ImGui.PopStyleVar(3);
        return choice;
    }

    /// <summary>The lit edge and hazard band that close the bottom of the menu bar.</summary>
    /// <remarks>
    /// Drawn inside the bar rather than under it so that it is clipped by the bar's own rectangle
    /// and lands over whatever the world is doing behind it. The same pair closes every panel in
    /// the game, which is what makes the bar read as part of the same machine.
    /// </remarks>
    private static void DrawMenuBarTrim(float scale)
    {
        var position = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var bottom = position.Y + size.Y;
        var band = 5f * scale;

        UiCanvas.Line(
            new float2(position.X, bottom - band - (1f * scale)),
            new float2(position.X + size.X, bottom - band - (1f * scale)),
            UiPalette.Fade(UiPalette.Edge, 0.55f),
            scale
        );

        var bandMinimum = new float2(position.X, bottom - band);
        var bandMaximum = new float2(position.X + size.X, bottom);
        UiCanvas.Rect(bandMinimum, bandMaximum, UiPalette.Recess);
        UiCanvas.HazardStripes(bandMinimum, bandMaximum, UiPalette.Fade(UiPalette.Accent, 0.32f), 16f * scale);
    }

    /// <summary>The game's name at the left end of the bar, which is not a menu.</summary>
    /// <remarks>
    /// A label rather than the first menu. It used to be one, and a menu called Shorebound that
    /// holds Quit is a menu whose name says nothing about what is inside it — the word is the
    /// game's mark, and a mark that opens a list is a mark nobody trusts to be only a mark.
    /// </remarks>
    private static void DrawWordmark(float scale)
    {
        var size = ImGui.GetFontSize() * 0.86f;
        var position = ImGui.GetCursorScreenPos();
        var middleY = position.Y + (ImGui.GetFrameHeight() * 0.5f);

        UiCanvas.Rect(
            new float2(position.X, middleY - (size * 0.55f)),
            new float2(position.X + (3f * scale), middleY + (size * 0.55f)),
            UiPalette.Accent
        );

        var width = UiCanvas.TrackedText(
            new float2(position.X + (11f * scale), middleY - (size * 0.5f)),
            UiPalette.Text,
            "SHOREBOUND",
            size,
            2.4f * scale
        );

        ImGui.Dummy(new float2(width + (24f * scale), ImGui.GetFrameHeight()));
        ImGui.SameLine();
    }

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

        FullScreen("crosshair"u8, takesInput: false);

        var scale = ImGui.GetStyle().FontScaleMain;
        var grown = aiming ? 1f : 0f;
        var gap = (4f + grown) * scale;
        var arm = (7f + (2f * grown)) * scale;
        var thickness = 1.6f * scale;
        var color = aiming ? UiPalette.Accent : UiPalette.Fade(UiPalette.Text, 0.78f);

        UiCanvas.Line(new float2(middle.X - gap - arm, middle.Y), new float2(middle.X - gap, middle.Y), color,
            thickness);
        UiCanvas.Line(new float2(middle.X + gap, middle.Y), new float2(middle.X + gap + arm, middle.Y), color,
            thickness);
        UiCanvas.Line(new float2(middle.X, middle.Y - gap - arm), new float2(middle.X, middle.Y - gap), color,
            thickness);
        UiCanvas.Line(new float2(middle.X, middle.Y + gap), new float2(middle.X, middle.Y + gap + arm), color,
            thickness);
        UiCanvas.Circle(middle, 1.2f * scale, color);

        ImGui.End();
    }

    /// <summary>Draws one inventory slot, wherever it is: in the row or in the pack.</summary>
    /// <param name="minimum">Its top left corner, in screen pixels.</param>
    /// <param name="maximum">Its bottom right corner.</param>
    /// <param name="swatch">The colour of what is in it, or nothing when it is empty.</param>
    /// <param name="count">How many are in it.</param>
    /// <param name="selected">Whether it is the one in hand.</param>
    /// <param name="over">Whether the pointer is on it.</param>
    /// <param name="scale">The interface scale.</param>
    /// <remarks>
    /// One function for both so that the row along the bottom of the screen and the grid behind
    /// it are visibly the same object in two places. A slot drawn twice is a slot that stops
    /// agreeing with itself after the second change to either.
    /// </remarks>
    private static void Slot(
        float2 minimum,
        float2 maximum,
        float4? swatch,
        int count,
        bool selected,
        bool over,
        float scale
    )
    {
        var cut = 7f * scale;

        // Cut into the panel: dark fill under a lit top hairline. The swatch then sits in the
        // recess rather than on the panel, which is what makes a full slot read as holding
        // something instead of being painted a colour.
        var empty = swatch is null;
        UiCanvas.Chamfer(
            minimum,
            maximum,
            over ? UiPalette.Hovered : UiPalette.Fade(UiPalette.Recess, empty ? 0.72f : 1f),
            cut
        );

        if (swatch is { } color)
        {
            var inset = 6f * scale;
            UiCanvas.Chamfer(
                minimum + new float2(inset, inset),
                maximum - new float2(inset, inset),
                color,
                cut * 0.6f
            );
        }

        UiCanvas.Line(
            new float2(minimum.X + cut, minimum.Y + (0.5f * scale)),
            new float2(maximum.X - cut, minimum.Y + (0.5f * scale)),
            UiPalette.Fade(UiPalette.Edge, over ? 0.8f : 0.4f),
            scale
        );

        UiCanvas.ChamferOutline(
            minimum,
            maximum,
            selected ? UiPalette.Accent : UiPalette.Fade(UiPalette.Divider, over ? 1f : 0.7f),
            cut,
            selected ? 2f * scale : scale
        );

        if (selected)
            UiCanvas.CornerTicks(minimum, maximum, UiPalette.Accent, 10f * scale, 2f * scale);

        if (count <= 0)
            return;

        // The count on a plate of its own in the corner, because half the blocks in this game
        // are pale and a number straight onto the swatch disappears into them.
        var countSize = ImGui.GetFontSize() * 0.8f;
        var carried = $"{count}";
        var countWidth = UiCanvas.Measure(carried, countSize).X;
        var plateMinimum = new float2(
            maximum.X - countWidth - (8f * scale),
            maximum.Y - countSize - (5f * scale)
        );

        UiCanvas.Chamfer(plateMinimum, maximum - new float2(scale, scale), UiPalette.Fade(UiPalette.Scrim, 0.82f),
            cut * 0.5f);
        UiCanvas.Text(
            new float2(plateMinimum.X + (4f * scale), plateMinimum.Y + (1.5f * scale)),
            UiPalette.Text,
            carried,
            countSize
        );
    }

    /// <summary>Draws how much of the player is left, over on the left of the row.</summary>
    /// <param name="vitality">What is left of them.</param>
    /// <remarks>
    /// <para>
    /// Hidden while whole. A bar that is always there is a bar nobody looks at, and in a game
    /// where most hours are spent digging rather than fighting, a health bar appearing at all is
    /// itself the warning.
    /// </para>
    /// <para>
    /// Cut into segments rather than drawn as one strip, because a player under attack is
    /// counting how many more bites they can take and not reading a percentage.
    /// </para>
    /// </remarks>
    public void DrawVitality(Vitality vitality)
    {
        var fraction = vitality.Fraction;
        if (fraction >= 1f && !vitality.WasHurtRecently)
            return;

        var viewport = ImGui.GetMainViewport();
        var scale = ImGui.GetStyle().FontScaleMain;

        FullScreen("vitality"u8, takesInput: false);

        const int Segments = 10;
        var segment = 22f * scale;
        var gap = 3f * scale;
        var height = 11f * scale;
        var width = (segment * Segments) + (gap * (Segments - 1));

        var left = viewport.Pos.X + ((viewport.Size.X - width) * 0.5f);
        // Above the name of what is in hand, which sits above the row. Clearing it by hand
        // rather than by luck: the two were drawn on top of each other first time round.
        var bottom = viewport.Pos.Y + viewport.Size.Y - (134f * scale);

        // Red as it runs out rather than red from the start: the colour is the reading.
        var low = fraction < 0.35f;
        var filled = fraction * Segments;

        for (var index = 0; index < Segments; index++)
        {
            var minimum = new float2(left + (index * (segment + gap)), bottom - height);
            var maximum = minimum + new float2(segment, height);
            var part = float.Clamp(filled - index, 0f, 1f);

            UiCanvas.Chamfer(minimum, maximum, UiPalette.Fade(UiPalette.Scrim, 0.78f), 3f * scale);

            if (part > 0f)
            {
                UiCanvas.Chamfer(
                    minimum,
                    new float2(float.Lerp(minimum.X, maximum.X, part), maximum.Y),
                    low ? UiPalette.Warning : UiPalette.Accent,
                    3f * scale
                );
            }

            UiCanvas.ChamferOutline(minimum, maximum, UiPalette.Fade(UiPalette.Edge, 0.45f), 3f * scale);
        }

        ImGui.End();
    }

    /// <summary>Draws the row of blocks along the bottom of the screen.</summary>
    /// <param name="inventory">The row to draw.</param>
    /// <remarks>
    /// A row of swatches rather than a row of words. The thing a player is choosing between is
    /// blocks, and a block is a colour in this game — so the colour is the label, the count is
    /// in the corner where every game of this kind puts it, and the name appears once, over
    /// whichever one is in hand.
    /// </remarks>
    public void DrawRow(Inventory inventory)
    {
        var viewport = ImGui.GetMainViewport();
        var scale = ImGui.GetStyle().FontScaleMain;

        var slot = 46f * scale;
        var gap = 6f * scale;
        var totalWidth = (slot * Inventory.RowCount) + (gap * (Inventory.RowCount - 1));
        var left = viewport.Pos.X + ((viewport.Size.X - totalWidth) * 0.5f);
        var bottom = viewport.Pos.Y + viewport.Size.Y - (34f * scale);
        var top = bottom - slot;

        FullScreen("row"u8, takesInput: false);

        var size = ImGui.GetFontSize();
        var name = inventory.SelectedName.ToUpperInvariant();
        var nameSize = size * 0.9f;
        var nameWidth = UiCanvas.Measure(name, nameSize).X + ((name.Length - 1) * 1.6f * scale);
        var nameLeft = viewport.Pos.X + ((viewport.Size.X - nameWidth) * 0.5f);
        var nameTop = top - nameSize - (22f * scale);

        UiCanvas.Chamfer(
            new float2(nameLeft - (16f * scale), nameTop - (6f * scale)),
            new float2(nameLeft + nameWidth + (16f * scale), nameTop + nameSize + (6f * scale)),
            UiPalette.Fade(UiPalette.Scrim, 0.7f),
            5f * scale
        );
        UiCanvas.TrackedText(
            new float2(nameLeft, nameTop),
            inventory.HasSelection ? UiPalette.Text : UiPalette.MutedText,
            name,
            nameSize,
            1.6f * scale
        );

        for (var index = 0; index < Inventory.RowCount; index++)
        {
            var chosen = index == inventory.SelectedIndex;
            var count = inventory.At(index).Count;
            var x = left + (index * (slot + gap));
            var lift = chosen ? 5f * scale : 0f;
            var minimum = new float2(x, top - lift);
            var maximum = new float2(x + slot, bottom - lift);

            Slot(
                minimum,
                maximum,
                count > 0 ? inventory.ColorOf(index) : null,
                count,
                chosen,
                over: false,
                scale
            );

            // Under the slot rather than inside it, where the count already is, and in the accent
            // for the one in hand so the row can be read without the label above it.
            var number = $"{index + 1}";
            var numberSize = size * 0.76f;
            var numberWidth = UiCanvas.Measure(number, numberSize).X;
            UiCanvas.Text(
                new float2(x + ((slot - numberWidth) * 0.5f), bottom + (7f * scale)),
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

        var availableWidth = float.Max(120f * scale, viewport.Size.X - (left - viewport.Pos.X) - (48f * scale));
        var nominalMarkSize = TitleSize * scale;
        var nominalMarkWidth =
            UiCanvas.Measure("SHOREBOUND", nominalMarkSize).X + (TitleTracking * scale * ("SHOREBOUND".Length - 1));
        var markSize = nominalMarkSize * float.Min(1f, availableWidth / nominalMarkWidth);
        var markHeight = UiCanvas.Measure("SHOREBOUND", markSize).Y;
        var markWidth = UiCanvas.TrackedText(
            new float2(left, top),
            UiPalette.Text,
            "SHOREBOUND",
            markSize,
            TitleTracking * scale
        );

        // Measured rather than assumed: a font's line box is taller than its capitals, and a band
        // placed at the nominal size floats away from the word it belongs to.
        var bandY = top + markHeight + (10f * scale);
        var bandHeight = 9f * scale;
        var bandMinimum = new float2(left, bandY);
        var bandMaximum = new float2(left + markWidth, bandY + bandHeight);
        UiCanvas.Rect(bandMinimum, bandMaximum, UiPalette.Recess);
        UiCanvas.HazardStripes(bandMinimum, bandMaximum, UiPalette.Accent, 13f * scale);
        UiCanvas.Outline(bandMinimum, bandMaximum, UiPalette.Fade(UiPalette.Edge, 0.5f));

        var subtitleSize = ImGui.GetFontSize() * 0.86f;
        var subtitleText = "A VOXEL WORLD ON BRUTAL";
        var subtitleTracking = 2.2f * scale;
        var subtitleTextWidth =
            UiCanvas.Measure(subtitleText, subtitleSize).X + (subtitleTracking * (subtitleText.Length - 1));
        if (subtitleTextWidth > availableWidth - (28f * scale))
            subtitleSize *= (availableWidth - (28f * scale)) / subtitleTextWidth;
        var subtitleY = bandY + bandHeight + (18f * scale);
        var subtitleWidth = subtitleTextWidth + (28f * scale);

        // The subtitle on a plate of its own, because a line of small capitals floating over a
        // field is the one thing on this screen with nothing holding it down.
        UiCanvas.Chamfer(
            new float2(left, subtitleY - (7f * scale)),
            new float2(left + subtitleWidth, subtitleY + subtitleSize + (7f * scale)),
            UiPalette.Fade(UiPalette.Control, 0.85f),
            5f * scale
        );
        UiCanvas.ChamferOutline(
            new float2(left, subtitleY - (7f * scale)),
            new float2(left + subtitleWidth, subtitleY + subtitleSize + (7f * scale)),
            UiPalette.Fade(UiPalette.Edge, 0.6f),
            5f * scale
        );
        UiCanvas.TrackedText(
            new float2(left + (14f * scale), subtitleY),
            UiPalette.Text,
            subtitleText,
            subtitleSize,
            subtitleTracking
        );

        var width = float.Min(MenuWidth * scale, availableWidth);
        UiControls.BeginColumn(
            "titlemenu",
            new float2(left, subtitleY + (56f * scale)),
            new float2(width, UiControls.EntryHeight * scale * 5.2f)
        );

        if (hasSave)
        {
            if (_controls.Entry("Load worlds", width, emphasis: true))
                choice = MenuChoice.LoadSaves;
            if (_controls.Entry("New world", width))
                choice = MenuChoice.CreateWorld;
        }
        else if (_controls.Entry("Play", width, emphasis: true))
        {
            choice = MenuChoice.CreateWorld;
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
            FitText(footer, availableWidth, footerSize),
            footerSize
        );
        UiCanvas.Text(
            new float2(left, viewport.Pos.Y + viewport.Size.Y - (18f * scale)),
            UiPalette.MutedText,
            FitText("Block textures: Faithful Resource Pack (faithfulpack.net).", availableWidth, footerSize),
            footerSize
        );

        ImGui.End();
        return choice;
    }

    /// <summary>Draws the saved-world list and reports a load or file-management action.</summary>
    /// <param name="savePaths">The world files found beside the configured save path.</param>
    /// <param name="selectedPath">The currently selected file, if it is still present.</param>
    /// <returns>The action the game should perform.</returns>
    internal SaveBrowserChoice DrawSaveBrowser(IReadOnlyList<string> savePaths, string? selectedPath)
    {
        var viewport = ImGui.GetMainViewport();
        var scale = ImGui.GetStyle().FontScaleMain;
        var width = float.Min(700f * scale, viewport.Size.X - (40f * scale));
        var height = float.Min(560f * scale, viewport.Size.Y - (40f * scale));
        var left = viewport.Pos.X + ((viewport.Size.X - width) * 0.5f);
        var top = viewport.Pos.Y + ((viewport.Size.Y - height) * 0.5f);
        var minimum = new float2(left, top);
        var maximum = minimum + new float2(width, height);
        var inset = 24f * scale;
        var choice = new SaveBrowserChoice(SaveBrowserAction.None);

        FullScreen("savebrowser"u8);
        UiCanvas.Scrim(1f);
        UiCanvas.Panel(minimum, maximum, PanelCut * scale, scale);
        var listTop = UiCanvas.PanelHeading(minimum, width, "Saved worlds", scale) + (14f * scale);
        var listHeight = float.Max(96f * scale, height - (268f * scale));
        var listWidth = width - (inset * 2f);
        var rowHeight = UiControls.EntryHeight * scale;

        ImGui.SetCursorScreenPos(new float2(left + inset, listTop));
        ImGui.BeginChild("save-list"u8, new float2(listWidth, listHeight), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.None);
        if (savePaths.Count == 0)
        {
            UiCanvas.Text(
                ImGui.GetCursorScreenPos() + new float2(12f * scale, 12f * scale),
                UiPalette.MutedText,
                "No saves found in this folder.",
                ImGui.GetFontSize() * 0.88f
            );
        }
        else
        {
            for (var index = 0; index < savePaths.Count; index++)
            {
                var path = savePaths[index];
                var name = Path.GetFileNameWithoutExtension(path);
                var origin = ImGui.GetCursorScreenPos();
                ImGui.InvisibleButton($"save:{index}", new float2(listWidth - (12f * scale), rowHeight));
                var hovered = ImGui.IsItemHovered();
                if (ImGui.IsItemClicked())
                    choice = new SaveBrowserChoice(SaveBrowserAction.Select, path);

                var selected = string.Equals(selectedPath, path, StringComparison.OrdinalIgnoreCase);
                UiCanvas.Chamfer(
                    origin,
                    origin + new float2(listWidth - (12f * scale), rowHeight - (3f * scale)),
                    selected ? UiPalette.Fade(UiPalette.Accent, 0.8f) : hovered ? UiPalette.Hovered : UiPalette.Control,
                    UiControls.Cut * scale
                );
                UiCanvas.Text(
                    origin + new float2(14f * scale, (rowHeight - ImGui.GetFontSize()) * 0.5f),
                    selected ? UiPalette.Text : UiPalette.MutedText,
                    FitText(name, listWidth - (44f * scale), ImGui.GetFontSize()),
                    ImGui.GetFontSize()
                );
            }
        }

        ImGui.EndChild();

        var actionTop = listTop + listHeight + (14f * scale);
        if (_renamingPath is { } renamingPath)
        {
            Span<byte> nameBuffer = stackalloc byte[256];
            nameBuffer.Clear();
            var byteCount = Encoding.UTF8.GetBytes(_renameName, nameBuffer);
            UiCanvas.Text(
                new float2(left + inset, actionTop + (5f * scale)),
                UiPalette.MutedText,
                "New name",
                ImGui.GetFontSize() * 0.8f
            );
            ImGui.SetCursorScreenPos(new float2(left + inset, actionTop + (20f * scale)));
            ImGui.SetNextItemWidth(width - (inset * 2f));
            _ = ImGui.InputText("save-name"u8, nameBuffer);
            var nullIndex = nameBuffer.IndexOf((byte)0);
            _renameName = Encoding.UTF8.GetString(nameBuffer[..(nullIndex < 0 ? byteCount : nullIndex)]);
            actionTop += 58f * scale;
            if (SaveActionButton("Confirm rename", left + inset, actionTop, (width - (inset * 2f)) * 0.58f, scale))
                choice = new SaveBrowserChoice(SaveBrowserAction.Rename, renamingPath, _renameName);
            if (SaveActionButton("Cancel", left + inset + ((width - (inset * 2f)) * 0.62f), actionTop,
                    (width - (inset * 2f)) * 0.38f, scale))
                _renamingPath = null;
        }
        else if (_confirmDeletePath is { } deletingPath)
        {
            UiCanvas.Text(
                new float2(left + inset, actionTop + (5f * scale)),
                UiPalette.Warning,
                FitText($"Delete {Path.GetFileNameWithoutExtension(deletingPath)}?", width - (inset * 2f),
                    ImGui.GetFontSize() * 0.88f),
                ImGui.GetFontSize() * 0.88f
            );
            actionTop += 38f * scale;
            if (SaveActionButton("Confirm delete", left + inset, actionTop, (width - (inset * 2f)) * 0.58f, scale))
            {
                choice = new SaveBrowserChoice(SaveBrowserAction.Delete, deletingPath);
                _confirmDeletePath = null;
            }

            if (SaveActionButton("Cancel", left + inset + ((width - (inset * 2f)) * 0.62f), actionTop,
                    (width - (inset * 2f)) * 0.38f, scale))
                _confirmDeletePath = null;
        }
        else if (selectedPath is not null && savePaths.Contains(selectedPath, StringComparer.OrdinalIgnoreCase))
        {
            var buttonWidth = (width - (inset * 2f) - (12f * scale)) / 3f;
            if (SaveActionButton("Load", left + inset, actionTop, buttonWidth, scale, emphasis: true))
                choice = new SaveBrowserChoice(SaveBrowserAction.Load, selectedPath);
            if (SaveActionButton("Rename", left + inset + buttonWidth + (6f * scale), actionTop, buttonWidth, scale))
            {
                _renamingPath = selectedPath;
                _renameName = Path.GetFileNameWithoutExtension(selectedPath);
            }

            if (SaveActionButton("Delete", left + inset + ((buttonWidth + (6f * scale)) * 2f), actionTop, buttonWidth,
                    scale))
                _confirmDeletePath = selectedPath;
        }

        var backTop = maximum.Y - inset - (UiControls.EntryHeight * scale);
        if (SaveActionButton("Back", left + inset, backTop, width - (inset * 2f), scale))
        {
            choice = new SaveBrowserChoice(SaveBrowserAction.Back);
            _renamingPath = null;
            _confirmDeletePath = null;
        }

        ImGui.End();
        return choice;
    }

    /// <summary>How wide one inventory slot is, before the interface scale.</summary>
    private const float SlotSize = 46f;

    /// <summary>How much space goes between two inventory slots, before the scale.</summary>
    private const float SlotGap = 6f;

    /// <summary>Draws the inventory, moving stacks about as the player clicks them.</summary>
    /// <param name="inventory">What the player is carrying, which this moves around in place.</param>
    /// <param name="bench">The crafting grid, which this lays blocks out on and takes from.</param>
    /// <param name="recipes">The table the recipe list is read from.</param>
    /// <param name="progression">How far the player has got, which decides what the list offers.</param>
    /// <returns>Whether the player made something this frame.</returns>
    /// <param name="held">
    /// What the cursor is carrying between two slots, which this reads and writes. It lives
    /// outside the inventory because a stack on the cursor is in no slot at all, and lives
    /// outside this method because it has to survive the frames between picking it up and
    /// putting it down.
    /// </param>
    /// <remarks>
    /// <para>
    /// Hit-tested by hand against <see cref="ImGui.GetMousePos" /> rather than built out of
    /// buttons. A grid of thirty-six squares laid out by a widget library is a grid whose
    /// geometry is the library's; here the geometry is wanted in two places at once — the
    /// rectangle a slot is drawn in is the rectangle it is clicked in — and computing it once is
    /// shorter than persuading something else to agree with it.
    /// </para>
    /// <para>
    /// Left click takes or puts a whole stack and right click takes half or puts one, which is
    /// the pair of gestures everyone who has played a game of this kind already knows. The
    /// decisions about what that means to a slot are <see cref="Inventory" />'s, not this
    /// method's: it reports which slot was clicked and with which button, and nothing else.
    /// </para>
    /// </remarks>
    public bool DrawInventory(
        Inventory inventory,
        Bench bench,
        Recipes recipes,
        Progression progression,
        ref Stack held
    )
    {
        var viewport = ImGui.GetMainViewport();
        var scale = ImGui.GetStyle().FontScaleMain;

        FullScreen("inventory"u8);
        UiCanvas.Scrim(1f);

        var slot = SlotSize * scale;
        var gap = SlotGap * scale;
        var padding = 28f * scale;
        var pitch = slot + gap;

        var width = (slot * Inventory.RowCount) + (gap * (Inventory.RowCount - 1));
        var headingSize = ImGui.GetFontSize() * 1.05f;
        var packRows = (Inventory.SlotCount - Inventory.RowCount) / Inventory.RowCount;

        var benchHeight = (Recipes.GridHeight * pitch) - gap;
        var packHeight = (packRows * pitch) - gap;

        // Bench, a rule, the pack, a gap wide enough to read as a division, then the row in hand.
        var height =
            headingSize + (30f * scale)
                        + benchHeight + (30f * scale)
                        + packHeight + (24f * scale)
                        + slot + (26f * scale);

        var left = viewport.Pos.X + ((viewport.Size.X - width) * 0.5f);
        var top = viewport.Pos.Y + ((viewport.Size.Y - height) * 0.5f);

        var cardMinimum = new float2(left - padding, top - padding);
        var cardMaximum = new float2(left + width + padding, top + height + padding);
        UiCanvas.Panel(cardMinimum, cardMaximum, PanelCut * scale, scale);
        UiCanvas.PanelHeading(cardMinimum, cardMaximum.X - cardMinimum.X, "Carrying", scale);

        var named = string.Empty;
        var benchTop = top + headingSize + (30f * scale);

        var made = DrawBench(inventory, bench, recipes, progression, ref held, new float2(left, benchTop), slot, gap,
            ref named);

        var ruleY = benchTop + benchHeight + (15f * scale);
        UiCanvas.Line(new float2(left, ruleY), new float2(left + width, ruleY), UiPalette.Divider);

        var packTop = ruleY + (15f * scale);
        DrawSlots(
            inventory.Store,
            inventory,
            ref held,
            Inventory.RowCount,
            Inventory.SlotCount,
            Inventory.RowCount,
            new float2(left, packTop),
            slot,
            gap,
            selected: -1,
            ref named
        );

        var rowTop = packTop + packHeight + (24f * scale);
        DrawSlots(
            inventory.Store,
            inventory,
            ref held,
            0,
            Inventory.RowCount,
            Inventory.RowCount,
            new float2(left, rowTop),
            slot,
            gap,
            inventory.SelectedIndex,
            ref named
        );

        // The name of whatever is under the cursor, in the space the division already left. A
        // tooltip would cover the slot it is describing, which in a grid this tight is the slot
        // next to the one being aimed at.
        var footSize = ImGui.GetFontSize() * 0.84f;
        UiCanvas.Text(
            new float2(left, top + height - footSize),
            named.Length > 0 ? UiPalette.Text : UiPalette.MutedText,
            named.Length > 0 ? named : "I, tab or escape to close",
            footSize
        );

        DrawHeld(inventory, held, slot, scale);

        ImGui.End();
        return made;
    }

    /// <summary>Draws the crafting grid, what it makes, and the list of what could be made.</summary>
    /// <remarks>
    /// <para>
    /// The list is the half that matters. A shaped recipe a player has not been told is a recipe
    /// they will never find, and a game that answers that with a wiki has given up; a column of
    /// what is currently possible, with the ones they cannot afford greyed rather than hidden,
    /// turns the same table into something to read and to aim at.
    /// </para>
    /// <para>
    /// Clicking a row lays the recipe out rather than making it. The difference is the whole
    /// reason the grid is still there: the shortcut saves the remembering, and the grid keeps the
    /// making a thing the player does and can see before they do it.
    /// </para>
    /// </remarks>
    /// <param name="inventory">What the player is carrying, for colours, names and ingredients.</param>
    /// <param name="bench">The grid, which this draws and acts on.</param>
    /// <param name="recipes">The table the list is read from.</param>
    /// <param name="progression">How far the player has got.</param>
    /// <param name="held">What the cursor is carrying, which this reads and writes.</param>
    /// <param name="origin">The top-left corner of the grid, in screen pixels.</param>
    /// <param name="slot">How wide and tall one slot is, in screen pixels.</param>
    /// <param name="gap">How much space goes between two slots, in screen pixels.</param>
    /// <param name="named">Set to the name of whatever the cursor is over.</param>
    /// <returns>Whether the player made something this frame.</returns>
    private bool DrawBench(
        Inventory inventory,
        Bench bench,
        Recipes recipes,
        Progression progression,
        ref Stack held,
        float2 origin,
        float slot,
        float gap,
        ref string named
    )
    {
        var crafted = false;
        var scale = ImGui.GetStyle().FontScaleMain;
        var pitch = slot + gap;
        var gridWidth = (Recipes.GridWidth * pitch) - gap;
        var gridHeight = (Recipes.GridHeight * pitch) - gap;

        DrawSlots(
            bench.Grid,
            inventory,
            ref held,
            0,
            Recipes.GridSize,
            Recipes.GridWidth,
            origin,
            slot,
            gap,
            selected: -1,
            ref named
        );

        // The arrow, which is the only thing on the screen that says the grid on its left becomes
        // the slot on its right.
        var made = bench.Result;
        var arrowLeft = origin.X + gridWidth + (16f * scale);
        var arrowRight = arrowLeft + (34f * scale);
        var arrowY = origin.Y + (gridHeight * 0.5f);
        var arrowColor = made.IsNothing ? UiPalette.Fade(UiPalette.MutedText, 0.4f) : UiPalette.Accent;

        UiCanvas.Line(new float2(arrowLeft, arrowY), new float2(arrowRight, arrowY), arrowColor, 2f * scale);
        UiCanvas.Line(
            new float2(arrowRight - (10f * scale), arrowY - (7f * scale)),
            new float2(arrowRight, arrowY),
            arrowColor,
            2f * scale
        );
        UiCanvas.Line(
            new float2(arrowRight - (10f * scale), arrowY + (7f * scale)),
            new float2(arrowRight, arrowY),
            arrowColor,
            2f * scale
        );

        var resultSize = slot * 1.3f;
        var resultMinimum = new float2(arrowRight + (16f * scale), arrowY - (resultSize * 0.5f));
        var resultMaximum = resultMinimum + new float2(resultSize, resultSize);

        var mouse = ImGui.GetMousePos();
        var overResult =
            mouse.X >= resultMinimum.X
            && mouse.X < resultMaximum.X
            && mouse.Y >= resultMinimum.Y
            && mouse.Y < resultMaximum.Y;

        Slot(
            resultMinimum,
            resultMaximum,
            made.IsNothing ? null : inventory.ColorOf(made.Block),
            made.Count,
            selected: !made.IsNothing,
            overResult,
            scale
        );

        if (overResult && !made.IsNothing)
        {
            named = $"{inventory.NameOf(made.Block)}  ×{made.Count}";

            // Crafting puts the result on the cursor rather than into the inventory, which is how
            // a player sees that something happened. It is also the only way a full inventory can
            // refuse a craft without losing what it made.
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && held.IsNothing)
            {
                held = bench.Craft();
                crafted = !held.IsNothing;
            }
        }

        DrawRecipeList(
            inventory,
            bench,
            recipes,
            progression,
            new float2(resultMaximum.X + (22f * scale), origin.Y),
            origin.X + ((SlotSize * scale) + gap) * Inventory.RowCount - gap - resultMaximum.X - (22f * scale),
            gridHeight,
            scale
        );

        return crafted;
    }

    /// <summary>The column of recipes, with the ones that can be made now picked out.</summary>
    /// <param name="inventory">What the player is carrying, for colours and ingredients.</param>
    /// <param name="bench">The grid a clicked recipe is laid out on.</param>
    /// <param name="recipes">The table to list.</param>
    /// <param name="progression">How far the player has got, which decides what is listed.</param>
    /// <param name="origin">The top-left corner of the list, in screen pixels.</param>
    /// <param name="width">How wide the list is, in pixels.</param>
    /// <param name="height">How tall it is, in pixels.</param>
    /// <param name="scale">The interface scale.</param>
    private void DrawRecipeList(
        Inventory inventory,
        Bench bench,
        Recipes recipes,
        Progression progression,
        float2 origin,
        float width,
        float height,
        float scale
    )
    {
        var size = ImGui.GetFontSize() * 0.82f;

        // Locked recipes are listed, not hidden. What is coming is half of why the next milestone
        // is worth paying for, and a list that grew without warning would never do that job.
        var listed = recipes.All;
        var rowHeight = height / listed.Count;
        var swatch = float.Min(rowHeight - (5f * scale), 18f * scale);
        var mouse = ImGui.GetMousePos();

        for (var index = 0; index < listed.Count; index++)
        {
            var recipe = listed[index];
            var unlocked = progression.IsUnlocked(recipe.Name);
            var minimum = new float2(origin.X, origin.Y + (index * rowHeight));
            var maximum = new float2(origin.X + width, minimum.Y + rowHeight - (3f * scale));

            var affordable = unlocked && CanMake(recipe, inventory);
            var over =
                mouse.X >= minimum.X && mouse.X < maximum.X && mouse.Y >= minimum.Y && mouse.Y < maximum.Y;

            UiCanvas.Chamfer(
                minimum,
                maximum,
                over && affordable ? UiPalette.Hovered : UiPalette.Fade(UiPalette.Recess, 0.7f),
                4f * scale
            );

            var inset = (rowHeight - (3f * scale) - swatch) * 0.5f;
            UiCanvas.Chamfer(
                new float2(minimum.X + inset, minimum.Y + inset),
                new float2(minimum.X + inset + swatch, minimum.Y + inset + swatch),
                UiPalette.Fade(inventory.ColorOf(recipe.Result), affordable ? 1f : 0.3f),
                3f * scale
            );

            UiCanvas.Text(
                new float2(minimum.X + inset + swatch + (8f * scale),
                    minimum.Y + ((rowHeight - size) * 0.5f) - (2f * scale)),
                affordable ? UiPalette.Text : UiPalette.Fade(UiPalette.MutedText, unlocked ? 0.55f : 0.32f),
                unlocked ? recipe.Name : "locked",
                size
            );

            if (affordable)
            {
                UiCanvas.ChamferOutline(
                    minimum,
                    maximum,
                    UiPalette.Fade(UiPalette.Accent, over ? 0.9f : 0.3f),
                    4f * scale
                );
            }

            if (over && affordable && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                bench.Fill(recipe, inventory);
        }
    }

    /// <summary>Whether what is carried covers a recipe, counting the grid as spent.</summary>
    private static bool CanMake(Recipe recipe, Inventory inventory)
    {
        foreach (var block in recipe.Pattern.Where(block => !block.IsAir).Distinct())
        {
            if (inventory.CountOf(block) < recipe.Pattern.Count(wanted => wanted == block))
                return false;
        }

        return true;
    }

    /// <summary>Draws the tree of milestones, and a way to pay for any step that is open.</summary>
    /// <param name="progression">How far the player has got, which this hands in against.</param>
    /// <param name="inventory">What they are carrying, for whether a step is affordable.</param>
    /// <returns>The step the player paid for this frame, or nothing.</returns>
    /// <remarks>
    /// <para>
    /// The whole tree at once, with what is done behind and what is coming ahead. A progression
    /// shown one step at a time is a progression a player cannot plan against, and planning two
    /// steps ahead — mining copper knowing what the iron is for — is most of what makes the
    /// middle of this game worth playing.
    /// </para>
    /// <para>
    /// Every open step carries its own button, because more than one of them is open for most of
    /// the game and the whole point of that is choosing which. A locked step says which steps it
    /// is waiting on, so the tree can be read backwards from whatever the player wants.
    /// </para>
    /// <para>
    /// The story line of a step is shown whether it is done or not. Reading it before is a reason
    /// to go and do it; reading it after is the only reward a milestone gives that is not an
    /// unlock.
    /// </para>
    /// </remarks>
    public Milestone? DrawMilestones(Progression progression, Inventory inventory)
    {
        var viewport = ImGui.GetMainViewport();
        var scale = ImGui.GetStyle().FontScaleMain;
        Milestone? handedIn = null;

        FullScreen("milestones"u8);
        UiCanvas.Scrim(1f);

        var width = float.Min(880f * scale, viewport.Size.X - (80f * scale));
        var headingSize = ImGui.GetFontSize() * 1.05f;

        // The rows shrink to fit rather than the panel growing past the window. A tree with one
        // more step in it must not push the first one off the top of the screen, which is what
        // a fixed row height does the moment the table outgrows the display it was sized for.
        var around = headingSize + (90f * scale);
        var rowHeight = float.Min(
            104f * scale,
            float.Max(68f * scale, (viewport.Size.Y - (64f * scale) - around) / progression.All.Count)
        );

        var height = around + (progression.All.Count * rowHeight);

        var left = viewport.Pos.X + ((viewport.Size.X - width) * 0.5f);
        var top = viewport.Pos.Y + ((viewport.Size.Y - height) * 0.5f);

        UiCanvas.Panel(new float2(left, top), new float2(left + width, top + height), PanelCut * scale, scale);
        var listTop = UiCanvas.PanelHeading(new float2(left, top), width, "The shore asks", scale) + (16f * scale);

        var inset = 22f * scale;
        var size = ImGui.GetFontSize() * 0.86f;
        var smallSize = ImGui.GetFontSize() * 0.78f;
        var buttonWidth = 200f * scale;

        for (var index = 0; index < progression.All.Count; index++)
        {
            var milestone = progression.All[index];
            var done = progression.IsDone(milestone.Name);
            var open = progression.IsOpen(milestone);

            var rowTop = listTop + (index * rowHeight);
            var minimum = new float2(left + inset, rowTop);
            var maximum = new float2(left + width - inset, rowTop + rowHeight - (10f * scale));

            UiCanvas.Chamfer(
                minimum,
                maximum,
                open ? UiPalette.Control : UiPalette.Fade(UiPalette.Recess, done ? 0.75f : 0.5f),
                7f * scale
            );

            // The marker down the left edge says which of the three states this row is in without
            // a word: filled for done, accent for what is open, dark for what is still locked.
            UiCanvas.Chamfer(
                minimum,
                new float2(minimum.X + (5f * scale), maximum.Y),
                done ? UiPalette.Fade(UiPalette.Accent, 0.5f) : open ? UiPalette.Accent : UiPalette.Divider,
                3f * scale
            );

            if (open)
                UiCanvas.ChamferOutline(minimum, maximum, UiPalette.Fade(UiPalette.Accent, 0.7f), 7f * scale, scale);

            var textLeft = minimum.X + (20f * scale);
            var textWidth = width - (inset * 2f) - (40f * scale) - buttonWidth;

            UiCanvas.TrackedText(
                new float2(textLeft, rowTop + (12f * scale)),
                done ? UiPalette.Fade(UiPalette.Text, 0.6f) : open ? UiPalette.Accent : UiPalette.MutedText,
                milestone.Name.ToUpperInvariant(),
                size,
                2f * scale
            );

            UiCanvas.Text(
                new float2(textLeft, rowTop + (12f * scale) + size + (8f * scale)),
                UiPalette.Fade(UiPalette.MutedText, done ? 0.7f : 1f),
                Wrapped(milestone.Story, textWidth, smallSize),
                smallSize
            );

            // What the row says along the bottom is the one thing the player can act on: what it
            // costs while it is open, what it is waiting for while it is not.
            var waiting = progression.Waiting(milestone).ToArray();
            var costText = done ? "done"
                : waiting.Length > 0 ? "after " + string.Join(" and ", waiting)
                : string.Join("   ", milestone.Cost.Select(part => $"{inventory.NameOf(part.Block)} ×{part.Count}"));

            UiCanvas.Text(
                new float2(textLeft, maximum.Y - smallSize - (10f * scale)),
                done ? UiPalette.Fade(UiPalette.Accent, 0.6f)
                : waiting.Length > 0 ? UiPalette.MutedText
                : UiPalette.Text,
                costText,
                smallSize
            );

            if (!open)
                continue;

            var buttonLeft = maximum.X - (16f * scale) - buttonWidth;
            var buttonTop = rowTop + ((rowHeight - (10f * scale) - (UiControls.EntryHeight * scale * 1.2f)) * 0.5f);

            if (!progression.CanAfford(milestone, inventory))
            {
                UiCanvas.Text(
                    new float2(
                        buttonLeft + buttonWidth - UiCanvas.Measure("not enough yet", smallSize).X,
                        buttonTop + (14f * scale)
                    ),
                    UiPalette.MutedText,
                    "not enough yet",
                    smallSize
                );

                continue;
            }

            // One column per row rather than one for the panel, because the rows are drawn on the
            // canvas at fixed places and a shared column would put every button in the same one.
            UiControls.BeginColumn(
                $"handin{index}",
                new float2(buttonLeft, buttonTop),
                new float2(buttonWidth, UiControls.EntryHeight * scale * 1.2f)
            );

            if (_controls.Entry("Hand it in", buttonWidth, emphasis: true))
                handedIn = progression.HandIn(milestone, inventory);

            UiControls.EndColumn();
        }

        if (progression.IsFinished)
        {
            UiCanvas.Text(
                new float2(left + inset, listTop + (progression.All.Count * rowHeight) + (6f * scale)),
                UiPalette.Accent,
                "The shore has nothing else to ask of you.",
                size
            );
        }

        var footSize = ImGui.GetFontSize() * 0.82f;
        UiCanvas.Text(
            new float2(left + width - inset - UiCanvas.Measure("M or escape to close", footSize).X,
                top + height - footSize - (16f * scale)),
            UiPalette.MutedText,
            "M or escape to close",
            footSize
        );

        ImGui.End();
        return handedIn;
    }

    /// <summary>Breaks a line of prose onto as many lines as it needs.</summary>
    /// <remarks>
    /// Measured word by word rather than counted by characters, because the font is proportional
    /// and a column counted in characters is a column that is ragged on one side and overflowing
    /// on the other.
    /// </remarks>
    private static string Wrapped(string text, float width, float size)
    {
        var lines = new List<string>();
        var line = string.Empty;

        foreach (var word in text.Split(' '))
        {
            var candidate = line.Length == 0 ? word : $"{line} {word}";
            if (line.Length > 0 && UiCanvas.Measure(candidate, size).X > width)
            {
                lines.Add(line);
                line = word;
                continue;
            }

            line = candidate;
        }

        if (line.Length > 0)
            lines.Add(line);

        return string.Join('\n', lines);
    }

    /// <summary>Draws a smelter: what goes in, what it burns, and what has come out.</summary>
    /// <param name="smelter">The machine standing in the world, which this moves stacks in.</param>
    /// <param name="smelting">The tables, for how far along the current block is.</param>
    /// <param name="inventory">What the player is carrying, drawn under it and swapped with.</param>
    /// <param name="held">What the cursor is carrying, which this reads and writes.</param>
    /// <remarks>
    /// <para>
    /// The player's own slots are on the same screen, because every single thing anyone does at a
    /// smelter is move something between the two. A screen that showed only the machine would
    /// make feeding it a matter of closing one panel and opening another.
    /// </para>
    /// <para>
    /// Two gauges rather than numbers: how much fuel is left and how far through the block it is.
    /// A machine's whole job is to be understood at a glance from across a room, and seconds
    /// remaining is not something anyone reads at a glance.
    /// </para>
    /// </remarks>
    public void DrawSmelter(Smelter smelter, Smelting smelting, Inventory inventory, ref Stack held)
    {
        var viewport = ImGui.GetMainViewport();
        var scale = ImGui.GetStyle().FontScaleMain;

        FullScreen("smelter"u8);
        UiCanvas.Scrim(1f);

        var slot = SlotSize * scale;
        var gap = SlotGap * scale;
        var padding = 28f * scale;
        var pitch = slot + gap;

        var width = (slot * Inventory.RowCount) + (gap * (Inventory.RowCount - 1));
        var headingSize = ImGui.GetFontSize() * 1.05f;
        var packRows = (Inventory.SlotCount - Inventory.RowCount) / Inventory.RowCount;
        var packHeight = (packRows * pitch) - gap;
        var machineHeight = (slot * 2.6f) + gap;

        var height =
            headingSize + (30f * scale)
                        + machineHeight + (30f * scale)
                        + packHeight + (24f * scale)
                        + slot + (26f * scale);

        var left = viewport.Pos.X + ((viewport.Size.X - width) * 0.5f);
        var top = viewport.Pos.Y + ((viewport.Size.Y - height) * 0.5f);

        var cardMinimum = new float2(left - padding, top - padding);
        var cardMaximum = new float2(left + width + padding, top + height + padding);
        UiCanvas.Panel(cardMinimum, cardMaximum, PanelCut * scale, scale);
        UiCanvas.PanelHeading(cardMinimum, cardMaximum.X - cardMinimum.X, "Smelter", scale);

        var named = string.Empty;
        var machineTop = top + headingSize + (30f * scale);

        DrawFurnace(smelter, smelting, inventory, ref held, new float2(left, machineTop), slot, gap, ref named);

        var ruleY = machineTop + machineHeight + (15f * scale);
        UiCanvas.Line(new float2(left, ruleY), new float2(left + width, ruleY), UiPalette.Divider);

        var packTop = ruleY + (15f * scale);
        DrawSlots(
            inventory.Store,
            inventory,
            ref held,
            Inventory.RowCount,
            Inventory.SlotCount,
            Inventory.RowCount,
            new float2(left, packTop),
            slot,
            gap,
            selected: -1,
            ref named
        );

        var rowTop = packTop + packHeight + (24f * scale);
        DrawSlots(
            inventory.Store,
            inventory,
            ref held,
            0,
            Inventory.RowCount,
            Inventory.RowCount,
            new float2(left, rowTop),
            slot,
            gap,
            inventory.SelectedIndex,
            ref named
        );

        var footSize = ImGui.GetFontSize() * 0.84f;
        UiCanvas.Text(
            new float2(left, top + height - footSize),
            named.Length > 0 ? UiPalette.Text : UiPalette.MutedText,
            named.Length > 0 ? named : "escape to close",
            footSize
        );

        DrawHeld(inventory, held, slot, scale);

        ImGui.End();
    }

    /// <summary>The machine half of the smelter screen: three slots and two gauges.</summary>
    private static void DrawFurnace(
        Smelter smelter,
        Smelting smelting,
        Inventory inventory,
        ref Stack held,
        float2 origin,
        float slot,
        float gap,
        ref string named
    )
    {
        var scale = ImGui.GetStyle().FontScaleMain;
        var size = ImGui.GetFontSize() * 0.8f;
        var column = origin.X;

        // Laid out the way the machine works, left to right: what goes in above what burns it,
        // then the arrow, then what came out.
        DrawLabelled(smelter.Input, inventory, ref held, new float2(column, origin.Y), slot, "IN", ref named);

        var fuelTop = origin.Y + (slot * 1.6f);
        DrawLabelled(smelter.Fuel, inventory, ref held, new float2(column, fuelTop), slot, "FUEL", ref named);

        // The fire gauge climbs rather than falls, because a bar that empties downwards next to a
        // bar that fills rightwards is two different conventions on one panel.
        var fireLeft = column + slot + (14f * scale);
        var fireWidth = 12f * scale;
        var fireBottom = fuelTop + slot;
        var fireTop = fuelTop;
        var burning = smelter.BurnTotalSeconds > 0f
            ? float.Clamp(smelter.BurnLeftSeconds / smelter.BurnTotalSeconds, 0f, 1f)
            : 0f;

        UiCanvas.Chamfer(
            new float2(fireLeft, fireTop),
            new float2(fireLeft + fireWidth, fireBottom),
            UiPalette.Recess,
            3f * scale
        );
        if (burning > 0f)
        {
            UiCanvas.Chamfer(
                new float2(fireLeft, float.Lerp(fireBottom, fireTop, burning)),
                new float2(fireLeft + fireWidth, fireBottom),
                UiPalette.Accent,
                3f * scale
            );
        }

        UiCanvas.ChamferOutline(
            new float2(fireLeft, fireTop),
            new float2(fireLeft + fireWidth, fireBottom),
            UiPalette.Fade(UiPalette.Edge, 0.6f),
            3f * scale,
            scale
        );

        var workLeft = fireLeft + fireWidth + (18f * scale);
        var workWidth = slot * 2.2f;
        var workMiddle = origin.Y + (slot * 1.3f);
        var workHeight = 14f * scale;
        var progress = smelter.Progress(smelting);

        UiCanvas.Chamfer(
            new float2(workLeft, workMiddle - (workHeight * 0.5f)),
            new float2(workLeft + workWidth, workMiddle + (workHeight * 0.5f)),
            UiPalette.Recess,
            4f * scale
        );
        if (progress > 0f)
        {
            UiCanvas.Chamfer(
                new float2(workLeft, workMiddle - (workHeight * 0.5f)),
                new float2(workLeft + (workWidth * progress), workMiddle + (workHeight * 0.5f)),
                UiPalette.Accent,
                4f * scale
            );
        }

        UiCanvas.ChamferOutline(
            new float2(workLeft, workMiddle - (workHeight * 0.5f)),
            new float2(workLeft + workWidth, workMiddle + (workHeight * 0.5f)),
            UiPalette.Fade(UiPalette.Edge, 0.6f),
            4f * scale,
            scale
        );

        UiCanvas.TrackedText(
            new float2(workLeft, workMiddle - (workHeight * 0.5f) - size - (6f * scale)),
            smelter.IsLit ? UiPalette.Accent : UiPalette.MutedText,
            smelter.IsLit ? "RUNNING" : "IDLE",
            size,
            1.6f * scale
        );

        var outLeft = workLeft + workWidth + (20f * scale);
        var outSize = slot * 1.3f;
        DrawLabelled(
            smelter.Output,
            inventory,
            ref held,
            new float2(outLeft, origin.Y + (slot * 0.5f)),
            outSize,
            "OUT",
            ref named
        );

        DrawMelts(
            smelting,
            inventory,
            new float2(outLeft + outSize + (26f * scale), origin.Y - (size * 0.5f)),
            (slot * 2.6f) + gap
        );
    }

    /// <summary>What a smelter will take, listed beside it.</summary>
    /// <remarks>
    /// The same answer the recipe list gives on the crafting screen, for the same reason: a
    /// machine that silently does nothing with the wrong block in it is a machine a player has to
    /// guess at. Three lines of "this becomes that" removes the guessing entirely.
    /// </remarks>
    private static void DrawMelts(Smelting smelting, Inventory inventory, float2 origin, float height)
    {
        var scale = ImGui.GetStyle().FontScaleMain;
        var size = ImGui.GetFontSize() * 0.78f;
        var melts = smelting.All.ToList();
        var rowHeight = height / (melts.Count + 1);
        var swatch = float.Min(rowHeight - (6f * scale), 16f * scale);

        UiCanvas.TrackedText(origin, UiPalette.MutedText, "MELTS", size, 1.6f * scale);

        for (var index = 0; index < melts.Count; index++)
        {
            var (input, result, seconds) = melts[index];
            var top = origin.Y + ((index + 1) * rowHeight);
            var middle = top + (rowHeight * 0.5f);

            UiCanvas.Chamfer(
                new float2(origin.X, middle - (swatch * 0.5f)),
                new float2(origin.X + swatch, middle + (swatch * 0.5f)),
                inventory.ColorOf(input),
                3f * scale
            );

            var arrowLeft = origin.X + swatch + (8f * scale);
            var arrowRight = arrowLeft + (16f * scale);
            UiCanvas.Line(
                new float2(arrowLeft, middle),
                new float2(arrowRight, middle),
                UiPalette.Fade(UiPalette.MutedText, 0.8f),
                scale
            );

            UiCanvas.Chamfer(
                new float2(arrowRight + (8f * scale), middle - (swatch * 0.5f)),
                new float2(arrowRight + (8f * scale) + swatch, middle + (swatch * 0.5f)),
                inventory.ColorOf(result),
                3f * scale
            );

            UiCanvas.Text(
                new float2(arrowRight + swatch + (18f * scale), middle - (size * 0.5f)),
                UiPalette.Fade(UiPalette.Text, 0.85f),
                $"{inventory.NameOf(result)}  {seconds:0}s",
                size
            );
        }
    }

    /// <summary>One machine slot with a word over it.</summary>
    private static void DrawLabelled(
        Slots slots,
        Inventory inventory,
        ref Stack held,
        float2 origin,
        float size,
        string label,
        ref string named
    )
    {
        var scale = ImGui.GetStyle().FontScaleMain;
        var textSize = ImGui.GetFontSize() * 0.74f;

        UiCanvas.TrackedText(
            new float2(origin.X, origin.Y - textSize - (5f * scale)),
            UiPalette.MutedText,
            label,
            textSize,
            1.4f * scale
        );

        DrawSlots(slots, inventory, ref held, 0, 1, 1, origin, size, 0f, selected: -1, ref named);
    }

    /// <summary>Draws one block of slots and acts on a click in it.</summary>
    /// <param name="slots">The slots to draw, which this moves stacks around in.</param>
    /// <param name="lookup">Where a block's name and colour come from.</param>
    /// <param name="held">What the cursor is carrying, which this reads and writes.</param>
    /// <param name="first">The first slot to draw.</param>
    /// <param name="end">One past the last slot to draw.</param>
    /// <param name="perRow">How many slots go in a row before it wraps.</param>
    /// <param name="origin">The top-left corner of the block, in screen pixels.</param>
    /// <param name="slot">How wide and tall one slot is, in screen pixels.</param>
    /// <param name="gap">How much space goes between two slots, in screen pixels.</param>
    /// <param name="selected">Which slot wears the accent, or -1 for none.</param>
    /// <param name="named">
    /// Set to the name of whatever the cursor is over, so that the caller can write it somewhere
    /// that is not on top of the grid.
    /// </param>
    private static void DrawSlots(
        Slots slots,
        Inventory lookup,
        ref Stack held,
        int first,
        int end,
        int perRow,
        float2 origin,
        float slot,
        float gap,
        int selected,
        ref string named
    )
    {
        var mouse = ImGui.GetMousePos();
        var scale = ImGui.GetStyle().FontScaleMain;
        var pitch = slot + gap;

        for (var index = first; index < end; index++)
        {
            var place = index - first;
            var minimum = origin + new float2((place % perRow) * pitch, (place / perRow) * pitch);
            var maximum = minimum + new float2(slot, slot);

            var over =
                mouse.X >= minimum.X && mouse.X < maximum.X && mouse.Y >= minimum.Y && mouse.Y < maximum.Y;

            var stack = slots.At(index);
            if (over && !stack.IsNothing)
                named = $"{lookup.NameOf(stack.Block)}  ×{stack.Count}";

            Slot(
                minimum,
                maximum,
                stack.IsNothing ? null : lookup.ColorOf(stack.Block),
                stack.Count,
                index == selected,
                over,
                scale
            );

            if (!over)
                continue;

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                held = held.IsNothing ? slots.Take(index) : slots.Put(index, held);
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                held = held.IsNothing ? slots.TakeHalf(index) : slots.PutOne(index, held);
        }
    }

    /// <summary>Draws whatever the cursor is carrying, under the cursor.</summary>
    private static void DrawHeld(Inventory inventory, Stack held, float slot, float scale)
    {
        if (held.IsNothing)
            return;

        var mouse = ImGui.GetMousePos();
        var size = slot * 0.68f;
        var minimum = mouse - new float2(size * 0.5f, size * 0.5f);
        var maximum = minimum + new float2(size, size);

        UiCanvas.Chamfer(minimum, maximum, inventory.ColorOf(held.Block), 4f * scale);
        UiCanvas.ChamferOutline(minimum, maximum, UiPalette.Fade(UiPalette.Scrim, 0.8f), 4f * scale, 2f * scale);

        var carried = $"{held.Count}";
        var countSize = ImGui.GetFontSize() * 0.82f;
        var countWidth = UiCanvas.Measure(carried, countSize).X;
        UiCanvas.Text(
            new float2(maximum.X - countWidth, maximum.Y - countSize),
            UiPalette.Text,
            carried,
            countSize
        );
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

        var width = float.Min(MenuWidth * scale, viewport.Size.X - (80f * scale));
        var height = 350f * scale;
        var padding = 26f * scale;
        var left = viewport.Pos.X + ((viewport.Size.X - width) * 0.5f);
        var top = viewport.Pos.Y + ((viewport.Size.Y - height) * 0.5f);

        // A panel, rather than text floating over the world. A pause menu has to look like the
        // game stopped, and nothing on a transparent background ever does.
        var cardMinimum = new float2(left - padding, top - padding);
        var cardMaximum = new float2(left + width + padding, top + height + padding);
        UiCanvas.Panel(cardMinimum, cardMaximum, PanelCut * scale, scale);

        var headingBottom = UiCanvas.PanelHeading(
            cardMinimum,
            cardMaximum.X - cardMinimum.X,
            "Paused",
            scale
        );

        var entriesTop = headingBottom + (24f * scale);
        UiControls.BeginColumn(
            "pausemenu",
            new float2(left, entriesTop),
            new float2(width, UiControls.EntryHeight * scale * 6.4f)
        );

        if (_controls.Entry("Resume", width, emphasis: true))
            choice = MenuChoice.Resume;

        if (_controls.Entry("Settings", width))
            choice = MenuChoice.Settings;

        if (_controls.Entry("Save world", width))
            choice = MenuChoice.Save;

        if (_controls.Entry("Save and quit", width))
            choice = MenuChoice.SaveAndQuit;

        if (_controls.Entry("Save and return to main menu", width))
            choice = MenuChoice.MainMenu;

        UiControls.EndColumn();

        var noteSize = ImGui.GetFontSize() * 0.84f;
        UiCanvas.Text(
            new float2(left, cardMaximum.Y - padding - noteSize),
            UiPalette.MutedText,
            FitText(savePath, width, noteSize),
            noteSize
        );

        ImGui.End();
        return choice;
    }

    /// <summary>Draws the new-world form and reports whether to create it or cancel.</summary>
    /// <param name="name">The player-facing world and save name.</param>
    /// <param name="seed">The seed used to generate the world.</param>
    /// <param name="viewRadiusChunks">How far chunks are kept loaded.</param>
    /// <param name="dayLengthSeconds">How long a day takes, or zero to hold the sun.</param>
    /// <returns>The action the game should perform.</returns>
    public MenuChoice DrawWorldCreation(
        ref string name,
        ref int seed,
        ref int viewRadiusChunks,
        ref float dayLengthSeconds
    )
    {
        var scale = ImGui.GetStyle().FontScaleMain;
        var viewport = ImGui.GetMainViewport();
        var size = new float2(520f * scale, 390f * scale);
        ImGui.SetNextWindowPos(viewport.Pos + ((viewport.Size - size) * 0.5f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);

        if (!ImGui.Begin("Create a world"u8, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove))
        {
            ImGui.End();
            return MenuChoice.None;
        }

        ImGui.TextWrapped("Choose a name and generation settings before the world is created."u8);
        ImGui.Separator();
        ImGui.SetNextItemWidth(-1f);
        TextInput("World name"u8, ref name);
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputInt("Seed"u8, ref seed);
        ImGui.SetNextItemWidth(-1f);
        ImGui.SliderInt("View distance"u8, ref viewRadiusChunks, 2, 24, "%d chunks"u8);
        ImGui.SetNextItemWidth(-1f);
        ImGui.SliderFloat("Day length"u8, ref dayLengthSeconds, 0f, 3600f, "%.0f seconds"u8);
        ImGui.TextDisabled(dayLengthSeconds <= 0f
            ? "The sun will remain fixed."u8
            : "Seconds from sunrise to sunrise."u8);

        var choice = MenuChoice.None;
        if (ImGui.Button("Create world"u8, new float2(250f * scale, 0f)))
            choice = MenuChoice.StartWorld;
        ImGui.SameLine();
        if (ImGui.Button("Cancel"u8, new float2(170f * scale, 0f)))
            choice = MenuChoice.Back;

        ImGui.End();
        return choice;
    }

    /// <summary>Draws the in-game console and reports a submitted line.</summary>
    /// <param name="command">The command being edited.</param>
    /// <param name="output">Recent command results to display.</param>
    /// <returns><see langword="true" /> when the player submits the command.</returns>
    /// <remarks>
    /// <para>
    /// Pulled down from the top edge of the screen rather than floating in the middle of it. A
    /// draggable window with a title bar is a tool's console: it can be moved off the screen, it
    /// can be resized until the output is two lines tall, and it looks like the debug overlay it
    /// is not. A console that is always the same shape in the same place is one the hand finds
    /// without the eye, which is the whole of what makes a command prompt quick to use.
    /// </para>
    /// <para>
    /// The output is monospaced because half of what this prints is columns — coordinates, a
    /// seed, counts of what is loaded — and a column set in a proportional face is not a column.
    /// </para>
    /// </remarks>
    public bool DrawTerminal(ref string command, IReadOnlyList<string> output)
    {
        var viewport = ImGui.GetMainViewport();
        var scale = ImGui.GetStyle().FontScaleMain;

        // Whether this is the frame the console came up on, which is when the caret has to be put
        // in the prompt. ImGui counts frames and this is drawn only while the console is open, so
        // a gap in the count is an opening.
        var frame = ImGui.GetFrameCount();
        var opening = frame != _terminalFrame + 1;
        _terminalFrame = frame;

        FullScreen("terminal"u8);
        UiCanvas.Scrim(0.5f);

        var height = float.Round(viewport.Size.Y * 0.5f);
        var minimum = viewport.Pos;
        var maximum = viewport.Pos + new float2(viewport.Size.X, height);
        var margin = 18f * scale;
        var rowHeight = float.Round((ImGui.GetFontSize() * 1.1f) + (14f * scale));

        // No chamfer and no shadow: three of its four edges are the screen's own, and a corner
        // cut against the edge of the display is a corner cut out of nothing.
        UiCanvas.Rect(minimum, maximum, UiPalette.Panel);

        var headingBottom = UiCanvas.PanelHeading(minimum, viewport.Size.X, "Terminal", scale);

        // The keys, at the far end of the heading strip. In the strip rather than under the
        // prompt because the prompt is where the player is looking while typing, and a hint
        // under the caret is a hint the text being typed runs into.
        var hintSize = ImGui.GetFontSize() * 0.84f;
        const string Hint = "ENTER RUNS    ESC CLOSES";
        UiCanvas.TrackedText(
            new float2(
                maximum.X - UiCanvas.MeasureTracked(Hint, hintSize, 1.6f * scale) - (18f * scale),
                minimum.Y + ((headingBottom - (8f * scale) - minimum.Y - hintSize) * 0.5f)
            ),
            UiPalette.MutedText,
            Hint,
            hintSize,
            1.6f * scale
        );

        var outputTop = headingBottom + margin;
        var outputBottom = maximum.Y - margin - rowHeight - (8f * scale);
        var outputLeft = minimum.X + margin;
        var outputRight = maximum.X - margin;

        UiCanvas.Rect(
            new float2(outputLeft, outputTop),
            new float2(outputRight, outputBottom),
            UiPalette.Recess
        );

        DrawTerminalOutput(
            output,
            new float2(outputLeft, outputTop),
            new float2(outputRight, outputBottom),
            scale
        );

        var submitted = DrawTerminalPrompt(
            ref command,
            new float2(outputLeft, outputBottom + (8f * scale)),
            new float2(outputRight, outputBottom + (8f * scale) + rowHeight),
            opening,
            scale
        );

        // The closing edge, which is the only one the console has: a lit hairline over a hazard
        // band, the same pair that closes every panel in this game.
        var band = 8f * scale;
        UiCanvas.Line(
            new float2(minimum.X, maximum.Y - band),
            new float2(maximum.X, maximum.Y - band),
            UiPalette.Fade(UiPalette.Edge, 0.7f),
            scale
        );
        UiCanvas.Rect(new float2(minimum.X, maximum.Y - band), maximum, UiPalette.Recess);
        UiCanvas.HazardStripes(
            new float2(minimum.X, maximum.Y - band),
            maximum,
            UiPalette.Fade(UiPalette.Accent, 0.38f),
            18f * scale
        );

        ImGui.End();
        return submitted;
    }

    /// <summary>Draws the console's scrollback, newest at the bottom.</summary>
    private void DrawTerminalOutput(IReadOnlyList<string> output, float2 minimum, float2 maximum, float scale)
    {
        var inset = 12f * scale;
        UiControls.BeginColumn(
            "terminal-output",
            minimum + new float2(inset, inset * 0.5f),
            maximum - minimum - new float2(inset * 2f, inset)
        );

        UiFont.PushMonospace(ImGui.GetFontSize() * 0.95f);

        foreach (var line in output)
        {
            // An echoed command wears the accent and everything the game answered with does not,
            // which is what lets a long scrollback be read as a conversation rather than a wall.
            var echoed = line.StartsWith('>');
            ImGui.PushStyleColor(ImGuiCol.Text, echoed ? UiPalette.Accent : UiPalette.Text);
            ImGui.TextWrapped(line);
            ImGui.PopStyleColor();
        }

        UiFont.Pop();

        // Pinned to the bottom while new lines are arriving, and left alone once the player has
        // scrolled up to read something: a console that yanks itself back down mid-read is worse
        // than one that never follows at all.
        if (output.Count != _terminalLines)
        {
            ImGui.SetScrollHereY(1f);
            _terminalLines = output.Count;
        }

        UiControls.EndColumn();
    }

    /// <summary>Draws the prompt line and reports whether a command was entered.</summary>
    private static bool DrawTerminalPrompt(
        ref string command,
        float2 minimum,
        float2 maximum,
        bool opening,
        float scale
    )
    {
        var height = maximum.Y - minimum.Y;
        var caretWidth = 26f * scale;

        UiCanvas.Rect(minimum, maximum, UiPalette.Recess);
        UiCanvas.Line(
            new float2(minimum.X, minimum.Y + (0.5f * scale)),
            new float2(maximum.X, minimum.Y + (0.5f * scale)),
            UiPalette.Fade(UiPalette.Edge, 0.5f),
            scale
        );

        var caretSize = ImGui.GetFontSize();
        UiCanvas.Text(
            new float2(minimum.X + (12f * scale), minimum.Y + ((height - caretSize) * 0.5f)),
            UiPalette.Accent,
            ">",
            caretSize
        );

        ImGui.SetCursorScreenPos(new float2(minimum.X + caretWidth, minimum.Y + ((height - ImGui.GetFrameHeight()) * 0.5f)));
        ImGui.SetNextItemWidth(maximum.X - minimum.X - caretWidth - (12f * scale));

        // The field itself is invisible: the recess behind it is already the box, and a second
        // box drawn inside the first is what every tool interface looks like.
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiPalette.Fade(UiPalette.Recess, 0f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, UiPalette.Fade(UiPalette.Recess, 0f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, UiPalette.Fade(UiPalette.Recess, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        UiFont.PushMonospace();

        if (opening)
            ImGui.SetKeyboardFocusHere();

        Span<byte> commandBuffer = stackalloc byte[1024];
        commandBuffer.Clear();
        var encoded = Encoding.UTF8.GetBytes(command, commandBuffer);
        commandBuffer[encoded] = 0;

        var submitted = ImGui.InputText(
            "##terminal-command"u8,
            commandBuffer,
            ImGuiInputTextFlags.EnterReturnsTrue
        );

        UiFont.Pop();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        var length = commandBuffer.IndexOf((byte)0);
        command = Encoding.UTF8.GetString(commandBuffer[..(length < 0 ? commandBuffer.Length : length)]);

        return submitted;
    }

    private static void TextInput(ImString label, ref string value)
    {
        Span<byte> buffer = stackalloc byte[1024];
        buffer.Clear();
        var encoded = Encoding.UTF8.GetBytes(value, buffer);
        buffer[encoded] = 0;
        if (!ImGui.InputText(label, buffer))
            return;

        var length = buffer.IndexOf((byte)0);
        value = Encoding.UTF8.GetString(buffer[..(length < 0 ? buffer.Length : length)]);
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

        UiCanvas.Panel(new float2(left, top), new float2(left + width, top + height), PanelCut * scale, scale);
        var headingBottom = UiCanvas.PanelHeading(new float2(left, top), width, "Settings", scale);

        // The rail gets its own darker field, which is what tells a glance that the left column
        // chooses and the right column changes.
        UiCanvas.Rect(
            new float2(left + scale, headingBottom),
            new float2(left + rail, top + height - scale),
            UiPalette.Fade(UiPalette.Scrim, 0.5f)
        );
        UiCanvas.Line(
            new float2(left + rail, headingBottom),
            new float2(left + rail, top + height),
            UiPalette.Divider
        );

        DrawRail(left, headingBottom, rail, top + height - headingBottom, scale);
        changed = DrawSection(ref settings, left + rail, headingBottom, width - rail, top + height - headingBottom,
            scale);

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

        FullScreen("notice"u8, takesInput: false);

        var maxTextWidth = float.Max(80f * scale, viewport.Size.X - (64f * scale));
        message = FitText(message, maxTextWidth, size);
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
    private static void FullScreen(ReadOnlySpan<byte> name, bool takesInput = true)
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.Begin(name, takesInput ? Bare : Passive);
    }

    private void DrawRail(float left, float top, float rail, float height, float scale)
    {
        UiControls.BeginColumn(
            "rail",
            new float2(left + (10f * scale), top + (18f * scale)),
            new float2(rail - (20f * scale), height - (36f * scale))
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
            new float2(left + (28f * scale), top + (16f * scale)),
            new float2(inner, height - (86f * scale))
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

        var reflections = settings.Reflections;
        if (_controls.Toggle("Water reflections", ref reflections, width))
        {
            settings = settings with { Reflections = reflections };
            changed = true;
        }

        UiControls.Note("Every pixel of water sends a ray through the world; this is the dear one");

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
        UiControls.Note("Ctrl crouches: slower, lower, and it will not walk off an edge");
        UiControls.Note("A ledge one block high is walked up rather than jumped");
        UiControls.Note("F flies, space and ctrl go up and down while flying");
        UiControls.Note("Hold left click to break, right click to place");
        UiControls.Note("1 to 9, Q and E, or the mouse wheel pick what is in hand");
        UiControls.Note("Escape pauses, T opens the terminal, and F3 toggles the performance overlay");

        return changed;
    }

    private bool DrawInterface(ref Settings settings, float width)
    {
        UiControls.Heading("Interface", width);

        var changed = false;
        var uiScale = settings.UiScale;
        var overlay = settings.ShowOverlay;
        var volume = settings.Volume;

        if (_controls.Slider("Interface scale", ref uiScale, UiTheme.MinimumScale, UiTheme.MaximumScale, width,
                $"{uiScale:F2}×"))
        {
            settings = settings with { UiScale = uiScale };
            changed = true;
        }

        if (_controls.Slider("Volume", ref volume, 0f, 1f, width, $"{volume * 100f:F0}%"))
        {
            settings = settings with { Volume = volume };
            changed = true;
        }

        UiControls.Note("Every sound here is made out of arithmetic when the world opens.");

        if (_controls.Toggle("Debug overlay", ref overlay, width))
        {
            settings = settings with { ShowOverlay = overlay };
            changed = true;
        }

        UiControls.Note("F3 or View > Performance overlay toggles it");

        return changed;
    }

    private static string Minutes(float seconds) =>
        seconds <= 0f ? "held" : $"{seconds / 60f:F1} min";

    private static bool SaveActionButton(string label, float left, float top, float width, float scale,
        bool emphasis = false)
    {
        var height = UiControls.EntryHeight * scale;
        ImGui.SetCursorScreenPos(new float2(left, top));
        ImGui.InvisibleButton($"save-action:{label}", new float2(width, height));
        var hovered = ImGui.IsItemHovered();
        var pressed = ImGui.IsItemClicked();
        var minimum = new float2(left, top);
        var maximum = minimum + new float2(width, height);
        var color = emphasis
            ? hovered ? UiPalette.AccentHovered : UiPalette.Accent
            : hovered
                ? UiPalette.Hovered
                : UiPalette.Control;
        UiCanvas.Chamfer(minimum, maximum, color, UiControls.Cut * scale);
        UiCanvas.Text(
            minimum + new float2(12f * scale, (height - ImGui.GetFontSize()) * 0.5f),
            emphasis ? UiPalette.Scrim : UiPalette.Text,
            FitText(label, width - (24f * scale), ImGui.GetFontSize()),
            ImGui.GetFontSize()
        );
        return pressed;
    }

    private static string FitText(string text, float width, float size)
    {
        if (UiCanvas.Measure(text, size).X <= width)
            return text;

        const string Ellipsis = "...";
        var length = text.Length;
        while (length > 0 && UiCanvas.Measure(text[..length] + Ellipsis, size).X > width)
            length--;
        return length == 0 ? Ellipsis : text[..length] + Ellipsis;
    }
}

/// <summary>What a menu was asked to do.</summary>
public enum MenuChoice
{
    /// <summary>Nothing was clicked.</summary>
    None,

    /// <summary>Open the world creation form.</summary>
    CreateWorld,

    /// <summary>Create the world currently described by the world creation form.</summary>
    StartWorld,

    /// <summary>Load the world on disk.</summary>
    Continue,

    /// <summary>Open the saved-world browser.</summary>
    LoadSaves,

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

    /// <summary>Save the active world and return to the title screen.</summary>
    MainMenu,

    /// <summary>Open the in-game terminal.</summary>
    Terminal,

    /// <summary>Teleport to the surface above the origin.</summary>
    TeleportToSpawn,

    /// <summary>Restore the player's health.</summary>
    RestoreHealth,

    /// <summary>Turn flight on or off.</summary>
    ToggleFlight,

    /// <summary>Stop or resume the day clock.</summary>
    ToggleTime,

    /// <summary>Set the day clock to noon.</summary>
    SetNoon,
}

internal enum SaveBrowserAction
{
    None,
    Select,
    Load,
    Rename,
    Delete,
    Back,
}

internal readonly record struct SaveBrowserChoice(
    SaveBrowserAction Action,
    string? Path = null,
    string? Name = null
);