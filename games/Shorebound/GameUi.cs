using Brutal.ImGuiApi;

namespace Shorebound;

/// <summary>What the player sees over the world: a crosshair, the hotbar, and the menus.</summary>
/// <remarks>
/// <para>
/// Every panel here is built out of plain ImGui windows rather than a draw list, including the
/// crosshair. It costs a window where a line would do, and it buys the interface being made of
/// one kind of thing — which matters more while the thing is still changing shape every week.
/// </para>
/// <para>
/// It draws and reports; it never acts. A button returns a <see cref="MenuChoice" /> and the game
/// decides what that means, because loading a world and quitting are not decisions an interface
/// should be making on its own.
/// </para>
/// </remarks>
public sealed class GameUi
{
    private const ImGuiWindowFlags Plain =
        ImGuiWindowFlags.NoDecoration
        | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoSavedSettings
        | ImGuiWindowFlags.NoFocusOnAppearing
        | ImGuiWindowFlags.NoNav
        | ImGuiWindowFlags.AlwaysAutoResize;

    private const ImGuiWindowFlags Passive = Plain | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground;

    /// <summary>Draws the crosshair in the middle of the screen.</summary>
    public void DrawCrosshair()
    {
        var middle = Middle();
        ImGui.SetNextWindowPos(middle, ImGuiCond.Always, new float2(0.5f, 0.5f));
        ImGui.SetNextWindowBgAlpha(0f);

        if (ImGui.Begin("crosshair"u8, Passive))
            ImGui.Text("+"u8);

        ImGui.End();
    }

    /// <summary>Draws the row of blocks along the bottom of the screen.</summary>
    /// <param name="hotbar">The row to draw.</param>
    public void DrawHotbar(Hotbar hotbar)
    {
        var viewport = ImGui.GetMainViewport();
        var bottom = new float2(viewport.Pos.X + (viewport.Size.X * 0.5f), viewport.Pos.Y + viewport.Size.Y - 24f);

        ImGui.SetNextWindowPos(bottom, ImGuiCond.Always, new float2(0.5f, 1f));
        ImGui.SetNextWindowBgAlpha(0.55f);

        if (ImGui.Begin("hotbar"u8, Plain | ImGuiWindowFlags.NoInputs))
        {
            for (var slot = 0; slot < hotbar.Count; slot++)
            {
                if (slot > 0)
                    ImGui.SameLine();

                // The selected slot is named with its number so the key that picks it is on
                // screen; the others are dimmed rather than hidden, so the row keeps its shape.
                var label = $"{slot + 1} {hotbar.NameOf(slot)}";
                if (slot == hotbar.SelectedIndex)
                    ImGui.Text(label);
                else
                    ImGui.TextDisabled(label);
            }
        }

        ImGui.End();
    }

    /// <summary>Draws the title screen and reports what was clicked.</summary>
    /// <param name="hasSave">Whether there is a world to continue.</param>
    /// <returns>What the player chose, or <see cref="MenuChoice.None" />.</returns>
    public MenuChoice DrawTitle(bool hasSave)
    {
        var choice = MenuChoice.None;

        ImGui.SetNextWindowPos(Middle(), ImGuiCond.Always, new float2(0.5f, 0.5f));
        ImGui.SetNextWindowBgAlpha(0.88f);

        if (ImGui.Begin("Shorebound"u8, Plain))
        {
            ImGui.SeparatorText("Shorebound"u8);

            if (hasSave && ImGui.Button("Continue"u8, new float2(220f, 0f)))
                choice = MenuChoice.Continue;

            if (ImGui.Button(hasSave ? "New world" : "Play", new float2(220f, 0f)))
                choice = MenuChoice.NewWorld;

            if (ImGui.Button("Quit"u8, new float2(220f, 0f)))
                choice = MenuChoice.Quit;
        }

        ImGui.End();
        return choice;
    }

    /// <summary>Draws the pause menu and reports what was clicked.</summary>
    /// <param name="savePath">Where the world would be written, for the player to see.</param>
    /// <returns>What the player chose, or <see cref="MenuChoice.None" />.</returns>
    public MenuChoice DrawPause(string savePath)
    {
        var choice = MenuChoice.None;

        ImGui.SetNextWindowPos(Middle(), ImGuiCond.Always, new float2(0.5f, 0.5f));
        ImGui.SetNextWindowBgAlpha(0.88f);

        if (ImGui.Begin("Paused"u8, Plain))
        {
            ImGui.SeparatorText("Paused"u8);

            if (ImGui.Button("Resume"u8, new float2(220f, 0f)))
                choice = MenuChoice.Resume;

            if (ImGui.Button("Save"u8, new float2(220f, 0f)))
                choice = MenuChoice.Save;

            if (ImGui.Button("Save and quit"u8, new float2(220f, 0f)))
                choice = MenuChoice.SaveAndQuit;

            ImGui.TextDisabled(savePath);
        }

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
        var top = new float2(viewport.Pos.X + (viewport.Size.X * 0.5f), viewport.Pos.Y + 24f);

        ImGui.SetNextWindowPos(top, ImGuiCond.Always, new float2(0.5f, 0f));
        ImGui.SetNextWindowBgAlpha(0.5f * opacity);

        if (ImGui.Begin("notice"u8, Plain | ImGuiWindowFlags.NoInputs))
            ImGui.TextColored(new float4(1f, 1f, 1f, opacity), message);

        ImGui.End();
    }

    private static float2 Middle()
    {
        var viewport = ImGui.GetMainViewport();
        return new float2(
            viewport.Pos.X + (viewport.Size.X * 0.5f),
            viewport.Pos.Y + (viewport.Size.Y * 0.5f)
        );
    }
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

    /// <summary>Write the world out and stay in it.</summary>
    Save,

    /// <summary>Write the world out and close the game.</summary>
    SaveAndQuit,

    /// <summary>Close the game without saving.</summary>
    Quit,
}
