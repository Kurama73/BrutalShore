using Brutal.ImGuiApi;
using BrutalShore.Gpu;
using BrutalShore.Render;
using BrutalShore.Runtime;
using BrutalShore.Voxel;
using BrutalShore.World;

namespace Shorebound;

/// <summary>Shorebound: a voxel world to fly through, and everything particular to it.</summary>
/// <remarks>
/// This is the whole of what the engine does not know. The blocks that exist, the terrain they are
/// arranged into, how fast the player moves, where they start, how much work a frame may spend on
/// loading and meshing — all of it is here, and none of it is in <c>src/</c>.
/// </remarks>
public sealed class ShoreboundGame : IGame
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

    /// <summary>Where fog begins, as a fraction of the loaded radius.</summary>
    /// <remarks>
    /// The band is placed around the loaded radius rather than inside it. A chunk appearing at the
    /// boundary has to be most of the way into the fog by the time it does, but pulling the band
    /// further in costs the whole middle distance — and the diagonal corners of the loaded box are
    /// further away than the radius, which is why it ends past 1.
    /// </remarks>
    private const float FogStartFraction = 0.78f;

    /// <summary>Where fog is total, as a fraction of the loaded radius.</summary>
    private const float FogEndFraction = 1.15f;

    /// <summary>How far from the player the sun's shadows reach, in metres.</summary>
    /// <remarks>
    /// With a 2048 map that is a shade over ten centimetres per texel, against blocks a metre
    /// across — fine enough that a shadow's edge reads as an edge rather than a staircase.
    /// Doubling the radius would halve that, and the far half of it is behind the fog anyway.
    /// </remarks>
    private const float ShadowRadiusMeters = 112f;

    /// <summary>The shadow map's size in texels.</summary>
    private const int ShadowResolution = 2048;

    private readonly Options _options;
    private readonly GameBlocks _blocks;
    private readonly TerrainGenerator _generator;
    private readonly VoxelWorld _world;
    private readonly Camera _camera = new();
    private readonly DayClock _clock;

    // Assigned in Load, because each of these needs a device, and there is none until the host has
    // opened a window.
    private PlayerController _controller = null!;
    private DepthTarget _shadowMap = null!;
    private SceneBindings _scene = null!;
    private VoxelPipeline _pipeline = null!;
    private ShadowPipeline _shadowPipeline = null!;
    private SkyRenderer _sky = null!;
    private ChunkRenderer _chunks = null!;

    private int3 _centre;
    private float4x4 _viewProjection;
    private float4x4 _lightViewProjection;

    /// <summary>Builds the world, which needs nothing from the GPU.</summary>
    public ShoreboundGame(Options options)
    {
        _options = options;
        _blocks = new GameBlocks(new BlockRegistry());
        _generator = new TerrainGenerator(_blocks, options.Seed);
        _world = new VoxelWorld(_blocks.Registry, _generator);
        _clock = new DayClock(options.StartHour, options.DayLengthSeconds);
    }

    /// <inheritdoc />
    public string Summary =>
        $"{_world.LoadedChunkCount} chunks loaded, {_chunks.MeshCount} meshed "
        + $"({_chunks.ResidentBytes / (1024.0 * 1024.0):F1} MiB), "
        + $"{_chunks.DrawnChunkCount} drawn, {_chunks.DrawnTriangleCount} triangles, "
        + $"{_chunks.PendingCount} waiting.";

    /// <inheritdoc />
    public void Load(GameContext context)
    {
        _shadowMap = new DepthTarget(context.Gpu, ShadowResolution);
        _scene = new SceneBindings(context.Gpu, _shadowMap);
        _pipeline = new VoxelPipeline(context.Gpu, context.Shaders, _scene, context.Target.RenderPass);
        _shadowPipeline = new ShadowPipeline(context.Gpu, context.Shaders, _scene, _shadowMap.RenderPass);
        _sky = new SkyRenderer(context.Gpu, context.Renderer, context.Shaders, _scene, context.Target.RenderPass);
        _chunks = new ChunkRenderer(context.Gpu, context.Renderer, _world, _pipeline, _scene);
        _controller = new PlayerController(context.Window, _camera);

        if (_options.Viewpoint is { } viewpoint)
        {
            _camera.Position = viewpoint.Position;
            _camera.Look(
                viewpoint.Yaw / _controller.Sensitivity,
                -viewpoint.Pitch / _controller.Sensitivity,
                _controller.Sensitivity
            );
        }
        else
        {
            // Asking the generator for the surface height rather than dropping the camera from the
            // sky: the height field is cheap to evaluate at a point and does not need the chunk to
            // exist, so the first frame already has the player standing on the ground.
            _camera.Position = new double3(0.5, _generator.SurfaceHeight(0, 0) + 3.0, 0.5);
        }

        Console.WriteLine("Click to capture the mouse, WASD and space/ctrl to move, shift to sprint, escape to release.");
    }

    /// <inheritdoc />
    public void Step(double seconds)
    {
        _controller.Step(seconds);
        _clock.Step(seconds);
    }

    /// <inheritdoc />
    public void Update(GameContext context)
    {
        _centre = WorldCoordinates.ToChunk(WorldCoordinates.ToBlock(_camera.Position));
        _world.UpdateStreaming(_centre, _options.ViewRadiusChunks, LoadBudgetPerFrame);
        _chunks.Update(_centre, MeshBudgetPerFrame);
    }

    /// <inheritdoc />
    public void DrawOffscreen(GameContext context)
    {
        // The whole frame's view of the world is decided here, before anything is recorded, so
        // that the shadow pass and the camera pass cannot disagree about where the sun is.
        var sky = SunCycle.At(_clock.DayFraction);
        _viewProjection = _camera.ViewProjection(context.AspectRatio);
        _lightViewProjection = SunShadow.LightViewProjection(
            _camera.Position,
            sky.KeyDirection,
            ShadowRadiusMeters,

            // Deep enough to catch anything above or below the player that could cast into the
            // box, which in a world this tall means the whole of it either way.
            VoxelWorld.HeightBlocks + ShadowRadiusMeters,
            ShadowResolution
        );

        var viewMeters = _options.ViewRadiusChunks * Chunk.Size;
        _scene.Update(
            context.Renderer.FrameIndex,
            _viewProjection,
            _lightViewProjection,
            sky,
            viewMeters * FogStartFraction,
            viewMeters * FogEndFraction
        );

        _shadowMap.Begin(context.Renderer.Commands);
        _chunks.DrawShadow(_shadowPipeline, _camera, _lightViewProjection);
        _shadowMap.End(context.Renderer.Commands);
    }

    /// <inheritdoc />
    public void Draw(GameContext context)
    {
        // The sky first: it writes no depth, so everything below lands on top of it.
        _sky.Draw();
        _chunks.Draw(_camera, _viewProjection);
    }

    /// <inheritdoc />
    public void DrawOverlay()
    {
        ImGui.SeparatorText("Position"u8);
        ImGui.Text($"{_camera.Position.X:F1}, {_camera.Position.Y:F1}, {_camera.Position.Z:F1}");
        ImGui.Text($"chunk {_centre.X}, {_centre.Y}, {_centre.Z}");

        ImGui.SeparatorText("Sky"u8);
        ImGui.Text($"{_clock} (day {(int)_clock.DayFraction + 1})");
        ImGui.SeparatorText("World"u8);
        ImGui.Text($"seed {_generator.Seed}, radius {_options.ViewRadiusChunks} chunks");
        ImGui.Text($"{_world.LoadedChunkCount} loaded, {_chunks.MeshCount} meshed");
        ImGui.Text($"{_chunks.PendingCount} waiting to mesh");

        ImGui.SeparatorText("Drawn"u8);
        ImGui.Text($"{_chunks.DrawnChunkCount} chunks, {_chunks.DrawnTriangleCount} triangles");
        ImGui.Text($"{_chunks.ShadowedChunkCount} chunks into the shadow map");
        ImGui.Text($"{_chunks.ResidentBytes / (1024.0 * 1024.0):F1} MiB of geometry");

        ImGui.SeparatorText("Controls"u8);
        ImGui.Text("click to look, WASD, space/ctrl, shift"u8);
        ImGui.Text("escape releases the mouse"u8);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _chunks.Dispose();
        _sky.Dispose();
        _shadowPipeline.Dispose();
        _pipeline.Dispose();
        _scene.Dispose();
        _shadowMap.Dispose();
    }
}
