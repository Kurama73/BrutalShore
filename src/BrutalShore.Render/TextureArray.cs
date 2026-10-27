using BrutalShore.Gpu;

namespace BrutalShore.Render;

/// <summary>Uploads and samples a fixed-size array of RGBA texture layers.</summary>
/// <remarks>
/// One array keeps a block's texture choice in its compact vertex data; coordinates can repeat
/// across greedy-merged faces without packing or bleeding between atlas tiles.
/// </remarks>
public sealed class TextureArray : IDisposable
{
    private readonly GpuContext _context;
    private readonly VkImage _image;
    private readonly VkDeviceMemory _memory;
    private readonly GpuBuffer _staging;
    private bool _uploaded;

    /// <summary>Creates a texture array from tightly packed RGBA8 pixels.</summary>
    /// <param name="context">The device to allocate the array on.</param>
    /// <param name="width">The width of every layer in pixels.</param>
    /// <param name="height">The height of every layer in pixels.</param>
    /// <param name="layers">The number of layers.</param>
    /// <param name="rgbaPixels">Layer-major, top-to-bottom RGBA8 pixel data.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension or layer count is not positive.</exception>
    /// <exception cref="ArgumentException">The pixel data does not match the array dimensions.</exception>
    public TextureArray(GpuContext context, int width, int height, int layers, ReadOnlySpan<byte> rgbaPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(layers);

        var expectedBytes = checked(width * height * layers * 4);
        if (rgbaPixels.Length != expectedBytes)
            throw new ArgumentException($"A {width}x{height} array with {layers} layers takes {expectedBytes} bytes.", nameof(rgbaPixels));

        _context = context;
        Width = width;
        Height = height;
        Layers = layers;

        _image = context.Device.CreateImage(
            new VkImageCreateInfo
            {
                ImageType = VkImageType._2D,
                Format = VkFormat.R8G8B8A8UNorm,
                Extent = new VkExtent3D { Width = width, Height = height, Depth = 1 },
                MipLevels = 1,
                ArrayLayers = layers,
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
                ViewType = VkImageViewType._2DArray,
                Format = VkFormat.R8G8B8A8UNorm,
                SubresourceRange = new VkImageSubresourceRange
                {
                    AspectMask = VkImageAspectFlags.ColorBit,
                    LevelCount = 1,
                    LayerCount = layers,
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
                AddressModeU = VkSamplerAddressMode.Repeat,
                AddressModeV = VkSamplerAddressMode.Repeat,
                AddressModeW = VkSamplerAddressMode.ClampToEdge,
                MaxLod = 1f,
            },
            null
        );

        _staging = GpuBuffer.Create(context, rgbaPixels, VkBufferUsageFlags.TransferSrcBit);
    }

    /// <summary>The width of every texture layer.</summary>
    public int Width { get; }

    /// <summary>The height of every texture layer.</summary>
    public int Height { get; }

    /// <summary>The number of texture layers.</summary>
    public int Layers { get; }

    /// <summary>The view bound to shaders.</summary>
    public VkImageView View { get; }

    /// <summary>The sampler bound to shaders.</summary>
    public VkSampler Sampler { get; }

    /// <summary>Whether the one-time transfer has been recorded.</summary>
    public bool HasBeenUploaded => _uploaded;

    /// <summary>Records the one-time transfer into an open command buffer outside a render pass.</summary>
    /// <param name="commands">The command buffer to record the upload into.</param>
    /// <exception cref="InvalidOperationException">The array has already been uploaded.</exception>
    public void Upload(CommandBuffer commands)
    {
        if (_uploaded)
            throw new InvalidOperationException("The texture array has already been uploaded.");

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
            _staging.Handle,
            _image,
            VkImageLayout.TransferDstOptimal,
            [
                new VkBufferImageCopy
                {
                    ImageSubresource = new VkImageSubresourceLayers
                    {
                        AspectMask = VkImageAspectFlags.ColorBit,
                        LayerCount = Layers,
                    },
                    ImageExtent = new VkExtent3D { Width = Width, Height = Height, Depth = 1 },
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

        _uploaded = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _staging.Dispose();
        _context.Device.DestroySampler(Sampler, null);
        _context.Device.DestroyImageView(View, null);
        _context.Device.DestroyImage(_image, null);
        _context.Device.FreeMemory(_memory, null);
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
                        LayerCount = Layers,
                    },
                },
            ]
        );
}
