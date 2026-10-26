namespace BrutalShore.Gpu;

/// <summary>A cube of texels a shader can sample, written from the CPU a whole cube at a time.</summary>
/// <remarks>
/// <para>
/// The device side of anything a shader has to walk through rather than look up once: the world
/// around the camera, for a ray that has to find out what it hits. Nothing here knows what the
/// texels mean — it is four bytes a cell and a size, exactly as a 2D texture is — because what is
/// in them is a decision two layers up, where blocks exist.
/// </para>
/// <para>
/// One staging buffer, not one per frame in flight. A cube of any useful size is megabytes, and
/// three copies of it to allow an upload on every frame would be megabytes for nothing: the
/// contents only change when the camera has moved far enough to be looking at somewhere else.
/// <see cref="CanUpload" /> is what keeps that safe — it refuses a second upload until the first
/// is certainly finished, which is the same fence the renderer already waits on.
/// </para>
/// <para>
/// Sampled without filtering and clamped at the edges. A cell is a cell: averaging two of them
/// would produce a value that is neither, and a ray leaving the cube should find more of
/// whatever was at its edge rather than wrap around to the other side.
/// </para>
/// </remarks>
public sealed unsafe class Volume3D : IDisposable
{
    /// <summary>How many bytes one cell takes.</summary>
    public const int BytesPerCell = 4;

    private readonly GpuContext _context;
    private readonly VkImage _image;
    private readonly VkDeviceMemory _memory;
    private readonly VkBuffer _staging;
    private readonly VkDeviceMemory _stagingMemory;
    private readonly int _framesInFlight;

    private bool _everUploaded;
    private int _framesSinceUpload;

