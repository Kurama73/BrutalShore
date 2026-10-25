using BrutalShore.Core;
using BrutalShore.Gpu;
using BrutalShore.Voxel;
using BrutalShore.World;

namespace BrutalShore.Render;

/// <summary>
/// Keeps one GPU mesh per loaded chunk in step with the world, and draws the ones the camera can
/// see.
/// </summary>
/// <remarks>
/// <para>
/// Remeshing is budgeted and ordered, never exhaustive. A chunk edit dirties up to four chunks and
/// a streaming step loads a whole ring of them, so a frame that insisted on meshing everything
/// outstanding would stall for as long as that took. Instead each frame takes the nearest few
/// candidates, which is both what the player is most likely to be looking at and what finishes
/// soonest.
/// </para>
/// <para>
/// A chunk is only meshed once its whole neighbourhood is resident, because the mesher culls
/// boundary faces against neighbours: meshing early would seal a chunk against air that is merely
/// not loaded yet, and that wall stays until something dirties the chunk again.
/// </para>
/// </remarks>
public sealed class ChunkRenderer : IDisposable
{
    private readonly GpuContext _context;
    private readonly GpuRenderer _renderer;
    private readonly VoxelWorld _world;
    private readonly VoxelPipeline _opaquePipeline;
    private readonly VoxelPipeline _translucentPipeline;
    private readonly SceneBindings _scene;

    private readonly MeshStreamer _mesher;
    private readonly Dictionary<int3, ChunkMesh> _meshes = [];

    // Reused every frame: the dirty set cannot be enumerated while it is being cleared, and the
    // candidates have to be sorted before any of them is asked for.
    private readonly List<int3> _candidates = [];
    private readonly List<int3> _stale = [];
    private readonly List<int3> _visible = [];

    // Built once rather than per frame. A comparison written inline captures the centre, so the
    // sort below would allocate a closure and a delegate on every frame of every session.
    private readonly Comparison<int3> _nearestFirst;
    private readonly Comparison<int3> _furthestFirst;

    private int3 _sortCentre;

    /// <summary>Wires the renderer to a world and a pipeline.</summary>
    /// <param name="context">The device meshes are uploaded to.</param>
    /// <param name="renderer">The renderer whose frame chunks are recorded into.</param>
    /// <param name="world">The world whose chunks are meshed and drawn.</param>
    /// <param name="opaquePipeline">The pipeline that draws the solid half of a chunk.</param>
    /// <param name="translucentPipeline">The pipeline that draws the see-through half.</param>
    /// <param name="scene">The per-frame uniform block the shaders read.</param>
    /// <param name="mesher">
    /// The workers that build the meshes. The renderer takes ownership and disposes it, because
    /// a mesh one of them is part way through is one only this will ever upload.
    /// </param>
    public ChunkRenderer(
        GpuContext context,
        GpuRenderer renderer,
        VoxelWorld world,
        VoxelPipeline opaquePipeline,
        VoxelPipeline translucentPipeline,
        SceneBindings scene,
        MeshStreamer mesher
    )
    {
        _context = context;
        _renderer = renderer;
        _world = world;
        _opaquePipeline = opaquePipeline;
        _translucentPipeline = translucentPipeline;
        _scene = scene;
        _mesher = mesher;
        _nearestFirst = (first, second) =>
            SquaredDistance(first, _sortCentre).CompareTo(SquaredDistance(second, _sortCentre));
        _furthestFirst = (first, second) =>
            SquaredDistance(second, _sortCentre).CompareTo(SquaredDistance(first, _sortCentre));
    }

    /// <summary>How many chunks have a mesh on the GPU.</summary>
    public int MeshCount => _meshes.Count;

    /// <summary>How many chunks were drawn by the last <see cref="Draw" />.</summary>
    public int DrawnChunkCount { get; private set; }

