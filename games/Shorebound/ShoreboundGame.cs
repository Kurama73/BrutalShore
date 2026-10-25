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
    /// <summary>How many finished chunks may be taken into the world in one frame.</summary>
    /// <remarks>
    /// Generation itself happens on worker threads, so what this bounds is only the handover:
    /// applying the edits and marking neighbours dirty. That is cheap enough that the number can
    /// be several times what generating them on the frame would have allowed.
    /// </remarks>
    private const int LoadBudgetPerFrame = 48;

    /// <summary>How many chunks may be waiting to be generated at once.</summary>
    /// <remarks>
    /// Two rings' worth at the default radius. A player sprinting in a straight line can ask for
    /// chunks faster than they can be made, and a queue that grew without limit would spend the
    /// workers on ground already behind them.
    /// </remarks>
    private const int StreamingQueueCapacity = 256;

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

    /// <summary>How long a message about saving or loading stays on screen, in seconds.</summary>
    private const double NoticeSeconds = 3.0;

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

    private Screen _screen = Screen.Title;
    private int3 _centre;
    private float4x4 _viewProjection;
    private float4x4 _lightViewProjection;
    private VoxelHit? _aimedAt;
    private string _notice = string.Empty;
    private double _noticeLeftSeconds;

    /// <summary>Builds the world, which needs nothing from the GPU.</summary>
    /// <param name="options">What the command line asked for.</param>
    public ShoreboundGame(Options options)
    {
        _options = options;
        _blocks = new GameBlocks(new BlockRegistry());
        _generator = new TerrainGenerator(_blocks, options.Seed);
        // One worker per core but one, so the thread drawing the frame still has somewhere to
        // run; capped because beyond a handful the workers are waiting on memory, not arithmetic.
        _world = new VoxelWorld(
            _blocks.Registry,
            _generator,
            new ChunkStreamer(
                _generator,
                int.Clamp(Environment.ProcessorCount - 1, 1, 6),
                StreamingQueueCapacity
            )
        );
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

        // A run with a fixed frame count is a render check, not a play session: it has nobody to
        // click past a title screen, and the frames it was asked for start now.
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
            Enter(context);
        }
        else if (_options.Host.FrameLimit > 0)
        {
            Enter(context);
        }

        Console.WriteLine("Click to play. WASD to move, space to jump, F to fly, escape for the menu.");
    }

    /// <inheritdoc />
    public void Step(double seconds)
    {
        if (_screen != Screen.Playing)
            return;

        _clock.Step(seconds);
        _player.Step(_world, seconds, _controller.ReadIntent(), _camera.Yaw);
        _camera.Position = _player.EyeMeters;

        if (_noticeLeftSeconds > 0.0)
            _noticeLeftSeconds -= seconds;
    }

    /// <inheritdoc />
    public void Update(GameContext context)
    {
        ReadScreenKeys(context);

        if (_screen == Screen.Playing)
        {
            _controller.Look();
            _camera.Position = _player.EyeMeters;
            ReadPlayingKeys(context);
            Aim();
        }

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
        if (_noticeLeftSeconds > 0.0)
            _ui.DrawNotice(_notice, (float)(_noticeLeftSeconds / NoticeSeconds));

        switch (_screen)
        {
            case Screen.Title:
                Choose(context, _ui.DrawTitle(File.Exists(_options.SavePath)));
                break;

            case Screen.Playing:
                _ui.DrawCrosshair();
                _ui.DrawHotbar(_hotbar);
                break;

            case Screen.Paused:
                _ui.DrawHotbar(_hotbar);
                Choose(context, _ui.DrawPause(_options.SavePath));
                break;
        }
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
        ImGui.Text($"{_world.StreamingCount} chunks being generated");
        ImGui.Text($"{_chunks.PendingCount} waiting to mesh, {_world.Edits.Count} blocks changed");

        ImGui.SeparatorText("Drawn"u8);
        ImGui.Text($"{_chunks.DrawnChunkCount} chunks, {_chunks.DrawnTriangleCount} triangles");
        ImGui.Text($"{_chunks.ShadowedChunkCount} chunks into the shadow map");
        ImGui.Text($"{_chunks.ResidentBytes / (1024.0 * 1024.0):F1} MiB of geometry");

        ImGui.SeparatorText("Controls"u8);
        ImGui.Text("WASD, space, shift to sprint, F to fly"u8);
        ImGui.Text("left click breaks, right click places, 1-6 picks"u8);
        ImGui.Text("escape for the menu, F3 hides this"u8);
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
        _world.Dispose();
    }

    /// <summary>Reads the keys that change which screen is showing.</summary>
    private void ReadScreenKeys(GameContext context)
    {
        if (!context.Window.WasKeyPressed(GlfwKey.Escape))
            return;

        switch (_screen)
        {
            case Screen.Playing:
                _screen = Screen.Paused;
                context.Window.SetMouseCaptured(false);
                break;

            case Screen.Paused:
                Enter(context);
                break;
        }
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

    /// <summary>Acts on what a menu reported.</summary>
    private void Choose(GameContext context, MenuChoice choice)
    {
        switch (choice)
        {
            case MenuChoice.NewWorld:
                StartFreshWorld();
                Enter(context);
                break;

            case MenuChoice.Continue:
                LoadWorld();
                Enter(context);
                break;

            case MenuChoice.Resume:
                Enter(context);
                break;

            case MenuChoice.Save:
                SaveWorld();
                break;

            case MenuChoice.SaveAndQuit:
                SaveWorld();
                context.Window.Close();
                break;

            case MenuChoice.Quit:
                context.Window.Close();
                break;
        }
    }

    /// <summary>Goes into the world: mouse captured, time running.</summary>
    private void Enter(GameContext context)
    {
        _screen = Screen.Playing;
        context.Window.SetMouseCaptured(true);
        _camera.Position = _player.EyeMeters;
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

    private void LoadWorld()
    {
        try
        {
            _world.Edits.Clear();
            var (save, edits) = SaveFile.Read(_options.SavePath, _world.Edits, _world.Registry);

            // Every loaded chunk was generated before the edits arrived, so each has to be made
            // again; unloading is how a chunk is asked for a second time.
            foreach (var coordinate in _world.LoadedCoordinates.ToArray())
                _world.Unload(coordinate);

            _player.TeleportTo(save.FeetMeters);
            _player.IsFlying = save.IsFlying;
            _controller.AimAt(save.Yaw, save.Pitch);
            _hotbar.Select(save.HotbarIndex);
            _clock.SetDayFraction(save.DayFraction);

            Say(
                edits.Dropped == 0
                    ? $"Loaded {_options.SavePath}: {edits.Applied} blocks changed."
                    : $"Loaded {_options.SavePath}: {edits.Applied} blocks changed, {edits.Dropped} dropped."
            );
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            // A world that cannot be read is a thing to say, not a thing to crash over: the
            // player is standing in a perfectly good fresh one.
            Say($"Could not load {_options.SavePath}: {failure.Message}");
            StartFreshWorld();
        }
    }

    private void SaveWorld()
    {
        var save = new SaveFile
        {
            Seed = _generator.Seed,
            FeetMeters = _player.FeetMeters,
            Yaw = _camera.Yaw,
            Pitch = _camera.Pitch,
            IsFlying = _player.IsFlying,
            DayFraction = _clock.DayFraction,
            HotbarIndex = _hotbar.SelectedIndex,
        };

        try
        {
            save.Write(_options.SavePath, _world.Edits, _world.Registry);
            Say($"Saved {_world.Edits.Count} changed blocks to {_options.SavePath}.");
        }
        catch (IOException failure)
        {
            Say($"Could not save to {_options.SavePath}: {failure.Message}");
        }
    }

    private void Say(string message)
    {
        _notice = message;
        _noticeLeftSeconds = NoticeSeconds;
        Console.WriteLine(message);
    }
}
