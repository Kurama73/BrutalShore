using BrutalShore.Core;
using BrutalShore.Gpu;

namespace BrutalShore.Runtime;

/// <summary>Everything the host owns and lends to the game.</summary>
/// <remarks>
/// Handed over rather than inherited from, so a game is a plain class the host calls into instead
/// of a subclass that can reach inside it. Every member is a thing with its own lifetime, created
/// before the game and destroyed after it.
/// </remarks>
public sealed class GameContext
{
    internal GameContext(
        GameWindow window,
        GpuContext gpu,
        SwapchainTarget target,
        GpuRenderer renderer,
        ShaderLibrary shaders,
        FrameClock clock,
        Profiler counters
    )
    {
        Window = window;
        Gpu = gpu;
        Target = target;
        Renderer = renderer;
        Shaders = shaders;
        Clock = clock;
        Counters = counters;
    }

    /// <summary>Where the game times its own work, for the overlay to show.</summary>
    /// <remarks>
    /// Handed to the game rather than reached for, so that timing something is available
    /// everywhere the context is and nothing has to be wired through on purpose. Counters are
    /// meant to be opened once, at load, and the handles kept.
    /// </remarks>
    public Profiler Counters { get; }

    /// <summary>The window, for input and its framebuffer size.</summary>
    public GameWindow Window { get; }

    /// <summary>The Vulkan device everything is created on.</summary>
    public GpuContext Gpu { get; }

    /// <summary>What a frame is drawn into, and the render pass a pipeline is built against.</summary>
    public SwapchainTarget Target { get; }

    /// <summary>The frame loop, for the command buffer being recorded and for retiring resources.</summary>
    public GpuRenderer Renderer { get; }

    /// <summary>Compiled shaders, loaded by file name.</summary>
    public ShaderLibrary Shaders { get; }

    /// <summary>The clock, for the frame rate and the step count.</summary>
    public FrameClock Clock { get; }

    /// <summary>The framebuffer's width divided by its height.</summary>
    public float AspectRatio => (float)Target.Extent.Width / Target.Extent.Height;
}
