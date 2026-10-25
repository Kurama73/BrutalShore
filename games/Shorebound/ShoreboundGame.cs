using Brutal.ImGuiApi;
using BrutalShore.Gpu;
using BrutalShore.Render;
using BrutalShore.Runtime;
using BrutalShore.Voxel;
using BrutalShore.World;

namespace Shorebound;

/// <summary>Shorebound: the world, the player in it, and everything shown on top.</summary>
/// <remarks>
/// <para>
/// This is the whole of what the engine does not know. It owns the block set, the terrain, the
/// player's body, the clock, the hotbar, the menus and the save file; the engine owns chunks,
/// meshing, streaming, pipelines and the frame. The two meet at
/// <see cref="IChunkGenerator" /> and at <see cref="IGame" />, and nowhere else.
/// </para>
/// <para>
/// The budgets below are the only tuning that belongs to a game rather than to the renderer: how
/// much work one frame may do is a question about what this world should feel like, not about
/// how a chunk is built.
/// </para>
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
    private readonly Player _player = new();
    private readonly Hotbar _hotbar;
    private readonly GameUi _ui = new();
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
    private VoxelHit? _aimedAt;

    /// <summary>Builds the world, which needs nothing from the GPU.</summary>
    /// <param name="options">What the command line asked for.</param>
    public ShoreboundGame(Options options)
    {
        _options = options;
        _blocks = new GameBlocks(new BlockRegistry());
        _generator = new TerrainGenerator(_blocks, options.Seed);
        _world = new VoxelWorld(_blocks.Registry, _generator);
        _hotbar = new Hotbar(_blocks);
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

        StartFreshWorld();

        if (_options.Viewpoint is { } viewpoint)
        {
            // --camera names where the eye goes, so the feet go a player's height below it.
            _player.TeleportTo(
                new double3(
                    viewpoint.Position.X,
                    viewpoint.Position.Y - Player.EyeHeightMeters,
                    viewpoint.Position.Z
                )
            );
            _controller.AimAt(viewpoint.Yaw, viewpoint.Pitch);
        }

        _camera.Position = _player.EyeMeters;
        Console.WriteLine(
            "Click to look. WASD to move, space to jump, F to fly, 1-6 picks a block, escape releases the mouse."
        );
    }

    /// <inheritdoc />
    public void Step(double seconds)
    {
        _clock.Step(seconds);
        _player.Step(_world, seconds, _controller.ReadIntent(), _camera.Yaw);
        _camera.Position = _player.EyeMeters;
    }

    /// <inheritdoc />
    public void Update(GameContext context)
    {
        UpdateMouseCapture(context);

        if (context.Window.IsMouseCaptured)
        {
            _controller.Look();
            _camera.Position = _player.EyeMeters;
            ReadPlayingKeys(context);
        }

        Aim();

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
    public void DrawUi(GameContext context)
    {
        _ui.DrawCrosshair();
        _ui.DrawHotbar(_hotbar);
    }

    /// <inheritdoc />
    public void DrawOverlay()
    {
        ImGui.SeparatorText("Player"u8);
        ImGui.Text($"{_player.FeetMeters.X:F1}, {_player.FeetMeters.Y:F1}, {_player.FeetMeters.Z:F1}");
        ImGui.Text($"chunk {_centre.X}, {_centre.Y}, {_centre.Z}");
        ImGui.Text(
            _player.IsFlying
                ? "flying"
                : _player.IsOnGround
                    ? "on the ground"
                    : $"falling at {_player.FallSpeedMetersPerSecond:F1} m/s"
        );
        ImGui.Text(
            _aimedAt is { } hit
                ? $"aimed at {hit.Block.X}, {hit.Block.Y}, {hit.Block.Z} ({_world.Registry[_world.BlockAt(hit.Block.X, hit.Block.Y, hit.Block.Z)].Name})"
                : "aimed at nothing in reach"
        );

        ImGui.SeparatorText("Sky"u8);
        ImGui.Text($"{_clock} (day {(int)_clock.DayFraction + 1})");

        ImGui.SeparatorText("World"u8);
        ImGui.Text($"seed {_generator.Seed}, radius {_options.ViewRadiusChunks} chunks");
        ImGui.Text($"{_world.LoadedChunkCount} loaded, {_chunks.MeshCount} meshed");
        ImGui.Text($"{_chunks.PendingCount} waiting to mesh, {_world.Edits.Count} blocks changed");

        ImGui.SeparatorText("Drawn"u8);
        ImGui.Text($"{_chunks.DrawnChunkCount} chunks, {_chunks.DrawnTriangleCount} triangles");
        ImGui.Text($"{_chunks.ShadowedChunkCount} chunks into the shadow map");
        ImGui.Text($"{_chunks.ResidentBytes / (1024.0 * 1024.0):F1} MiB of geometry");

        ImGui.SeparatorText("Controls"u8);
        ImGui.Text("WASD, space, shift to sprint, F to fly"u8);
        ImGui.Text("left click breaks, right click places, 1-6 picks"u8);
        ImGui.Text("escape releases the mouse, F3 hides this"u8);
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

    /// <summary>Takes the mouse on a click and gives it back on escape.</summary>
    private static void UpdateMouseCapture(GameContext context)
    {
        if (context.Window.WasKeyPressed(GlfwKey.Escape))
            context.Window.SetMouseCaptured(false);
        else if (!context.Window.IsMouseCaptured && context.Window.WasMouseButtonPressed(GlfwMouseButton.Left))
            context.Window.SetMouseCaptured(true);
    }

    /// <summary>Reads the keys and clicks that only mean something inside the world.</summary>
    private void ReadPlayingKeys(GameContext context)
    {
        var window = context.Window;

        if (window.WasKeyPressed(GlfwKey.F))
            _player.IsFlying = !_player.IsFlying;

        for (var slot = 0; slot < _hotbar.Count; slot++)
        {
            if (window.WasKeyPressed(GlfwKey.Number1 + slot))
                _hotbar.Select(slot);
        }

        if (window.WasKeyPressed(GlfwKey.Q))
            _hotbar.Cycle(-1);

        if (window.WasKeyPressed(GlfwKey.E))
            _hotbar.Cycle(1);

        if (window.WasMouseButtonPressed(GlfwMouseButton.Left))
            Break();

        if (window.WasMouseButtonPressed(GlfwMouseButton.Right))
            Place();
    }

    /// <summary>Works out which block the player is looking at.</summary>
    private void Aim()
    {
        var forward = _camera.Forward;
        _aimedAt = VoxelRay.Cast(
            _world,
            _player.EyeMeters,
            new double3(forward.X, forward.Y, forward.Z),
            Player.ReachMeters
        );
    }

    private void Break()
    {
        if (_aimedAt is { } hit)
            _world.SetBlock(hit.Block, BlockId.Air);
    }

    private void Place()
    {
        if (_aimedAt is not { } hit)
            return;

        // A block placed where the player is standing would leave them inside it, and no answer
        // to that is a good one; refusing to place it is the only move that cannot go wrong.
        if (_player.WouldBeInsideMe(hit.Before))
            return;

        _world.SetBlock(hit.Before, _hotbar.Selected);
    }

    /// <summary>Puts the player on the surface above the origin, with nothing changed.</summary>
    private void StartFreshWorld()
    {
        _world.Edits.Clear();

        // One block above the surface, so the first step is a short fall rather than a long one
        // through terrain that has not streamed in yet.
        _player.TeleportTo(new double3(0.5, _generator.SurfaceHeight(0, 0) + 2.0, 0.5));
        _player.IsFlying = true;
        _camera.Position = _player.EyeMeters;
    }
}
