using BrutalShore.Core;
using BrutalShore.Gpu;

namespace BrutalShore.Runtime;

/// <summary>Owns the window, the device and the frame loop, and runs a game inside them.</summary>
/// <remarks>
/// <para>
/// Everything here is true of any game: open a window, find a GPU, build a swapchain, keep two
/// frames in flight, run the simulation at a fixed rate whatever the frame rate, and draw. None of
/// it mentions a block, a chunk or a player, and that is the point — the game arrives through
/// <see cref="IGame" />.
/// </para>
/// <para>
/// The loop separates three rates deliberately. <see cref="IGame.Step" /> runs on
/// <see cref="FrameClock" />'s fixed step so movement is identical at any frame rate;
/// <see cref="IGame.Update" /> runs once per frame for work that is budgeted rather than exact,
/// such as streaming; <see cref="IGame.Draw" /> runs only on frames that are actually presented,
/// and may be skipped entirely while the swapchain is being rebuilt.
/// </para>
/// </remarks>
public sealed class GameHost : IDisposable
{
    private readonly HostOptions _options;
    private readonly GameWindow _window;
    private readonly GpuContext _gpu;
    private readonly SwapchainTarget _target;
    private readonly GpuRenderer _renderer;
    private readonly ShaderLibrary _shaders;
    private readonly DebugOverlay _overlay;
    private readonly FrameClock _clock = new();

    /// <summary>Opens a window and brings up a device on it.</summary>
    public GameHost(HostOptions options)
    {
        _options = options;

        VulkanPlatform.Prepare();

        _window = new GameWindow(options.Title, options.WindowSize);
        _gpu = new GpuContext(_window, options.Title, options.EnableValidation);
        _target = new SwapchainTarget(_gpu, _window);
        _renderer = new GpuRenderer(_gpu, _target);
        _shaders = new ShaderLibrary(_gpu, Path.Combine(AppContext.BaseDirectory, "Shaders"));
        _overlay = new DebugOverlay(_window, _gpu, _target);

        Context = new GameContext(_window, _gpu, _target, _renderer, _shaders, _clock);
    }

    /// <summary>What the host lends to the game.</summary>
    public GameContext Context { get; }

    /// <summary>Runs <paramref name="game" /> until the window closes or the frame limit is reached.</summary>
    public void Run(IGame game)
    {
        game.Load(Context);

        Console.WriteLine($"{_options.Title} on {_gpu.DeviceName}.");

        while (!_window.ShouldClose)
        {
            _window.PollEvents();

            var steps = _clock.BeginFrame();
            for (var step = 0; step < steps; step++)
                game.Step(FrameClock.StepSeconds);

            game.Update(Context);

            // A minimised window has a zero-sized framebuffer, which no swapchain can be created
            // for. The game keeps running; only drawing waits.
            if (_window.IsMinimised || !_renderer.BeginFrame())
                continue;

            // Off-screen passes first: once the swapchain's render pass is open, no other can be.
            game.DrawOffscreen(Context);
            _renderer.BeginSceneRenderPass();

            game.Draw(Context);

            // The overlay is built after the game has drawn, so it can report what that draw did,
            // and recorded after it so it ends up on top.
            _overlay.Draw(_gpu.DeviceName, _clock, game);
            _overlay.Render(_renderer.Commands);

            _renderer.EndFrame();

            if (_options.FrameLimit > 0 && _clock.FrameCount >= _options.FrameLimit)
                _window.Close();
        }

        Finish(game);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _overlay.Dispose();
        _shaders.Dispose();
        _renderer.Dispose();
        _target.Dispose();
        _gpu.Dispose();
        _window.Dispose();
    }

    private void Finish(IGame game)
    {
        if (_options.ScreenshotPath is { } path)
        {
            // Everything has to be off the GPU before the image behind the last frame can be read.
            _gpu.WaitIdle();
            FrameCapture.Save(_gpu, _target, _renderer.LastImageIndex, path);
            Console.WriteLine($"Wrote {path}.");
        }

        // Printed on the way out rather than per frame: a line every frame costs more than some of
        // what it measures, and these numbers are steady by the time the window closes.
        Console.WriteLine($"{_clock.FrameCount} frames, {_clock.FramesPerSecond:F1} fps. {game.Summary}");
    }
}
