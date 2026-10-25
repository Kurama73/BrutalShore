using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Backends;
using Brutal.VulkanApi;
using BrutalShore.Gpu;

namespace BrutalShore.App;

/// <summary>The development overlay: what the engine is doing, on screen, while it does it.</summary>
/// <remarks>
/// <para>
/// Dear ImGui through BRUTAL's two backends — one for input and windowing, one for drawing. The
/// drawing backend is built against a render pass and has to be told how many swapchain images
/// there are, which is why this is created after the swapchain and destroyed before it.
/// </para>
/// <para>
/// Toggled with F3, which is where a decade of this genre has trained everyone's hand to go.
/// </para>
/// </remarks>
public sealed class DebugOverlay : IDisposable
{
    private readonly GameWindow _window;
    private readonly GlfwBackend _platform;
    private readonly VulkanBackend _renderer;

    private bool _toggleWasDown;

    /// <summary>Starts Dear ImGui and attaches it to the window and the render pass.</summary>
    public DebugOverlay(GameWindow window, GpuContext context, SwapchainTarget target)
    {
        _window = window;

        ImGui.CreateContext();

        _platform = new GlfwBackend(window.Handle, GlfwBackend.ClientApi.Vulkan);
        _renderer = new VulkanBackend(
            new VulkanBackend.CreateInfo
            {
                Device = context.Device,
                GraphicsQueue = context.GraphicsQueue,
                RenderPass = target.RenderPass,
                MinImageCount = target.ImageCount,
                ImageCount = target.ImageCount,
                SampleCount = VkSampleCountFlags._1Bit,

                // The overlay draws text and a window frame: one font atlas and nothing else, so
                // a handful of descriptors is already generous.
                DescriptorPoolSize = 16,
            }
        );
    }

    /// <summary>Whether the overlay is being drawn.</summary>
    public bool IsVisible { get; private set; } = true;

    /// <summary>Starts an overlay frame and draws it.</summary>
    /// <param name="statistics">What to report.</param>
    public void Draw(in FrameStatistics statistics)
    {
        UpdateVisibility();

        _renderer.NewFrame();
        _platform.NewFrame();
        ImGui.NewFrame();

        if (IsVisible)
            DrawWindow(statistics);
    }

    /// <summary>Records the overlay's geometry into the frame being drawn.</summary>
    /// <remarks>
    /// Always called, even while the overlay is hidden: Dear ImGui's frame has been started by
    /// then and has to be finished, or the next one trips its own assertion.
    /// </remarks>
    public void Render(CommandBuffer commands)
    {
        ImGui.Render();
        _renderer.RenderDrawData(ImGui.GetDrawData(), commands);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _renderer.Dispose();
        _platform.Dispose();
        ImGui.DestroyContext();
    }

    private void UpdateVisibility()
    {
        var isDown = _window.IsKeyDown(GlfwKey.F3);
        if (isDown && !_toggleWasDown)
            IsVisible = !IsVisible;

        _toggleWasDown = isDown;
    }

    private static void DrawWindow(in FrameStatistics statistics)
    {
        ImGui.SetNextWindowPos(new float2(16f, 16f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(340f, 0f), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("BrutalShore"u8))
        {
            ImGui.End();
            return;
        }

        ImGui.Text($"{statistics.DeviceName}");
        ImGui.Text($"{statistics.FramesPerSecond:F0} fps  ({statistics.FrameMilliseconds:F2} ms)");

        ImGui.SeparatorText("Position"u8);
        ImGui.Text($"{statistics.Position.X:F1}, {statistics.Position.Y:F1}, {statistics.Position.Z:F1}");
        ImGui.Text($"chunk {statistics.Chunk.X}, {statistics.Chunk.Y}, {statistics.Chunk.Z}");

        ImGui.SeparatorText("World"u8);
        ImGui.Text($"seed {statistics.Seed}, radius {statistics.ViewRadiusChunks} chunks");
        ImGui.Text($"{statistics.LoadedChunks} loaded, {statistics.MeshedChunks} meshed");
        ImGui.Text($"{statistics.PendingChunks} waiting to mesh");

        ImGui.SeparatorText("Drawn"u8);
        ImGui.Text($"{statistics.DrawnChunks} chunks, {statistics.DrawnTriangles} triangles");
        ImGui.Text($"{statistics.ResidentBytes / (1024.0 * 1024.0):F1} MiB of geometry");

        ImGui.SeparatorText("Controls"u8);
        ImGui.Text("click to look, WASD, space/ctrl, shift"u8);
        ImGui.Text("escape releases the mouse, F3 hides this"u8);

        ImGui.End();
    }
}
