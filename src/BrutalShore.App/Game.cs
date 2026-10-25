using BrutalShore.Core;
using BrutalShore.Gpu;
using BrutalShore.Render;
using BrutalShore.Voxel;
using BrutalShore.World;

namespace BrutalShore.App;

/// <summary>The game: a window, a world, and the loop that keeps them in step.</summary>
/// <remarks>
/// <para>
/// The loop separates three rates on purpose. Movement runs on <see cref="FrameClock" />'s fixed
/// step, so it is identical whatever the frame rate; streaming and meshing run once per frame
/// under a budget, so a burst of newly loaded chunks spreads over several frames instead of
/// stalling one; drawing runs once per frame, and may be skipped entirely when the swapchain is
/// being rebuilt.
/// </para>
/// <para>
/// Construction order is dependency order, and disposal is the reverse — which is why these are
/// fields assigned in the constructor rather than anything lazier: a Vulkan object outliving the
/// device that made it is a crash with no useful stack.
/// </para>
/// </remarks>
public sealed class Game : IDisposable
{
    /// <summary>How many chunks may be generated in one frame.</summary>
    /// <remarks>
    /// Generation is the expensive half of streaming — four noise fields per column — so this is
    /// the number that decides whether flying fast stutters. Low enough to hide inside a frame,
    /// high enough that the loaded radius keeps up with the sprint speed.
    /// </remarks>
    private const int LoadBudgetPerFrame = 8;

    /// <summary>How many chunks may be meshed and uploaded in one frame.</summary>
    private const int MeshBudgetPerFrame = 6;

    private readonly GameOptions _options;
    private readonly GameWindow _window;
    private readonly GpuContext _context;
    private readonly SwapchainTarget _target;
    private readonly GpuRenderer _renderer;
    private readonly ShaderLibrary _shaders;
    private readonly VoxelPipeline _pipeline;
    private readonly VoxelWorld _world;
    private readonly TerrainGenerator _generator;
    private readonly ChunkRenderer _chunks;
    private readonly DebugOverlay _overlay;
    private readonly Camera _camera = new();
    private readonly PlayerController _controller;
    private readonly FrameClock _clock = new();

    /// <summary>Builds the whole game from its options.</summary>
    public Game(GameOptions options)
    {
        _options = options;

        VulkanPlatform.Prepare();

        _window = new GameWindow("BrutalShore", new int2(1600, 900));
        _context = new GpuContext(_window, "BrutalShore", options.EnableValidation);
        _target = new SwapchainTarget(_context, _window);
        _renderer = new GpuRenderer(_context, _target);
        _shaders = new ShaderLibrary(_context, Path.Combine(AppContext.BaseDirectory, "Shaders"));
        _pipeline = new VoxelPipeline(_context, _shaders, _target.RenderPass);

        var blocks = new GameBlocks(new BlockRegistry());
        _generator = new TerrainGenerator(blocks, options.Seed);
        _world = new VoxelWorld(blocks.Registry, _generator);
        _chunks = new ChunkRenderer(_context, _renderer, _world, _pipeline);
        _overlay = new DebugOverlay(_window, _context, _target);
        _controller = new PlayerController(_window, _camera);

        if (options.Viewpoint is { } viewpoint)
        {
            _camera.Position = viewpoint.Position;
            _camera.Look(viewpoint.Yaw / _controller.Sensitivity, -viewpoint.Pitch / _controller.Sensitivity, _controller.Sensitivity);
        }
        else
        {
            _camera.Position = SpawnPosition();
        }
    }

    /// <summary>Runs until the window is closed.</summary>
    public void Run()
    {
        Console.WriteLine($"BrutalShore on {_context.DeviceName}, seed {_options.Seed}.");
        Console.WriteLine("Click to capture the mouse, WASD and space/ctrl to move, shift to sprint, escape to release.");

        while (!_window.ShouldClose)
        {
            _window.PollEvents();

            var steps = _clock.BeginFrame();
            for (var step = 0; step < steps; step++)
                _controller.Step(FrameClock.StepSeconds);

            var centre = WorldCoordinates.ToChunk(WorldCoordinates.ToBlock(_camera.Position));
            _world.UpdateStreaming(centre, _options.ViewRadiusChunks, LoadBudgetPerFrame);
            _chunks.Update(centre, MeshBudgetPerFrame);

            // A minimised window has a zero-sized framebuffer, which no swapchain can be created
            // for. Simulation keeps running; only drawing waits.
            if (_window.IsMinimised || !_renderer.BeginFrame())
                continue;

            var size = _target.Extent;
            _chunks.Draw(_camera, (float)size.Width / size.Height);

            // The overlay is built after the chunks are drawn so it can report what that draw
            // actually did, and recorded after them so it ends up on top.
            _overlay.Draw(Statistics(centre));
            _overlay.Render(_renderer.Commands);

            _renderer.EndFrame();

            if (_options.FrameLimit > 0 && _clock.FrameCount >= _options.FrameLimit)
                _window.Close();
        }

        Finish();
    }

    /// <summary>
    /// Saves the screenshot, if one was asked for, and reports what the run drew.
    /// </summary>
    /// <remarks>
    /// The summary is on the way out rather than per frame on purpose: a line printed every frame
    /// costs more than some of the things it is measuring, and the numbers that matter — whether
    /// chunks were meshed at all, how many triangles survived the greedy pass — are steady by the
    /// time the window closes.
    /// </remarks>
    private void Finish()
    {
        if (_options.ScreenshotPath is { } path)
        {
            // Everything has to be off the GPU before the image behind the last frame can be read.
            _context.WaitIdle();
            FrameCapture.Save(_context, _target, _renderer.LastImageIndex, path);
            Console.WriteLine($"Wrote {path}.");
        }

        Console.WriteLine(
            $"{_clock.FrameCount} frames, {_clock.FramesPerSecond:F1} fps, "
                + $"{_world.LoadedChunkCount} chunks loaded, {_chunks.MeshCount} meshed "
                + $"({_chunks.ResidentBytes / (1024.0 * 1024.0):F1} MiB), "
                + $"{_chunks.DrawnChunkCount} drawn, {_chunks.DrawnTriangleCount} triangles, "
                + $"{_chunks.PendingCount} waiting."
        );
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _overlay.Dispose();
        _chunks.Dispose();
        _pipeline.Dispose();
        _shaders.Dispose();
        _renderer.Dispose();
        _target.Dispose();
        _context.Dispose();
        _window.Dispose();
    }

    /// <summary>A snapshot of this frame for the overlay.</summary>
    private FrameStatistics Statistics(int3 centre) =>
        new(
            _context.DeviceName,
            _clock.FramesPerSecond,
            _clock.FrameSeconds * 1000.0,
            _camera.Position,
            centre,
            _generator.Seed,
            _options.ViewRadiusChunks,
            _world.LoadedChunkCount,
            _chunks.MeshCount,
            _chunks.PendingCount,
            _chunks.DrawnChunkCount,
            _chunks.DrawnTriangleCount,
            _chunks.ResidentBytes
        );

    /// <summary>
    /// A few metres above the terrain at the origin.
    /// </summary>
    /// <remarks>
    /// Asking the generator for the surface height rather than dropping the camera from the sky:
    /// the height field is cheap to evaluate at a point and does not need the chunk to exist, so
    /// the first frame already has the player standing on the ground.
    /// </remarks>
    private double3 SpawnPosition() => new(0.5, _generator.SurfaceHeight(0, 0) + 3.0, 0.5);
}
