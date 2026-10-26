using System.Collections.Concurrent;
using BrutalShore.Gpu;
using BrutalShore.Voxel;
using BrutalShore.World;

namespace BrutalShore.Render;

/// <summary>The world around the camera as a cube of texels, for a shader to send rays through.</summary>
/// <remarks>
/// <para>
/// The renderer draws what the camera can see. Anything that asks a question about what it
/// <em>cannot</em> — what is above this water, what is behind that wall, what would a ray hit —
/// has to be able to look somewhere other than the screen, and a voxel world makes that cheap:
/// the geometry is already a regular grid, so a ray through it is a walk along integers rather
/// than a search through a tree.
/// </para>
/// <para>
/// A cell holds a block's colour and what kind of block it is, which is everything a ray needs to
/// decide whether it stopped and what colour it stopped against. Block ids are deliberately not
/// in it: the shader would then need the registry too, and a colour is what a hit is worth.
/// </para>
/// <para>
/// Re-centred rather than scrolled. Moving the contents to follow the camera would save most of
/// the filling, and cost a modulo in every one of the shader's steps; refilling the whole cube
/// once every <see cref="DriftBlocks" /> blocks of movement is a few tens of milliseconds on a
/// worker thread, which is where it happens.
/// </para>
/// </remarks>
public sealed class VoxelField : IDisposable
{
    /// <summary>How many blocks the cube is on a side.</summary>
    /// <remarks>
    /// 96 is a reflection of the far bank of a river rather than of the next valley, which is
    /// what the effect is for. It is also three megabytes and a quarter of a second of filling,
    /// and both of those double long before the picture improves twice as much.
    /// </remarks>
    public const int SizeBlocks = 96;

    /// <summary>How far the camera moves before the cube is filled again, in blocks.</summary>
    /// <remarks>
    /// A sixth of the cube. Smaller means filling it more often for a margin nothing can see;
    /// larger means the camera reaching the edge of what the field knows about, where a ray
    /// leaves the cube and has to guess.
    /// </remarks>
    private const int DriftBlocks = SizeBlocks / 6;

    /// <summary>The fewest frames between two fills asked for by the world changing.</summary>
    /// <remarks>
    /// Streaming changes the world a few times a frame while it is filling in, and the cube has
    /// no way to update a part of itself — so without a limit it would be refilled continuously
    /// and never be anything but out of date. A third of a second is fast enough that a block
    /// placed in front of a lake is reflected in it almost at once.
    /// </remarks>
    private const int FramesBetweenRefills = 20;

    /// <summary>What a cell's last byte says the block is.</summary>
    private const byte Empty = 0;
    private const byte SeeThrough = 128;
    private const byte Solid = 255;

    private readonly VoxelWorld _world;
    private readonly Volume3D _volume;
    private readonly byte[] _cells;
    private readonly ConcurrentQueue<Filled> _finished = new();

    private int3 _centreBlock;
    private int3 _filledLowest;
    private int _filledRevision = -1;
    private int _framesSinceFill;
    private bool _filling;
    private bool _everAsked;

    /// <summary>Creates the cube on the device and the buffer the worker fills.</summary>
    /// <param name="context">The device the cube is allocated from.</param>
    /// <param name="world">The world to read blocks from, on the main thread only.</param>
    public VoxelField(GpuContext context, VoxelWorld world)
    {
        _world = world;
        _volume = new Volume3D(context, SizeBlocks, GpuRenderer.MaxFramesInFlight);
        _cells = new byte[_volume.CellCount * Volume3D.BytesPerCell];
    }

    /// <summary>The cube's view, to bind in a descriptor set.</summary>
    public VkImageView View => _volume.View;

    /// <summary>The cube's sampler, to bind beside the view.</summary>
    public VkSampler Sampler => _volume.Sampler;

    /// <summary>Whether the cube holds anything yet, so a shader may walk it.</summary>
    public bool IsReady => _volume.HasContents;

    /// <summary>The world block the cube's lowest corner sits at.</summary>
    public int3 LowestBlock => _filledLowest;

    /// <summary>Whether a fill is running on a worker right now.</summary>
    public bool IsFilling => _filling;

