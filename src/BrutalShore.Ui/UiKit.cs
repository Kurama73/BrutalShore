using Brutal.ImGuiApi;

namespace BrutalShore.Ui;

/// <summary>The handful of widgets every screen in the game is built out of.</summary>
/// <remarks>
/// <para>
/// Not a widget library. Each of these is Dear ImGui's own control with this game's sizing and
/// colour already applied, and they exist because the alternative — every screen choosing its own
/// button width and its own shade of muted — is what makes an interface look assembled rather
/// than designed.
/// </para>
/// <para>
/// Anything that would only ever be called from one screen stays on that screen. What is here is
/// what the engine's overlay and the game's menus both reach for.
/// </para>
/// </remarks>
public static class UiKit
{
    /// <summary>How wide a menu button is, before the interface scale.</summary>
    public const float ButtonWidth = 260f;

    /// <summary>A section's name, with a rule through the space beside it.</summary>
    /// <param name="text">What the section is.</param>
    public static void Heading(string text) => ImGui.SeparatorText(text);

    /// <summary>A line of explanation under a control, in the muted colour.</summary>
    /// <param name="text">What it says.</param>
    public static void Hint(string text) => ImGui.TextDisabled(text);

    /// <summary>A full-width button.</summary>
    /// <param name="label">What it says.</param>
    /// <param name="enabled">Whether it can be pressed; a disabled one is dimmed, not hidden.</param>
    /// <returns>Whether it was pressed.</returns>
    public static bool Button(string label, bool enabled = true) => Button(label, UiPalette.Control, enabled);

    /// <summary>
    /// The one button on a screen that is the obvious thing to do, in the accent colour.
    /// </summary>
    /// <param name="label">What it says.</param>
    /// <param name="enabled">Whether it can be pressed.</param>
    /// <returns>Whether it was pressed.</returns>
    /// <remarks>
    /// One per screen. Two of them is the same as none: what the colour says is "this one", and
    /// it can only say that about one thing.
    /// </remarks>
    public static bool PrimaryButton(string label, bool enabled = true) =>
        Button(label, UiPalette.Accent, enabled, UiPalette.Panel);

    /// <summary>A button for something that loses work or ends the session.</summary>
    /// <param name="label">What it says.</param>
    /// <param name="enabled">Whether it can be pressed.</param>
    /// <returns>Whether it was pressed.</returns>
    public static bool DangerButton(string label, bool enabled = true) =>
        Button(label, UiPalette.Warning, enabled, UiPalette.Panel);

    /// <summary>A labelled slider over a range of real values.</summary>
    /// <param name="label">What the value is.</param>
    /// <param name="value">The value, written back when it moves.</param>
    /// <param name="minimum">The lowest it goes.</param>
    /// <param name="maximum">The highest it goes.</param>
    /// <param name="format">
    /// How the number reads, as a printf format. Units belong in it — a slider that says "70" and
    /// not "70°" is a slider nobody can set deliberately.
    /// </param>
    /// <returns>Whether the value changed.</returns>
    public static bool Slider(string label, ref float value, float minimum, float maximum, string format) =>
        ImGui.SliderFloat(label, ref value, minimum, maximum, format);

    /// <summary>A labelled slider over a range of whole values.</summary>
    /// <param name="label">What the value is.</param>
    /// <param name="value">The value, written back when it moves.</param>
    /// <param name="minimum">The lowest it goes.</param>
    /// <param name="maximum">The highest it goes.</param>
    /// <param name="format">How the number reads, as a printf format, units included.</param>
    /// <returns>Whether the value changed.</returns>
    public static bool Slider(string label, ref int value, int minimum, int maximum, string format) =>
        ImGui.SliderInt(label, ref value, minimum, maximum, format);

    /// <summary>A labelled switch.</summary>
    /// <param name="label">What it turns on.</param>
    /// <param name="value">The state, written back when it is clicked.</param>
    /// <returns>Whether it changed.</returns>
    public static bool Toggle(string label, ref bool value) => ImGui.Checkbox(label, ref value);

    /// <summary>A gap the height of one control, for separating groups without a rule.</summary>
    public static void Gap() => ImGui.Dummy(new float2(0f, ImGui.GetStyle().ItemSpacing.Y));

    private static bool Button(string label, float4 color, bool enabled, float4? textColor = null)
    {
        var pushed = 3;
        ImGui.PushStyleColor(ImGuiCol.Button, color);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Lighten(color));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Darken(color));

        if (textColor is { } text)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, text);
            pushed++;
        }

        ImGui.BeginDisabled(!enabled);
        var pressed = ImGui.Button(label, new float2(ButtonWidth * ImGui.GetStyle().FontScaleMain, 0f));
        ImGui.EndDisabled();

        ImGui.PopStyleColor(pushed);
        return pressed;
    }

    // Mixed towards white and towards the panel rather than scaled, so a dark button lightens
    // visibly and a bright one does not simply clip.
    private static float4 Lighten(float4 color) => Mix(color, UiPalette.Text, 0.22f);

    private static float4 Darken(float4 color) => Mix(color, UiPalette.Panel, 0.28f);

    private static float4 Mix(float4 from, float4 to, float amount) =>
        new(
            float.Lerp(from.X, to.X, amount),
            float.Lerp(from.Y, to.Y, amount),
            float.Lerp(from.Z, to.Z, amount),
            from.W
        );
}