    /// <summary>Allocates the cube, its memory, its view, its sampler and one staging buffer.</summary>
    /// <param name="context">The device everything is allocated from.</param>
    /// <param name="size">How many cells the cube is on a side.</param>
    /// <param name="framesInFlight">
    /// How many frames the renderer may have in flight, which is how long an upload has to be
    /// left alone before the staging buffer can be written again.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The size or the frame count is not positive.</exception>
    public Volume3D(GpuContext context, int size, int framesInFlight)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), size, "A volume needs a positive size.");

        if (framesInFlight <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(framesInFlight),
                framesInFlight,
                "A volume needs to know how many frames may be in flight."
            );

        _context = context;
        _framesInFlight = framesInFlight;
        Size = size;
        CellCount = size * size * size;

        _image = context.Device.CreateImage(
            new VkImageCreateInfo
            {
                ImageType = VkImageType._3D,
                Format = VkFormat.R8G8B8A8UNorm,
                Extent = new VkExtent3D { Width = size, Height = size, Depth = size },
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = VkSampleCountFlags._1Bit,
                Tiling = VkImageTiling.Optimal,
                Usage = VkImageUsageFlags.TransferDstBit | VkImageUsageFlags.SampledBit,
                SharingMode = VkSharingMode.Exclusive,
                InitialLayout = VkImageLayout.Undefined,
            },
            null
        );

        var requirements = context.Device.GetImageMemoryRequirements(_image);
        _memory = context.Device.AllocateMemory(
            new VkMemoryAllocateInfo
            {
                AllocationSize = requirements.Size,
                MemoryTypeIndex = context.PhysicalDevice.FindMemoryType(
                    (int)requirements.MemoryTypeBits,
                    VkMemoryPropertyFlags.DeviceLocalBit
                ),
            },
            null
        );
        context.Device.BindImageMemory(_image, _memory, ByteSize.Zero);

        View = context.Device.CreateImageView(
            new VkImageViewCreateInfo
            {
                Image = _image,
                ViewType = VkImageViewType._3D,
                Format = VkFormat.R8G8B8A8UNorm,
                SubresourceRange = new VkImageSubresourceRange
                {
                    AspectMask = VkImageAspectFlags.ColorBit,
                    LevelCount = 1,
                    LayerCount = 1,
                },
            },
            null
        );

        Sampler = context.Device.CreateSampler(
            new VkSamplerCreateInfo
            {
                MagFilter = VkFilter.Nearest,
                MinFilter = VkFilter.Nearest,
                MipmapMode = VkSamplerMipmapMode.Nearest,
                AddressModeU = VkSamplerAddressMode.ClampToEdge,
                AddressModeV = VkSamplerAddressMode.ClampToEdge,
                AddressModeW = VkSamplerAddressMode.ClampToEdge,
                MaxLod = 1f,
            },
            null
        );

        (_staging, _stagingMemory) = CreateStaging(new ByteSize((nuint)(CellCount * BytesPerCell)));
    }

    /// <summary>How many cells the cube is on a side.</summary>
    public int Size { get; }

    /// <summary>How many cells there are in all.</summary>
    public int CellCount { get; }

    /// <summary>The view to bind in a descriptor set.</summary>
    public VkImageView View { get; }

    /// <summary>The sampler to bind beside <see cref="View" />.</summary>
    public VkSampler Sampler { get; }

    /// <summary>Whether anything has been uploaded, so the cube is safe to sample.</summary>
    public bool HasContents => _everUploaded;

    /// <summary>
    /// Whether the staging buffer is free, meaning any earlier upload has certainly finished.
    /// </summary>
    /// <remarks>
    /// Counted in frames rather than asked of a fence, because the renderer already waits on the
    /// fence of the frame it is about to record: once as many frames have passed as may be in
    /// flight, the commands that read the staging buffer have been through that wait.
    /// </remarks>
    public bool CanUpload => !_everUploaded || _framesSinceUpload >= _framesInFlight;

    /// <summary>Counts a frame towards the staging buffer being free again.</summary>
    /// <remarks>Called once a frame by whoever owns the cube, whether or not anything was uploaded.</remarks>
    public void EndFrame()
    {
        if (_framesSinceUpload < int.MaxValue)
            _framesSinceUpload++;
    }

    /// <summary>Copies a whole cube of cells in, recording the transfer into a command buffer.</summary>
    /// <param name="commands">The command buffer being recorded, outside any render pass.</param>
    /// <param name="cells">
    /// The cells, four bytes each, with x varying fastest and z slowest. Must be exactly
    /// <see cref="CellCount" /> cells long.
    /// </param>
    /// <exception cref="ArgumentException">The span is not the size of the cube.</exception>
    /// <exception cref="InvalidOperationException">An earlier upload may still be in flight.</exception>
    public void Upload(CommandBuffer commands, ReadOnlySpan<byte> cells)
    {
        var expected = CellCount * BytesPerCell;
        if (cells.Length != expected)
            throw new ArgumentException($"A volume of {Size}³ takes {expected} bytes, not {cells.Length}.", nameof(cells));

        if (!CanUpload)
            throw new InvalidOperationException("The staging buffer is still being read by an earlier frame.");

        // Coherent memory needs no flush: the write is visible to the device as soon as it lands.
        _context.Device.WriteMemory(cells, _stagingMemory);

        // Undefined rather than the layout it was in: the whole image is being replaced, so
        // whatever was there does not have to be preserved, and saying so lets the driver skip a
        // decompression it would otherwise have to do.
        Barrier(
            commands,
            VkImageLayout.Undefined,
            VkImageLayout.TransferDstOptimal,
            VkAccessFlags.None,
            VkAccessFlags.TransferWriteBit,
            VkPipelineStageFlags.TopOfPipeBit,
            VkPipelineStageFlags.TransferBit
        );

        commands.CopyBufferToImage(
            _staging,
            _image,
            VkImageLayout.TransferDstOptimal,
            [
                new VkBufferImageCopy
                {
                    ImageSubresource = new VkImageSubresourceLayers
                    {
                        AspectMask = VkImageAspectFlags.ColorBit,
                        LayerCount = 1,
                    },
                    ImageExtent = new VkExtent3D { Width = Size, Height = Size, Depth = Size },
                },
            ]
        );

        Barrier(
            commands,
            VkImageLayout.TransferDstOptimal,
            VkImageLayout.ShaderReadOnlyOptimal,
            VkAccessFlags.TransferWriteBit,
            VkAccessFlags.ShaderReadBit,
            VkPipelineStageFlags.TransferBit,
            VkPipelineStageFlags.FragmentShaderBit
        );

        _everUploaded = true;
        _framesSinceUpload = 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Device.DestroyBuffer(_staging, null);
        _context.Device.FreeMemory(_stagingMemory, null);
        _context.Device.DestroySampler(Sampler, null);
        _context.Device.DestroyImageView(View, null);
        _context.Device.DestroyImage(_image, null);
        _context.Device.FreeMemory(_memory, null);
    }

    private (VkBuffer Buffer, VkDeviceMemory Memory) CreateStaging(ByteSize size)
    {
        var buffer = _context.Device.CreateBuffer(
            new VkBufferCreateInfo
            {
                Size = size,
                Usage = VkBufferUsageFlags.TransferSrcBit,
                SharingMode = VkSharingMode.Exclusive,
            },
            null
        );

        var requirements = _context.Device.GetBufferMemoryRequirements(buffer);
        var memory = _context.Device.AllocateMemory(
            new VkMemoryAllocateInfo
            {
                AllocationSize = requirements.Size,
                MemoryTypeIndex = _context.PhysicalDevice.FindMemoryType(
                    (int)requirements.MemoryTypeBits,
                    VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit
                ),
            },
            null
        );

        _context.Device.BindBufferMemory(buffer, memory, ByteSize.Zero);
        return (buffer, memory);
    }

    private void Barrier(
        CommandBuffer commands,
        VkImageLayout from,
        VkImageLayout to,
        VkAccessFlags before,
        VkAccessFlags after,
        VkPipelineStageFlags producer,
        VkPipelineStageFlags consumer
    ) =>
        commands.PipelineBarrier(
            producer,
            consumer,
            VkDependencyFlags.None,
            [],
            [],
            [
                new VkImageMemoryBarrier
                {
                    OldLayout = from,
                    NewLayout = to,
                    SrcAccessMask = before,
                    DstAccessMask = after,
                    SrcQueueFamilyIndex = unchecked((int)uint.MaxValue),
                    DstQueueFamilyIndex = unchecked((int)uint.MaxValue),
                    Image = _image,
                    SubresourceRange = new VkImageSubresourceRange
                    {
                        AspectMask = VkImageAspectFlags.ColorBit,
                        LevelCount = 1,
                        LayerCount = 1,
                    },
                },
            ]
        );
}
