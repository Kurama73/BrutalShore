using Brutal.ImGuiApi;
using Brutal.ImPlotApi;
using Brutal.Pointers;
using Brutal.ImGuiApi.Backends;
using Brutal.VulkanApi;
using BrutalShore.Core;
using BrutalShore.Gpu;
using BrutalShore.Ui;

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
public sealed unsafe class DebugOverlay : IDisposable
{
    private readonly GameWindow _window;
    private readonly GlfwBackend _platform;
    private readonly VulkanBackend _renderer;
    private readonly ImPlotContextPtr _plots;


    /// <summary>Starts Dear ImGui and attaches it to the window and the render pass.</summary>
    /// <param name="window">The window the interface reads its input from.</param>
    /// <param name="context">The device the interface draws with.</param>
    /// <param name="target">The swapchain whose render pass the interface is built against.</param>
    /// <param name="startVisible">Whether the overlay itself starts shown.</param>
    /// <param name="uiScale">How large every panel is drawn, the overlay's and the game's alike.</param>
    public DebugOverlay(
        GameWindow window,
        GpuContext context,
        SwapchainTarget target,
        bool startVisible,
        float uiScale
    )
    {
        _window = window;
        IsVisible = startVisible;

        ImGui.CreateContext();

        // Before the drawing backend, which builds the font atlas from whatever the context has
        // by then. After it, the faces would be added to an atlas nobody rebuilds.
        UiFont.Load();

        // The context exists exactly once and the style belongs to it, so this is the only place
        // the look of the whole interface can be set from — the game's menus included.
        UiTheme.Apply(uiScale);

        // ImPlot keeps its own context beside ImGui's and has to be told about it; the frame
        // time graph is the only thing that uses it, and it is worth a graph.
        _plots = ImPlot.CreateContext();

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
    public bool IsVisible { get; private set; }

    /// <summary>Shows or hides the floating performance panel.</summary>
    /// <param name="visible">Whether to draw the panel.</param>
    public void SetVisible(bool visible) => IsVisible = visible;

    /// <summary>Opens the interface's frame and handles the key that hides the overlay.</summary>
    /// <remarks>
    /// Separate from <see cref="DrawPanel" /> because everything drawn through ImGui this frame —
    /// the game's interface as much as this overlay — has to be recorded between one
    /// <c>NewFrame</c> and one <c>Render</c>, and the game draws in between the two.
    /// </remarks>
    public void BeginFrame()
    {
        if (_window.WasKeyPressed(GlfwKey.F3))
            IsVisible = !IsVisible;

        _renderer.NewFrame();
        _platform.NewFrame();
        ImGui.NewFrame();
    }

    /// <summary>Draws the overlay's own window, if it is showing.</summary>
    /// <param name="deviceName">The GPU's name, which the host knows and the game does not.</param>
    /// <param name="clock">The frame clock, for the frame rate.</param>
    /// <param name="profile">Where the last few seconds of frames went.</param>
    /// <param name="game">The game, which fills in the middle of the window.</param>
    /// <remarks>
    /// Called after the game has drawn its own interface, so that the overlay lands on top of it.
    /// </remarks>
    public void DrawPanel(string deviceName, FrameClock clock, FrameProfile profile, IGame game)
    {
        if (!IsVisible || _window.IsMouseCaptured)
            return;

        var viewport = ImGui.GetMainViewport();
        var width = 460f * ImGui.GetStyle().FontScaleMain;

        // Along the right edge, because a game's own menus go on the left or in the middle and
        // this is the one panel that is nobody's but the developer's. It is still draggable; the
        // right is only where it starts.
        ImGui.SetNextWindowPos(
            viewport.Pos + new float2(viewport.Size.X - width - 16f, 16f),
            ImGuiCond.FirstUseEver
        );

        // Sized every frame rather than remembered. The panel's contents grow as the engine does,
        // and a height worked out once and stored in an interface settings file is a height that
        // was right for whatever the overlay said the first time it was ever opened.
        ImGui.SetNextWindowSize(new float2(width, viewport.Size.Y * 0.74f), ImGuiCond.Always);

        // Not saved between runs. A developer moving the panel out of the way should have it stay
        // there for the session, and a position written to a file beside the game is a position
        // that silently beats every later default — including the one that stops it covering a
        // menu that did not exist when the file was written.
        if (!ImGui.Begin("BrutalShore"u8, ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.End();
            return;
        }

        ImGui.Text($"{deviceName}");
        ImGui.Text($"{clock.FramesPerSecond:F0} fps  ({clock.FrameSeconds * 1000.0:F2} ms)");

        DrawFrameTime(profile);

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
    /// <summary>Draws the frame time graph and the breakdown under it.</summary>
    /// <remarks>
    /// The graph is the point. A frame rate is one number averaged over everything, and what it
    /// cannot show is the shape — a steady eight milliseconds with a spike every second looks
    /// exactly like a steady nine, and only one of the two is worth fixing.
    /// </remarks>
    private static void DrawFrameTime(FrameProfile profile)
    {
        var worst = profile.Frame.Worst();
        var ninetyNinth = profile.Frame.Percentile(0.99);

        ImGui.Text($"{profile.Frame.Average():F2} ms average, {ninetyNinth:F2} at the 99th, {worst:F2} worst");

        if (ImPlot.BeginPlot("##frame time"u8, new float2(-1f, 90f), ImPlotFlags.NoInputs | ImPlotFlags.NoLegend))
        {
            ImPlot.SetupAxes(default, default, ImPlotAxisFlags.NoDecorations, ImPlotAxisFlags.NoDecorations);
            ImPlot.SetupAxisLimits(ImAxis.X1, 0.0, FrameProfile.WindowFrames, ImPlotCond.Always);

            // Scaled to the worst frame in the window rather than auto-fitted, so the height of a
            // spike means the same thing from one second to the next.
            ImPlot.SetupAxisLimits(ImAxis.Y1, 0.0, double.Max(worst * 1.15, 20.0), ImPlotCond.Always);

            var window = profile.Frame.Window;
            fixed (float* samples = window)
            {
                ImPlot.PlotShaded("ms"u8, new Ptr<float>(samples), window.Length, 0.0, 1.0, 0.0);
                ImPlot.PlotLine("ms"u8, new Ptr<float>(samples), window.Length, 1.0, 0.0);
            }

            ImPlot.EndPlot();
        }

        // Split over two lines: the one that matters is whichever is largest, and a line that
        // runs off the edge of the panel hides exactly that.
        ImGui.Text(
            $"sim {profile.Simulation.Average():F2} ms   world {profile.Streaming.Average():F2} ms"
        );
        ImGui.Text(
            $"record {profile.Recording.Average():F2} ms   present {profile.Presenting.Average():F2} ms"
        );

        DrawCounters(profile.Counters);
    }

    /// <summary>Draws whatever counters the game opened, worst first.</summary>
    /// <param name="counters">The game's own divisions of its share of a frame.</param>
    /// <remarks>
    /// <para>
    /// Sorted by cost rather than shown in the order they were opened. The order they were opened
    /// in is the order the work happens, which is the right way to read a frame — but the
    /// question anyone opens this panel to answer is "what is slow", and that is answered by
    /// putting the largest at the top.
    /// </para>
    /// <para>
    /// The ninety-ninth percentile beside the average, because the frames a player notices are
    /// not the average ones. A counter whose average is half a millisecond and whose ninety-ninth
    /// is nine is a counter with a hitch in it, and the average alone hides that completely.
    /// </para>
    /// </remarks>
    private static void DrawCounters(Profiler counters)
    {
        if (counters.Count == 0)
            return;

        ImGui.SeparatorText("Game"u8);

        foreach (var (name, timeline) in counters.All().OrderByDescending(counter => counter.Timeline.Average()))
        {
            var average = timeline.Average();

            // Anything that never costs a tenth of a millisecond is noise in a list of what is
            // slow, and a list that is mostly noise is one nobody reads.
            if (average < 0.01f)
                continue;

            ImGui.Text($"{name} {average:F2} ms   {timeline.Percentile(0.99f):F2} at the 99th");
        }
    }

    /// <summary>Records the interface's draw commands into the frame.</summary>
    /// <param name="commands">The command buffer being recorded, inside the swapchain's pass.</param>
    /// <remarks>
    /// Closes the frame that <see cref="BeginFrame" /> opened, so everything drawn through ImGui
    /// since then — the game's windows as well as this overlay — ends up in these commands.
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
        ImPlot.DestroyContext(_plots);
        ImGui.DestroyContext();
    }
}