    /// <summary>
    /// Starts a fill if the camera has drifted far enough, and uploads whatever a worker has
    /// finished.
    /// </summary>
    /// <param name="commands">The command buffer being recorded, outside any render pass.</param>
    /// <param name="cameraBlock">The block the camera is in.</param>
    /// <remarks>
    /// Main thread only, and before the frame's render pass opens: the upload is a transfer, and
    /// Vulkan allows none inside a pass.
    /// </remarks>
    public void Update(CommandBuffer commands, int3 cameraBlock)
    {
        _volume.EndFrame();

        if (_finished.TryDequeue(out var filled))
        {
            _filling = false;

            if (_volume.CanUpload)
            {
                filled.Cells.CopyTo(_cells, 0);
                _filledLowest = filled.Lowest;
                _volume.Upload(commands, _cells);
            }
        }

        _framesSinceFill++;

        if (_filling || !NeedsFilling(cameraBlock))
            return;

        _centreBlock = cameraBlock;
        _everAsked = true;
        _framesSinceFill = 0;
        StartFill(cameraBlock);
    }

    /// <inheritdoc />
    public void Dispose() => _volume.Dispose();

    /// <summary>Whether the cube no longer describes the world the camera is standing in.</summary>
    /// <remarks>
    /// Two reasons, and the second is the one that is easy to forget: the camera has moved far
    /// enough that the cube is centred somewhere else, or the world inside the cube has changed.
    /// A camera that never moves at all — a fixed viewpoint for a screenshot — is the case where
    /// only the second applies, and without it the cube keeps whatever the world looked like
    /// before a single chunk had finished streaming in.
    /// </remarks>
    private bool NeedsFilling(int3 cameraBlock)
    {
        if (!_everAsked)
            return true;

        var moved = cameraBlock - _centreBlock;
        if (int.Max(int.Abs(moved.X), int.Max(int.Abs(moved.Y), int.Abs(moved.Z))) >= DriftBlocks)
            return true;

        return _world.Revision != _filledRevision && _framesSinceFill >= FramesBetweenRefills;
    }

    /// <summary>
    /// Takes a snapshot of the chunks the cube covers and hands it to the thread pool to read.
    /// </summary>
    /// <remarks>
    /// The snapshot is taken here, on the main thread, for the same reason a mesh worker is given
    /// one: the world's dictionary is the main thread's to change, and a worker walking it while
    /// it is being resized is a fault that shows up as a hang.
    /// </remarks>
    private void StartFill(int3 cameraBlock)
    {
        var half = SizeBlocks / 2;
        var lowest = cameraBlock - new int3(half, half, half);
        var size = new int3(SizeBlocks, SizeBlocks, SizeBlocks);
        var region = new ChunkRegion(lowest, size, _world.Find);
        var registry = _world.Registry;

        // Noted before the worker starts rather than after it finishes: the world is free to
        // change while it runs, and a fill is a picture of the world as it was when the chunks
        // were captured.
        _filledRevision = _world.Revision;
        _filling = true;

        ThreadPool.UnsafeQueueUserWorkItem(
            _ =>
            {
                var cells = new byte[_volume.CellCount * Volume3D.BytesPerCell];
                Fill(cells, region, registry, lowest);
                _finished.Enqueue(new Filled(lowest, cells));
            },
            null
        );
    }

    /// <summary>Writes one cube of cells from the blocks a region holds.</summary>
    /// <remarks>
    /// x fastest and z slowest, which is the order a 3D image copy expects its rows in. Air is
    /// left at zero rather than written, which is most of the cube and most of the time saved.
    /// </remarks>
    private static void Fill(byte[] cells, ChunkRegion region, BlockRegistry registry, int3 lowest)
    {
        var index = 0;

        for (var z = 0; z < SizeBlocks; z++)
        for (var y = 0; y < SizeBlocks; y++)
        for (var x = 0; x < SizeBlocks; x++, index += Volume3D.BytesPerCell)
        {
            var block = region.BlockAt(lowest.X + x, lowest.Y + y, lowest.Z + z);
            if (block.IsAir)
            {
                cells[index + 3] = Empty;
                continue;
            }

            var type = registry[block];
            cells[index + 0] = type.Color.X;
            cells[index + 1] = type.Color.Y;
            cells[index + 2] = type.Color.Z;
            cells[index + 3] = type.IsTranslucent ? SeeThrough : Solid;
        }
    }

    private readonly record struct Filled(int3 Lowest, byte[] Cells);
}
