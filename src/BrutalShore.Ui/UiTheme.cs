using Brutal.ImGuiApi;

namespace BrutalShore.Ui;

/// <summary>Applies the game's look to Dear ImGui, once, for every panel anything draws.</summary>
/// <remarks>
/// <para>
/// Dear ImGui keeps one style per context, so a theme is a thing you set rather than a thing you
/// wrap. That is the whole reason this is a few lines in one place instead of a widget library:
/// the engine's debug overlay and the game's menus are already drawn through the same context,
/// and styling the context styles both.
/// </para>
/// <para>
/// The sizes are stated at a scale of one and multiplied afterwards, so a player on a dense
/// display moves one slider rather than finding that the padding scaled and the rounding did not.
/// </para>
/// </remarks>
public static class UiTheme
{
    /// <summary>The smallest interface scale worth offering.</summary>
    public const float MinimumScale = 0.75f;

    /// <summary>The largest, beyond which a panel stops fitting on a small window.</summary>
    public const float MaximumScale = 2f;

    /// <summary>Sets colours, spacing and rounding on the current ImGui context.</summary>
    /// <param name="scale">
    /// How large the interface is drawn, 1 being the designed size. Clamped to the range between
    /// <see cref="MinimumScale" /> and <see cref="MaximumScale" />.
    /// </param>
    /// <remarks>
    /// Safe to call again with a different scale: every size is set from the constants below
    /// rather than multiplied in place, so scaling twice does not compound.
    /// </remarks>
    public static void Apply(float scale)
    {
        scale = float.Clamp(scale, MinimumScale, MaximumScale);

        var style = ImGui.GetStyle();

        style.WindowPadding = new float2(18f, 16f) * scale;
        style.FramePadding = new float2(12f, 7f) * scale;
        style.ItemSpacing = new float2(10f, 8f) * scale;
        style.ItemInnerSpacing = new float2(8f, 6f) * scale;
        style.CellPadding = new float2(8f, 4f) * scale;
        style.IndentSpacing = 18f * scale;
        style.ScrollbarSize = 12f * scale;
        style.GrabMinSize = 14f * scale;

        // Square, everywhere. The drawn panels are chamfered rather than rounded, and a rounded
        // corner anywhere else — on the debug overlay, on a tooltip — would be the one moulded
        // shape in a program of machined ones. Rounding is set to zero rather than left alone
        // because ImGui's default is not zero.
        style.WindowRounding = 0f;
        style.ChildRounding = 0f;
        style.FrameRounding = 0f;
        style.PopupRounding = 0f;
        style.GrabRounding = 0f;
        style.TabRounding = 0f;

        style.WindowBorderSize = 1f;
        style.FrameBorderSize = 1f;
        style.PopupBorderSize = 1f;
        style.SeparatorTextBorderSize = 1f * scale;

        style.WindowTitleAlign = new float2(0.5f, 0.5f);
        style.SeparatorTextAlign = new float2(0.5f, 0.5f);
        style.SeparatorTextPadding = new float2(20f, 10f) * scale;

        // The text itself. ImGui scales the font separately from the sizes above, and leaving it
        // out is what makes a "larger interface" setting produce roomier panels of the same tiny
        // text.
        style.FontScaleMain = scale;

        style.DisabledAlpha = 0.45f;

        ApplyColors(style);
    }

    private static void ApplyColors(ImGuiStylePtr style)
    {
        ref var colors = ref style.Colors;

        colors[(int)ImGuiCol.Text] = UiPalette.Text;
        colors[(int)ImGuiCol.TextDisabled] = UiPalette.MutedText;

        colors[(int)ImGuiCol.WindowBg] = UiPalette.Panel;
        colors[(int)ImGuiCol.ChildBg] = UiPalette.Fade(UiPalette.Panel, 0f);
        colors[(int)ImGuiCol.PopupBg] = UiPalette.Panel;
        colors[(int)ImGuiCol.Border] = UiPalette.Divider;
        colors[(int)ImGuiCol.BorderShadow] = UiPalette.Fade(UiPalette.Divider, 0f);

        colors[(int)ImGuiCol.FrameBg] = UiPalette.Recess;
        colors[(int)ImGuiCol.FrameBgHovered] = UiPalette.Hovered;
        colors[(int)ImGuiCol.FrameBgActive] = UiPalette.Pressed;

        colors[(int)ImGuiCol.TitleBg] = UiPalette.Header;
        colors[(int)ImGuiCol.TitleBgActive] = UiPalette.Header;
        colors[(int)ImGuiCol.TitleBgCollapsed] = UiPalette.Fade(UiPalette.Header, 0.6f);

        colors[(int)ImGuiCol.Button] = UiPalette.Control;
        colors[(int)ImGuiCol.ButtonHovered] = UiPalette.Hovered;
        colors[(int)ImGuiCol.ButtonActive] = UiPalette.Pressed;

        colors[(int)ImGuiCol.Header] = UiPalette.Hovered;
        colors[(int)ImGuiCol.HeaderHovered] = UiPalette.Pressed;
        colors[(int)ImGuiCol.HeaderActive] = UiPalette.Pressed;

        colors[(int)ImGuiCol.Separator] = UiPalette.Divider;
        colors[(int)ImGuiCol.SeparatorHovered] = UiPalette.Accent;
        colors[(int)ImGuiCol.SeparatorActive] = UiPalette.Accent;

        // Everything that answers "which one is it" wears the accent: the slider's handle, a
        // tick, the selected tab. One colour for one meaning is most of what makes an interface
        // readable at a glance.
        colors[(int)ImGuiCol.CheckMark] = UiPalette.Accent;
        colors[(int)ImGuiCol.SliderGrab] = UiPalette.Accent;
        colors[(int)ImGuiCol.SliderGrabActive] = UiPalette.AccentHovered;
        colors[(int)ImGuiCol.TextSelectedBg] = UiPalette.Fade(UiPalette.Accent, 0.35f);
        colors[(int)ImGuiCol.TextLink] = UiPalette.Accent;
        colors[(int)ImGuiCol.NavCursor] = UiPalette.Accent;

        colors[(int)ImGuiCol.Tab] = UiPalette.Control;
        colors[(int)ImGuiCol.TabHovered] = UiPalette.Hovered;
        colors[(int)ImGuiCol.TabSelected] = UiPalette.Pressed;
        colors[(int)ImGuiCol.TabSelectedOverline] = UiPalette.Accent;
        colors[(int)ImGuiCol.TabDimmed] = UiPalette.Control;
        colors[(int)ImGuiCol.TabDimmedSelected] = UiPalette.Hovered;

        colors[(int)ImGuiCol.ScrollbarBg] = UiPalette.Fade(UiPalette.Control, 0.4f);
        colors[(int)ImGuiCol.ScrollbarGrab] = UiPalette.Hovered;
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = UiPalette.Pressed;
        colors[(int)ImGuiCol.ScrollbarGrabActive] = UiPalette.Accent;

        colors[(int)ImGuiCol.PlotLines] = UiPalette.Accent;
        colors[(int)ImGuiCol.PlotLinesHovered] = UiPalette.AccentHovered;
        colors[(int)ImGuiCol.PlotHistogram] = UiPalette.Accent;
        colors[(int)ImGuiCol.PlotHistogramHovered] = UiPalette.AccentHovered;

        // What dims the world behind a menu. The menu is the only thing being interacted with,
        // and a world still at full brightness behind it reads as though it were.
        colors[(int)ImGuiCol.ModalWindowDimBg] = UiPalette.Fade(UiPalette.Panel, 0.55f);
    }
}
