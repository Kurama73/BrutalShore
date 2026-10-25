using Brutal.ImGuiApi;
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

    private readonly Options _options;
    private readonly GameBlocks _blocks;
    private readonly TerrainGenerator _generator;
    private readonly VoxelWorld _world;
    private readonly Camera _camera = new();

    // Assigned in Load, because each of these needs a device, and there is none until the host has
    // opened a window.
    private PlayerController _controller = null!;
    private VoxelPipeline _pipeline = null!;
    private ChunkRenderer _chunks = null!;

    private int3 _centre;

    /// <summary>Builds the world, which needs nothing from the GPU.</summary>
    public ShoreboundGame(Options options)
    {
        _options = options;
        _blocks = new GameBlocks(new BlockRegistry());
        _generator = new TerrainGenerator(_blocks, options.Seed);
        _world = new VoxelWorld(_blocks.Registry, _generator);
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
        _pipeline = new VoxelPipeline(context.Gpu, context.Shaders, context.Target.RenderPass);
        _chunks = new ChunkRenderer(context.Gpu, context.Renderer, _world, _pipeline);
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
    public void Step(double seconds) => _controller.Step(seconds);

    /// <inheritdoc />
    public void Update(GameContext context)
    {
        _centre = WorldCoordinates.ToChunk(WorldCoordinates.ToBlock(_camera.Position));
        _world.UpdateStreaming(_centre, _options.ViewRadiusChunks, LoadBudgetPerFrame);
        _chunks.Update(_centre, MeshBudgetPerFrame);
    }

    /// <inheritdoc />
    public void Draw(GameContext context) => _chunks.Draw(_camera, context.AspectRatio);

    /// <inheritdoc />
    public void DrawOverlay()
    {
        ImGui.SeparatorText("Position"u8);
        ImGui.Text($"{_camera.Position.X:F1}, {_camera.Position.Y:F1}, {_camera.Position.Z:F1}");
        ImGui.Text($"chunk {_centre.X}, {_centre.Y}, {_centre.Z}");

        ImGui.SeparatorText("World"u8);
        ImGui.Text($"seed {_generator.Seed}, radius {_options.ViewRadiusChunks} chunks");
        ImGui.Text($"{_world.LoadedChunkCount} loaded, {_chunks.MeshCount} meshed");
        ImGui.Text($"{_chunks.PendingCount} waiting to mesh");

        ImGui.SeparatorText("Drawn"u8);
        ImGui.Text($"{_chunks.DrawnChunkCount} chunks, {_chunks.DrawnTriangleCount} triangles");
        ImGui.Text($"{_chunks.ResidentBytes / (1024.0 * 1024.0):F1} MiB of geometry");

        ImGui.SeparatorText("Controls"u8);
        ImGui.Text("click to look, WASD, space/ctrl, shift"u8);
        ImGui.Text("escape releases the mouse"u8);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _chunks.Dispose();
        _pipeline.Dispose();
    }
}
