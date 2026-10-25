using Brutal.ImGuiApi;
using BrutalShore.Ui;

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

        FullScreen("crosshair"u8, takesInput: false);

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

        UiCanvas.Chamfer(plateMinimum, maximum - new float2(scale, scale), UiPalette.Fade(UiPalette.Scrim, 0.82f), cut * 0.5f);
        UiCanvas.Text(
            new float2(plateMinimum.X + (4f * scale), plateMinimum.Y + (1.5f * scale)),
            UiPalette.Text,
            carried,
            countSize
        );
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

        var markSize = TitleSize * scale;
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
        var subtitleY = bandY + bandHeight + (18f * scale);
        var subtitleWidth = UiCanvas.Measure("A VOXEL WORLD ON BRUTAL", subtitleSize).X + (44f * scale);

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
            "A VOXEL WORLD ON BRUTAL",
            subtitleSize,
            2.2f * scale
        );

        var width = MenuWidth * scale;
        UiControls.BeginColumn(
            "titlemenu",
            new float2(left, subtitleY + (56f * scale)),
            new float2(width, UiControls.EntryHeight * scale * 5.2f)
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

    /// <summary>How wide one inventory slot is, before the interface scale.</summary>
    private const float SlotSize = 46f;

    /// <summary>How much space goes between two inventory slots, before the scale.</summary>
    private const float SlotGap = 6f;

    /// <summary>Draws the inventory, moving stacks about as the player clicks them.</summary>
    /// <param name="inventory">What the player is carrying, which this moves around in place.</param>
    /// <param name="bench">The crafting grid, which this lays blocks out on and takes from.</param>
    /// <param name="recipes">The table the recipe list is read from.</param>
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
    public void DrawInventory(Inventory inventory, Bench bench, Recipes recipes, ref Stack held)
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

        DrawBench(inventory, bench, recipes, ref held, new float2(left, benchTop), slot, gap, ref named);

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
    /// <param name="held">What the cursor is carrying, which this reads and writes.</param>
    /// <param name="origin">The top-left corner of the grid, in screen pixels.</param>
    /// <param name="slot">How wide and tall one slot is, in screen pixels.</param>
    /// <param name="gap">How much space goes between two slots, in screen pixels.</param>
    /// <param name="named">Set to the name of whatever the cursor is over.</param>
    private void DrawBench(
        Inventory inventory,
        Bench bench,
        Recipes recipes,
        ref Stack held,
        float2 origin,
        float slot,
        float gap,
        ref string named
    )
    {
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
                held = bench.Craft();
        }

        DrawRecipeList(
            inventory,
            bench,
            recipes,
            new float2(resultMaximum.X + (22f * scale), origin.Y),
            origin.X + ((SlotSize * scale) + gap) * Inventory.RowCount - gap - resultMaximum.X - (22f * scale),
            gridHeight,
            scale
        );
    }

    /// <summary>The column of recipes, with the ones that can be made now picked out.</summary>
    /// <param name="inventory">What the player is carrying, for colours and ingredients.</param>
    /// <param name="bench">The grid a clicked recipe is laid out on.</param>
    /// <param name="recipes">The table to list.</param>
    /// <param name="origin">The top-left corner of the list, in screen pixels.</param>
    /// <param name="width">How wide the list is, in pixels.</param>
    /// <param name="height">How tall it is, in pixels.</param>
    /// <param name="scale">The interface scale.</param>
    private void DrawRecipeList(
        Inventory inventory,
        Bench bench,
        Recipes recipes,
        float2 origin,
        float width,
        float height,
        float scale
    )
    {
        var size = ImGui.GetFontSize() * 0.82f;
        var rowHeight = height / recipes.All.Count;
        var swatch = float.Min(rowHeight - (5f * scale), 18f * scale);
        var mouse = ImGui.GetMousePos();

        for (var index = 0; index < recipes.All.Count; index++)
        {
            var recipe = recipes.All[index];
            var minimum = new float2(origin.X, origin.Y + (index * rowHeight));
            var maximum = new float2(origin.X + width, minimum.Y + rowHeight - (3f * scale));

            var affordable = CanMake(recipe, inventory);
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
                new float2(minimum.X + inset + swatch + (8f * scale), minimum.Y + ((rowHeight - size) * 0.5f) - (2f * scale)),
                affordable ? UiPalette.Text : UiPalette.Fade(UiPalette.MutedText, 0.55f),
                recipe.Name,
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

        var width = MenuWidth * scale;
        var height = 356f * scale;
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
            new float2(width, UiControls.EntryHeight * scale * 5.4f)
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
            new float2(left, cardMaximum.Y - padding - noteSize),
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
        changed = DrawSection(ref settings, left + rail, headingBottom, width - rail, top + height - headingBottom, scale);

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