    /// <summary>How many triangles were drawn by the last <see cref="Draw" />.</summary>
    public int DrawnTriangleCount { get; private set; }

    /// <summary>How many chunks the last <see cref="DrawShadow" /> wrote into the map.</summary>
    public int ShadowedChunkCount { get; private set; }

    /// <summary>How many dirty chunks were still waiting after the last <see cref="Update" />.</summary>
    public int PendingCount { get; private set; }

    /// <summary>How many bytes of vertex and index data are resident on the GPU.</summary>
    public long ResidentBytes { get; private set; }

    /// <summary>
    /// Meshes up to <paramref name="budget" /> dirty chunks, nearest first, and drops the meshes of
    /// chunks that are no longer loaded.
    /// </summary>
    /// <param name="centreChunk">The chunk the camera is in; meshing is ordered outwards from it.</param>
    /// <param name="budget">The most chunks to mesh in this call.</param>
    /// <returns>How many chunks were meshed.</returns>
    public int Update(int3 centreChunk, int budget)
    {
        DropUnloaded();

        _candidates.Clear();
        foreach (var coordinate in _world.Dirty)
        {
            if (_world.IsMeshable(coordinate))
                _candidates.Add(coordinate);
        }

        _sortCentre = centreChunk;
        _candidates.Sort(_nearestFirst);

        // Ask for as many as the workers will take, nearest first. A chunk the queue refuses
        // stays dirty and is asked for again once there is room — which is also what rebuilds a
        // chunk edited while its mesh was in flight, rather than losing the edit.
        var handedOver = 0;
        foreach (var coordinate in _candidates)
        {
            if (_world.CaptureNeighbourhood(coordinate) is not { } neighbourhood)
                continue;

            if (!_mesher.Request(neighbourhood))
                break;

            _world.ClearDirty(coordinate);
            handedOver++;
        }

        // What is still owed a mesh: the ones the queue had no room for, plus the ones being
        // built. Chunks that are dirty but cannot be meshed yet are not counted — the outermost
        // ring of the loaded world is always one of those, and it is not a backlog.
        PendingCount = (_candidates.Count - handedOver) + _mesher.OutstandingCount;

        // Uploading is the only part that has to happen here, because it is the only part that
        // touches the device.
        return _mesher.Collect(budget, Upload);
    }

    /// <summary>Draws every visible chunk mesh into the frame being recorded.</summary>
    /// <param name="camera">The camera to draw from; its position is the render-space origin.</param>
    /// <param name="viewProjection">
    /// That camera's view-projection. It is passed in rather than rebuilt here because the
    /// scene uniform block needs the same matrix, and two of them would be two chances to differ.
    /// </param>
    public void Draw(Camera camera, in float4x4 viewProjection)
    {
        var commands = _renderer.Commands;

        commands.BindPipeline(VkPipelineBindPoint.Graphics, _opaquePipeline.Handle);
        _scene.Bind(commands, _opaquePipeline.Layout, _renderer.FrameIndex);

        (DrawnChunkCount, DrawnTriangleCount) = Record(
            _opaquePipeline.Layout,
            camera,
            viewProjection,
            VoxelPass.Opaque
        );

        // The see-through half afterwards, so it has a world behind it to blend into. Nearest
        // last: a blend is not commutative, and two surfaces drawn the wrong way round show the
        // far one over the near one.
        commands.BindPipeline(VkPipelineBindPoint.Graphics, _translucentPipeline.Handle);
        _scene.Bind(commands, _translucentPipeline.Layout, _renderer.FrameIndex);

        var (chunks, triangles) = Record(
            _translucentPipeline.Layout,
            camera,
            viewProjection,
            VoxelPass.Translucent
        );

        DrawnChunkCount += chunks;
        DrawnTriangleCount += triangles;
    }

