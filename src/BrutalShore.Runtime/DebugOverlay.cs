using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Backends;
using Brutal.VulkanApi;
using BrutalShore.Core;
using BrutalShore.Gpu;

namespace BrutalShore.Runtime;

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
    /// <param name="deviceName">The GPU in use.</param>
    /// <param name="clock">The clock the frame rate is read from.</param>
    /// <param name="game">The game, which fills in its own rows.</param>
    public void Draw(string deviceName, FrameClock clock, IGame game)
    {
        UpdateVisibility();

        _renderer.NewFrame();
        _platform.NewFrame();
        ImGui.NewFrame();

        if (!IsVisible)
            return;

        ImGui.SetNextWindowPos(new float2(16f, 16f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(340f, 0f), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("BrutalShore"u8))
        {
            ImGui.End();
            return;
        }

        ImGui.Text($"{deviceName}");
        ImGui.Text($"{clock.FramesPerSecond:F0} fps  ({clock.FrameSeconds * 1000.0:F2} ms)");

        // The host stops here. What else is worth showing is the game's business, and the engine
        // has no way of guessing it.
        game.DrawOverlay();

        ImGui.SeparatorText("Overlay"u8);
        ImGui.Text("F3 hides this"u8);

        ImGui.End();
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
}
