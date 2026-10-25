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
/// It draws and reports; it never acts on the world. Deciding what a click means is the game's,
/// because an interface that reaches into the world is one that has to be read to know what the
/// world can do.
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

    private static float2 Middle()
    {
        var viewport = ImGui.GetMainViewport();
        return new float2(
            viewport.Pos.X + (viewport.Size.X * 0.5f),
            viewport.Pos.Y + (viewport.Size.Y * 0.5f)
        );
    }
}