    /// <summary>Draws every chunk the light can see into the shadow map.</summary>
    /// <param name="pipeline">The depth-only pipeline that writes the map.</param>
    /// <param name="camera">The camera; its position is still the render-space origin.</param>
    /// <param name="lightViewProjection">Render space to the shadow map's clip space.</param>
    /// <remarks>
    /// The same meshes as <see cref="Draw" />, culled against the light's box instead of the
    /// camera's frustum — a chunk behind the camera can still cast into what the camera sees.
    /// </remarks>
    public void DrawShadow(ShadowPipeline pipeline, Camera camera, in float4x4 lightViewProjection)
    {
        var commands = _renderer.Commands;

        commands.BindPipeline(VkPipelineBindPoint.Graphics, pipeline.Handle);
        _scene.Bind(commands, pipeline.Layout, _renderer.FrameIndex);

        // Only the solid half casts. Water that cast a full shadow would put the seabed in
        // permanent night, and a shadow map has no way to say "a little".
        (ShadowedChunkCount, _) = Record(pipeline.Layout, camera, lightViewProjection, VoxelPass.Opaque);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.WaitIdle();

        foreach (var mesh in _meshes.Values)
            mesh.Dispose();

        _meshes.Clear();
        ResidentBytes = 0;
    }

    /// <summary>
    /// Records a draw call for every mesh inside a frustum, and reports what it drew.
    /// </summary>
    /// <remarks>
    /// Shared by the camera pass and the shadow pass because the two differ only in which matrix
    /// decides what is visible. The pipeline and the descriptor set are already bound; all this
    /// adds per chunk is the one thing that changes, which is where the chunk is.
    /// </remarks>
    private (int Chunks, int Triangles) Record(
        VkPipelineLayout layout,
        Camera camera,
        in float4x4 viewProjection,
        VoxelPass pass
    )
    {
        var frustum = ViewFrustum.FromViewProjection(viewProjection);
        var commands = _renderer.Commands;
        var chunks = 0;
        var triangles = 0;

        foreach (var coordinate in VisibleInOrder(camera, frustum, pass))
        {
            var half = pass == VoxelPass.Opaque ? _meshes[coordinate].Opaque : _meshes[coordinate].Translucent;
            if (half is null)
                continue;

            // The chunk's corner relative to the camera, narrowed to f32 only after the
            // subtraction: this is the floating origin, and the one place it has to happen.
            var origin = RenderSpace.ToRenderSpace(WorldOrigin(coordinate), camera.Position, 1.0);

            commands.BindVertexBuffer(0, half.Vertices.Handle);
            commands.BindIndexBuffer(half.Indices.Handle, ByteSize.Zero, VkIndexType.UInt32);
            commands.PushConstants(
                layout,
                VkShaderStageFlags.VertexBit,
                ByteSize.Zero,
                new VoxelPushConstants(origin, WorldCoordinates.ChunkOrigin(coordinate))
            );
            commands.DrawIndexed(half.Indices.Count, 1, 0, 0, 0);

            chunks++;
            triangles += half.Indices.Count / 3;
        }

        return (chunks, triangles);
    }

    /// <summary>The chunks a frustum reaches, in the order this pass wants to draw them.</summary>
    /// <remarks>
    /// The solid pass is left in whatever order the map hands them over: the depth buffer sorts
    /// it, and a sort would be work for nothing. The see-through pass is sorted far to near,
    /// because blending has to happen in that order to look right, and because there are few
    /// enough of them that sorting is cheap.
    /// </remarks>
    private List<int3> VisibleInOrder(Camera camera, in ViewFrustum frustum, VoxelPass pass)
    {
        _visible.Clear();

        foreach (var (coordinate, mesh) in _meshes)
        {
            if ((pass == VoxelPass.Opaque ? mesh.Opaque : mesh.Translucent) is null)
                continue;

            var origin = RenderSpace.ToRenderSpace(WorldOrigin(coordinate), camera.Position, 1.0);
            if (frustum.Intersects(origin, origin + new float3(Chunk.Size)))
                _visible.Add(coordinate);
        }

        if (pass == VoxelPass.Translucent)
        {
            _sortCentre = WorldCoordinates.ToChunk(WorldCoordinates.ToBlock(camera.Position));
            _visible.Sort(_furthestFirst);
        }

        return _visible;
    }

