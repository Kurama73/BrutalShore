using Brutal.ImGuiApi;
using BrutalShore.Core;
using BrutalShore.Gpu;
using BrutalShore.Render;
using BrutalShore.Runtime;
using BrutalShore.Ui;
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

    /// <summary>How many chunks may be waiting to be meshed at once.</summary>
    /// <remarks>
    /// Smaller than the generation queue: a chunk can only be meshed once its whole neighbourhood
    /// is resident, so there are fewer of them ready at any moment, and each one holds twenty-six
    /// chunk references alive while it waits.
    /// </remarks>
    private const int MeshQueueCapacity = 64;

    /// <summary>How many chunks may be waiting to be generated at once.</summary>
    /// <remarks>
    /// Two rings' worth at the default radius. A player sprinting in a straight line can ask for
    /// chunks faster than they can be made, and a queue that grew without limit would spend the
    /// workers on ground already behind them.
    /// </remarks>
    private const int StreamingQueueCapacity = 256;

    /// <summary>How many threads each of the two streamers gets.</summary>
    /// <remarks>
    /// One per core but one, so the thread drawing the frame still has somewhere to run, and
    /// capped because past a handful the workers are waiting on memory rather than arithmetic.
    /// Both pools get the same number: generating and meshing alternate for any given chunk, so
    /// whichever is given fewer threads becomes the queue the other waits behind.
    /// </remarks>
    private static readonly int WorkerCount = int.Clamp(Environment.ProcessorCount - 1, 1, 6);

    /// <summary>How many finished meshes may be uploaded to the GPU in one frame.</summary>
    /// <remarks>
    /// Meshing happens on worker threads, so what this bounds is the upload: one buffer pair per
    /// chunk, written straight into host-visible memory. That is bandwidth rather than work, and
    /// a frame can afford several times what building the meshes on it would have allowed.
    /// </remarks>
    private const int MeshBudgetPerFrame = 24;

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

    /// <summary>How much of its direct light a fully shadowed surface loses.</summary>
    /// <remarks>
    /// Kept here rather than taken from the renderer's own default so the debug switch has
    /// something to put back when it is turned on again.
    /// </remarks>
    private const float DefaultShadowStrength = 0.9f;

    /// <summary>How far anything can be seen through a liquid, in metres.</summary>
    /// <remarks>
    /// Short, because that is what makes being underwater feel like being underwater rather than
    /// like being outside with a blue filter on. Twenty metres is roughly a clear sea.
    /// </remarks>
    private const float UnderwaterSightMeters = 28f;

    /// <summary>How long a message about saving or loading stays on screen, in seconds.</summary>
    private const double NoticeSeconds = 3.0;

    /// <summary>What colour a carried lantern burns.</summary>
    /// <remarks>
    /// The same warm colour the shader gives a placed one, so walking a lantern into a room lit
    /// by another does not change the colour of the room.
    /// </remarks>
    private static readonly float3 HeldLightColor = new(1f, 0.72f, 0.42f);

    /// <summary>How far a carried lantern reaches at full emission, in metres.</summary>
    private const float HeldLightRangeMeters = 15f;

    /// <summary>Where a carried light sits relative to the eye: down, to the right, and ahead.</summary>
    private static readonly float3 HeldLightOffsetMeters = new(0.38f, -0.34f, 0.25f);

    /// <summary>How much a carried flame wavers, as a fraction of its brightness.</summary>
    /// <remarks>
    /// Seven per cent, from two sine waves whose periods do not divide into each other, so the
    /// pattern does not repeat on any interval anyone notices. Enough to read as a flame; any
    /// more and a room appears to be breathing.
    /// </remarks>
    private const float FlickerAmount = 0.07f;

    private readonly Options _options;
    private readonly GameBlocks _blocks;
    private readonly TerrainGenerator _generator;
    private readonly VoxelWorld _world;
    private readonly Camera _camera = new();
    private readonly Player _player = new();
    private readonly Hotbar _hotbar;
    private readonly GameUi _ui = new();
    private readonly DayClock _clock;
    private readonly TitleCamera _titleCamera;
    private readonly Mining _mining = new();
    private readonly Drops _drops;

    // Assigned in Load, because each of these needs a device, and there is none until the host has
    // opened a window.
    private PlayerController _controller = null!;
    private DepthTarget _shadowMap = null!;
    private VoxelField _field = null!;
    private SceneBindings _scene = null!;
    private VoxelPipeline _opaquePipeline = null!;
    private VoxelPipeline _translucentPipeline = null!;
    private ShadowPipeline _shadowPipeline = null!;
    private SkyRenderer _sky = null!;
    private OutlineRenderer _outline = null!;
    private ChunkRenderer _chunks = null!;
    private CubeRenderer _cubes = null!;

    private Settings _settings;
    private Screen _screen = Screen.Title;

    // Where the settings were opened from, so closing them goes back rather than always to one
    // of the two: the same screen is reached from the title and from a paused world.
    private Screen _screenBehindSettings = Screen.Title;

    private int3 _centre;
    private double _playedSeconds;
    private bool _isBreaking;
    private float4x4 _viewProjection;
    private float4x4 _lightViewProjection;
    private VoxelHit? _aimedAt;
    private float4 _submerged;
    private string _notice = string.Empty;
    private double _noticeLeftSeconds;

    /// <summary>Builds the world, which needs nothing from the GPU.</summary>
    /// <param name="options">What the command line asked for.</param>
    /// <param name="settings">The player's settings, with the command line already folded in.</param>
    public ShoreboundGame(Options options, Settings settings)
    {
        _options = options;
        _settings = settings.Clamped();
        _blocks = new GameBlocks(new BlockRegistry());
        _generator = new TerrainGenerator(_blocks, options.Seed);
        _world = new VoxelWorld(
            _blocks.Registry,
            _generator,
            new ChunkStreamer(_generator, WorkerCount, StreamingQueueCapacity)
        );
        _hotbar = new Hotbar(_blocks);
        _drops = new Drops(options.Seed);
        _clock = new DayClock(options.StartHour, _settings.DayLengthSeconds);

        // Above where the player will spawn, which is the one place the world is certain to have
        // something in it worth looking at.
        _titleCamera = new TitleCamera(new double3(0.0, _generator.SurfaceHeight(0, 0), 0.0));
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
        _field = new VoxelField(context.Gpu, _world);
        _scene = new SceneBindings(context.Gpu, _shadowMap, _field);
        _opaquePipeline = new VoxelPipeline(
            context.Gpu,
            context.Shaders,
            _scene,
            context.Target.RenderPass,
            VoxelPass.Opaque
        );
        _translucentPipeline = new VoxelPipeline(
            context.Gpu,
            context.Shaders,
            _scene,
            context.Target.RenderPass,
            VoxelPass.Translucent
        );
        _shadowPipeline = new ShadowPipeline(context.Gpu, context.Shaders, _scene, _shadowMap.RenderPass);
        _sky = new SkyRenderer(context.Gpu, context.Renderer, context.Shaders, _scene, context.Target.RenderPass);
        _outline = new OutlineRenderer(
            context.Gpu,
            context.Renderer,
            context.Shaders,
            _scene,
            context.Target.RenderPass
        );
        _chunks = new ChunkRenderer(
            context.Gpu,
            context.Renderer,
            _world,
            _opaquePipeline,
            _translucentPipeline,
            _scene,
            new MeshStreamer(_blocks.Registry, WorkerCount, MeshQueueCapacity)
        );
        _cubes = new CubeRenderer(
            context.Gpu,
            context.Renderer,
            context.Shaders,
            _scene,
            context.Target.RenderPass
        );
        _controller = new PlayerController(context.Window, _camera);

        // Everything that has to be told about a setting rather than read it, told once now that
        // the things to tell exist.
        Apply(_settings);

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
        {
            // The title screen's world is live rather than a picture, so its camera keeps moving
            // while everything else is stopped.
            if (_screen == Screen.Title)
                _titleCamera.Step(seconds);

            return;
        }

        _playedSeconds += seconds;
        _clock.Step(seconds);

        // Breaking is held rather than clicked, so it belongs in the fixed step: a block that
        // took longer to break on a slower machine would be a block whose hardness was the frame
        // rate rather than the stone.
        if (_mining.Step(_world, _aimedAt, _isBreaking, seconds) is { } broken)
        {
            // Read before the block is taken out, because afterwards there is nothing there to
            // say what fell out of it.
            var fallen = _world.BlockAt(broken.X, broken.Y, broken.Z);
            if (_world.SetBlock(broken, BlockId.Air))
                _drops.Spawn(fallen, broken);
        }

        _drops.Step(_world, _world.Registry, seconds, _player.FeetMeters, _hotbar);
        _player.Step(_world, _world.Registry, seconds, _controller.ReadIntent(), _camera.Yaw);
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

            // Read here and used by the step, because a button is a thing that is held and a
            // step is where held things are acted on.
            _isBreaking = context.Window.IsMouseButtonDown(GlfwMouseButton.Left);
        }
        else if (_screen == Screen.Title)
        {
            _camera.Position = _titleCamera.EyeMeters;
            _controller.AimAt(_titleCamera.Yaw, _titleCamera.Pitch);
        }

        _centre = WorldCoordinates.ToChunk(WorldCoordinates.ToBlock(_camera.Position));
        _world.UpdateStreaming(_centre, _settings.ViewRadiusChunks, LoadBudgetPerFrame);
        _chunks.Update(_centre, MeshBudgetPerFrame);
    }

    /// <inheritdoc />
    public void DrawOffscreen(GameContext context)
    {
        // The whole frame's view of the world is decided here, before anything is recorded, so
        // that the shadow pass and the camera pass cannot disagree about where the sun is.
        var sky = SunCycle.At(_clock.DayFraction);
        _camera.FieldOfView = _settings.FieldOfViewRadians;
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

        var viewMeters = _settings.ViewRadiusChunks * Chunk.Size;
        _submerged = LiquidAtTheEye();
        var inLiquid = _submerged.W > 0f;

        // Turning fog off pushes it past the far plane rather than branching in the shader: the
        // point of the switch is to see how far the world really goes, and a world with no fog
        // and no far plane would be a different question.
        var fogStart = _settings.Fog ? viewMeters * FogStartFraction : viewMeters * 8f;
        var fogEnd = _settings.Fog ? viewMeters * FogEndFraction : viewMeters * 9f;

        _scene.Update(
            context.Renderer.FrameIndex,
            new SceneFrame
            {
                ViewProjection = _viewProjection,
                LightViewProjection = _lightViewProjection,
                Sky = sky,
                FogStartMeters = inLiquid ? UnderwaterSightMeters * 0.15f : fogStart,
                FogEndMeters = inLiquid ? UnderwaterSightMeters : fogEnd,
                Submerged = _submerged,
                ShadowStrength = _settings.Shadows ? _settings.ShadowStrength : 0f,
                Brightness = _settings.Brightness,
                Held = CarriedLight(),
                Field = FieldPlacement(),
                Breaking = BreakingBlock(),
            }
        );

        // Before the shadow pass, because a transfer cannot be recorded inside a render pass and
        // this is the gap between the command buffer opening and the first one beginning.
        _field.Update(context.Renderer.Commands, WorldCoordinates.ToBlock(_camera.Position));

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

        if (_drops.Count > 0)
        {
            _cubes.Begin();
            foreach (var drop in _drops.All)
            {
                var color = _world.Registry[drop.Block].Color;
                _cubes.Draw(
                    drop.MiddleMeters,
                    Drops.SizeMeters,
                    new float3(color.X / 255f, color.Y / 255f, color.Z / 255f),
                    drop.TurnRadians,
                    _camera.Position
                );
            }
        }

        // Last, so it sits over the world rather than under the water in front of it.
        if (_screen == Screen.Playing && _aimedAt is { } aimed)
            _outline.Draw(aimed.Block, _camera.Position);
    }

    /// <inheritdoc />
    public void DrawUi(GameContext context)
    {
        if (_noticeLeftSeconds > 0.0)
            _ui.DrawNotice(_notice, (float)(_noticeLeftSeconds / NoticeSeconds));

        switch (_screen)
        {
            case Screen.Title:
                Choose(
                    context,
                    _ui.DrawTitle(File.Exists(_options.SavePath), $"seed {_generator.Seed}")
                );
                break;

            case Screen.Playing:
                _ui.DrawCrosshair(_aimedAt is not null);
                _ui.DrawHotbar(_hotbar);
                break;

            case Screen.Paused:
                _ui.DrawHotbar(_hotbar);
                Choose(context, _ui.DrawPause(_options.SavePath));
                break;

            case Screen.Settings:
                DrawSettings(context);
                break;
        }
    }

    /// <summary>
    /// Draws the settings, applies anything moved at once, and writes them when the player leaves.
    /// </summary>
    /// <remarks>
    /// Written on the way out rather than on every frame a slider moves: dragging one produces a
    /// value a frame, and a file rewritten sixty times a second to record a field of view nobody
    /// has settled on yet is a file waiting to be truncated by a crash.
    /// </remarks>
    private void DrawSettings(GameContext context)
    {
        var settings = _settings;
        var choice = _ui.DrawSettings(ref settings, out var changed);

        if (changed)
            Apply(settings);

        if (choice == MenuChoice.Back)
            LeaveSettings();
    }

    /// <summary>Takes a new set of settings and makes everything that reads them agree.</summary>
    /// <remarks>
    /// Only two of them need anything done to them at all. The rest are read where they are used,
    /// every frame, which is why this is three lines rather than a list of every setting.
    /// </remarks>
    private void Apply(Settings settings)
    {
        _settings = settings.Clamped();

        _controller.Sensitivity = _settings.RadiansPerPixel;
        _clock.DayLengthSeconds = _settings.DayLengthSeconds;
        UiTheme.Apply(_settings.UiScale);
    }

    /// <summary>Goes back to whichever screen opened the settings, and writes them out.</summary>
    private void LeaveSettings()
    {
        _screen = _screenBehindSettings;

        try
        {
            SettingsFile.Write(_options.SettingsPath, _settings);
        }
        catch (IOException failure)
        {
            // Worth saying rather than throwing: a settings file that cannot be written is a
            // reason to lose the settings, not a reason to lose the world.
            Say($"Could not write {_options.SettingsPath}: {failure.Message}");
        }
        catch (UnauthorizedAccessException failure)
        {
            Say($"Could not write {_options.SettingsPath}: {failure.Message}");
        }
    }

    /// <inheritdoc />
    public void DrawOverlay()
    {
        ImGui.SeparatorText("Player"u8);
        ImGui.Text($"{_player.FeetMeters.X:F1}, {_player.FeetMeters.Y:F1}, {_player.FeetMeters.Z:F1}");
        ImGui.Text($"chunk {_centre.X}, {_centre.Y}, {_centre.Z}");
        ImGui.Text(_submerged.W > 0f ? "under water" : "in the open");
        ImGui.Text(
            _player.IsFlying
                ? "flying"
                : _player.IsSwimming
                    ? "swimming"
                    : _player.IsCrouching
                        ? "crouching"
                        : _player.IsOnGround
                            ? "on the ground"
                            : $"falling at {_player.FallSpeedMetersPerSecond:F1} m/s"
        );
        ImGui.Text($"{_player.SpeedMetersPerSecond:F1} m/s");
        ImGui.Text(
            _aimedAt is { } hit
                ? $"aimed at {hit.Block.X}, {hit.Block.Y}, {hit.Block.Z} ({_world.Registry[_world.BlockAt(hit.Block.X, hit.Block.Y, hit.Block.Z)].Name})"
                : "aimed at nothing in reach"
        );

        ImGui.SeparatorText("Sky"u8);
        ImGui.Text($"{_clock} (day {(int)_clock.DayFraction + 1})");

        ImGui.SeparatorText("World"u8);
        var under = WorldCoordinates.ToBlock(_player.FeetMeters);
        ImGui.Text($"seed {_generator.Seed}, radius {_settings.ViewRadiusChunks} chunks");
        ImGui.Text($"{_generator.BiomeAt(under.X, under.Z).Name}");
        ImGui.Text($"{_world.LoadedChunkCount} loaded, {_chunks.MeshCount} meshed");
        ImGui.Text($"{_world.StreamingCount} being generated, {_chunks.PendingCount} waiting to mesh");
        ImGui.Text($"{_world.Edits.Count} blocks changed, {_drops.Count} lying about");

        ImGui.SeparatorText("Drawn"u8);
        ImGui.Text($"{_chunks.DrawnChunkCount} chunks, {_chunks.DrawnTriangleCount} triangles");
        ImGui.Text($"{_chunks.ShadowedChunkCount} chunks into the shadow map");
        ImGui.Text($"{_chunks.ResidentBytes / (1024.0 * 1024.0):F1} MiB of geometry");

        ImGui.SeparatorText("Render"u8);
        var shadows = _settings.Shadows;
        if (ImGui.Checkbox("shadows"u8, ref shadows))
            Apply(_settings with { Shadows = shadows });

        ImGui.SameLine();

        var fog = _settings.Fog;
        if (ImGui.Checkbox("fog"u8, ref fog))
            Apply(_settings with { Fog = fog });

        var frozen = _clock.IsFrozen;
        if (ImGui.Checkbox("hold the sun"u8, ref frozen))
            _clock.IsFrozen = frozen;

        var hour = (float)_clock.Hour;
        if (ImGui.SliderFloat("hour"u8, ref hour, 0f, 24f))
            _clock.SetDayFraction(double.Floor(_clock.DayFraction) + (hour / 24.0));

        ImGui.SeparatorText("Controls"u8);
        ImGui.Text("WASD, space, shift to sprint, ctrl to crouch, F to fly"u8);
        ImGui.Text("left click breaks, right click places, 1-9 picks"u8);
        ImGui.Text("escape for the menu, F3 hides this"u8);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cubes.Dispose();
        _chunks.Dispose();
        _outline.Dispose();
        _sky.Dispose();
        _shadowPipeline.Dispose();
        _translucentPipeline.Dispose();
        _opaquePipeline.Dispose();
        _scene.Dispose();
        _field.Dispose();
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

            case Screen.Settings:
                LeaveSettings();
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

        // The wheel, the way every game of this kind does it: away from the hand moves left along
        // the row, which is the direction the numbers count down in.
        if (window.ScrollDelta != 0.0)
            _hotbar.Cycle(window.ScrollDelta > 0.0 ? -1 : 1);

        if (window.WasMouseButtonPressed(GlfwMouseButton.Right))
            Place();
    }

    /// <summary>Where the ray marching cube sits relative to the camera, in render space.</summary>
    /// <remarks>
    /// Worked out in f64 and narrowed afterwards, like every other distance this renderer hands
    /// to a shader: the cube's corner is a world block coordinate that can be thousands of
    /// metres from the origin, and subtracting the camera first is what keeps the result exact.
    /// </remarks>
    private float4 FieldPlacement()
    {
        // Nothing to march through is how reflections are turned off: the shader's first line is
        // a test of the cube's size, so the whole of it costs one comparison when it is not
        // wanted, and no pipeline has to be rebuilt to say so.
        if (!_field.IsReady || !_settings.Reflections)
            return float4.Zero;

        var corner = RenderSpace.ToRenderSpace(
            new double3(_field.LowestBlock.X, _field.LowestBlock.Y, _field.LowestBlock.Z),
            _camera.Position,
            1.0
        );

        return new float4(corner, VoxelField.SizeBlocks);
    }

    /// <summary>The block coming apart under the crosshair, and how far through it is.</summary>
    private float4 BreakingBlock() =>
        _mining.Target is { } target && _mining.Progress > 0f
            ? new float4(target.X, target.Y, target.Z, float.Clamp(_mining.Progress, 0f, 1f))
            : float4.Zero;

    /// <summary>The light the player is carrying, which is whatever is in their hand that glows.</summary>
    /// <remarks>
    /// <para>
    /// No separate torch and no separate key: the block in hand is the light. Selecting the
    /// lantern lights the way, and putting it down puts the light in the world where the baked
    /// grid takes it over — which is the same rule for the player as for the world, and one
    /// fewer thing to explain.
    /// </para>
    /// <para>
    /// Positioned in render space, where the camera is the origin, so the offset from the eye to
    /// the hand is the whole of it.
    /// </para>
    /// </remarks>
    private HeldLight CarriedLight()
    {
        var emission = _hotbar.SelectedEmission;
        if (_screen != Screen.Playing || emission == 0)
            return HeldLight.None;

        var flicker =
            1f
            - (FlickerAmount * 0.5f)
            + (FlickerAmount
                * 0.5f
                * (float)(double.Sin(_playedSeconds * 11.3) * double.Sin(_playedSeconds * 7.1)));

        var strength = emission / (float)LightVolume.MaxLevel * flicker;
        var right = _camera.Right;
        var forward = _camera.Forward;

        // The hand's offset, turned with the camera: up is the world's up, because a lantern
        // hangs the same way whatever the player is looking at.
        var offset =
            (right * HeldLightOffsetMeters.X)
            + new float3(0f, HeldLightOffsetMeters.Y, 0f)
            + (forward * HeldLightOffsetMeters.Z);

        return new HeldLight
        {
            OffsetMeters = offset,
            RangeMeters = HeldLightRangeMeters * strength,
            Color = HeldLightColor,
            Strength = strength,
        };
    }

    /// <summary>The colour of the liquid the eye is in, and nothing if it is in none.</summary>
    /// <remarks>
    /// Darkened, because a sea's colour as seen from above is light coming back off it, and from
    /// inside what is left is what got through.
    /// </remarks>
    private float4 LiquidAtTheEye()
    {
        var eye = WorldCoordinates.ToBlock(_camera.Position);
        var type = _world.Registry[_world.BlockAt(eye.X, eye.Y, eye.Z)];

        if (!type.IsLiquid)
            return float4.Zero;

        var linear = SrgbToLinear(type.Color);
        return new float4(linear.X * 0.3f, linear.Y * 0.3f, linear.Z * 0.3f, 1f);
    }

    /// <summary>
    /// Undoes the sRGB encoding of a block's colour, the way the vertex shader does.
    /// </summary>
    /// <remarks>
    /// The shaders work in linear light, so a colour handed to them straight out of the registry
    /// would be noticeably paler than the same block drawn as geometry.
    /// </remarks>
    private static float3 SrgbToLinear(byte3 color) =>
        new(Channel(color.X), Channel(color.Y), Channel(color.Z));

    private static float Channel(byte encoded)
    {
        var value = encoded / 255f;
        return value <= 0.04045f ? value / 12.92f : float.Pow((value + 0.055f) / 1.055f, 2.4f);
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

    private void Place()
    {
        if (_aimedAt is not { } hit)
            return;

        // A block placed where the player is standing would leave them inside it, and no answer
        // to that is a good one; refusing to place it is the only move that cannot go wrong.
        if (_player.WouldBeInsideMe(hit.Before))
            return;

        if (!_hotbar.HasSelection)
            return;

        // Spent only if it went down. A placement refused by the world — because something is
        // already there — must not cost the block, or a player tapping at a wall empties their
        // hand into nothing.
        if (_world.SetBlock(hit.Before, _hotbar.Selected))
            _hotbar.TakeOne();
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

            case MenuChoice.Settings:
                _screenBehindSettings = _screen;
                _screen = Screen.Settings;
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
        _hotbar.Clear();
        _drops.Clear();

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
            _hotbar.Restore(save.Carried, _blocks.Registry);
            _drops.Clear();
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
            Carried = _hotbar.Carried(_blocks.Registry),
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