    private static double3 WorldOrigin(int3 chunkCoordinate)
    {
        var origin = WorldCoordinates.ChunkOrigin(chunkCoordinate);
        return new double3(origin.X, origin.Y, origin.Z);
    }

    private static long SquaredDistance(int3 first, int3 second)
    {
        long x = first.X - second.X;
        long y = first.Y - second.Y;
        long z = first.Z - second.Z;
        return (x * x) + (y * y) + (z * z);
    }

    /// <summary>Copies a finished surface into GPU buffers and swaps it for the old one.</summary>
    /// <remarks>
    /// The surface belongs to the streamer and is reused the moment this returns, so everything
    /// wanted from it is copied here and nothing is kept.
    /// </remarks>
    private void Upload(int3 coordinate, ChunkSurface surface)
    {
        Release(coordinate);

        // A chunk of solid stone underground, or of sky, meshes to nothing at all. Keeping no
        // entry for it is what makes an empty chunk cost nothing to draw rather than an empty draw
        // call, and Vulkan would reject a zero-sized buffer anyway. A chunk unloaded while its
        // mesh was being built is the same case: geometry for ground the world has forgotten.
        if (surface.IsEmpty || _world.Find(coordinate) is null)
            return;

        var mesh = new ChunkMesh(Copy(surface.Opaque), Copy(surface.Translucent));
        _meshes[coordinate] = mesh;
        ResidentBytes += mesh.Bytes;
    }

    /// <summary>Puts one half of a surface on the GPU, or nothing if that half is empty.</summary>
    private MeshBuffers? Copy(VoxelMesh mesh) =>
        mesh.IsEmpty
            ? null
            : new MeshBuffers(
                GpuBuffer.Create(_context, mesh.Vertices, VkBufferUsageFlags.VertexBufferBit),
                GpuBuffer.Create(_context, mesh.Indices, VkBufferUsageFlags.IndexBufferBit)
            );

    private void DropUnloaded()
    {
        _stale.Clear();
        foreach (var coordinate in _meshes.Keys)
        {
            if (_world.Find(coordinate) is null)
                _stale.Add(coordinate);
        }

        foreach (var coordinate in _stale)
            Release(coordinate);
    }

    /// <summary>
    /// Gives up a chunk's mesh, leaving the renderer to free it once the GPU is done with it.
    /// </summary>
    private void Release(int3 coordinate)
    {
        if (!_meshes.Remove(coordinate, out var mesh))
            return;

        ResidentBytes -= mesh.Bytes;

        // A frame submitted moments ago may still be drawing this mesh, so it cannot be destroyed
        // here; the renderer disposes it once that frame's fence has been waited on.
        _renderer.Retire(mesh);
    }

    /// <summary>One chunk's geometry, as the GPU holds it.</summary>
    /// <summary>One half of a chunk's geometry, as the two buffers it is drawn from.</summary>
    private sealed record MeshBuffers(GpuBuffer Vertices, GpuBuffer Indices) : IDisposable
    {
        public long Bytes => (long)(int)Vertices.Size + (int)Indices.Size;

        public void Dispose()
        {
            Vertices.Dispose();
            Indices.Dispose();
        }
    }

    /// <summary>A chunk on the GPU. Either half may be absent; both never are.</summary>
    private sealed record ChunkMesh(MeshBuffers? Opaque, MeshBuffers? Translucent) : IDisposable
    {
        public long Bytes => (Opaque?.Bytes ?? 0) + (Translucent?.Bytes ?? 0);

        public void Dispose()
        {
            Opaque?.Dispose();
            Translucent?.Dispose();
        }
    }
}
