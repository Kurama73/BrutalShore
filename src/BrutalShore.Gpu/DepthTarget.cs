namespace BrutalShore.Gpu;

/// <summary>A square depth image with its own render pass, to be sampled later in the frame.</summary>
/// <remarks>
/// <para>
/// Its one use today is the sun's shadow map, but nothing here knows that: it is a depth buffer
/// something is rendered into and then read from, and a voxel never comes into it. What is drawn,
/// and from which light, is a decision one layer up.
/// </para>
/// <para>
/// The render pass leaves the image in <c>ShaderReadOnlyOptimal</c> and declares the dependencies
/// both ways, so the caller records a pass and then samples the result with no barrier of its
/// own. That is the whole reason the render pass lives here rather than beside the pipeline that
/// draws into it.
/// </para>
/// <para>
/// The sampler compares rather than returns: a <c>sampler2DShadow</c> fetch asks "is this
/// fragment nearer than what was stored", and with linear filtering the hardware averages four
/// of those comparisons for free. That free filtering is most of why shadow edges are not stairs.
/// </para>
/// </remarks>
public sealed unsafe class DepthTarget : IDisposable
{
    private readonly GpuContext _context;
    private readonly VkImage _image;
    private readonly VkDeviceMemory _memory;
    private readonly VkFramebuffer _framebuffer;

    /// <summary>Allocates the image, the view, the sampler, the render pass and the framebuffer.</summary>
    /// <param name="context">The device everything is allocated from.</param>
    /// <param name="resolution">The width and height of the square image, in texels.</param>
    /// <exception cref="ArgumentOutOfRangeException">The resolution is not positive.</exception>
    public DepthTarget(GpuContext context, int resolution)
    {
        if (resolution <= 0)
            throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "A depth target needs a positive size.");

        _context = context;
        Resolution = resolution;

        RenderPass = CreateRenderPass();

        _image = context.Device.CreateImage(
            new VkImageCreateInfo
            {
                ImageType = VkImageType._2D,
                Format = context.DepthFormat,
                Extent = new VkExtent3D { Width = resolution, Height = resolution, Depth = 1 },
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = VkSampleCountFlags._1Bit,
                Tiling = VkImageTiling.Optimal,
                Usage = VkImageUsageFlags.DepthStencilAttachmentBit | VkImageUsageFlags.SampledBit,
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
                ViewType = VkImageViewType._2D,
                Format = context.DepthFormat,
                SubresourceRange = new VkImageSubresourceRange
                {
                    AspectMask = VkImageAspectFlags.DepthBit,
                    LevelCount = 1,
                    LayerCount = 1,
                },
            },
            null
        );

        Sampler = context.Device.CreateSampler(
            new VkSamplerCreateInfo
            {
                MagFilter = VkFilter.Linear,
                MinFilter = VkFilter.Linear,
                MipmapMode = VkSamplerMipmapMode.Nearest,

                // Clamping to a white border means every sample taken outside the map reads as
                // "nothing was in the way", so the world beyond the map's reach is simply lit.
                AddressModeU = VkSamplerAddressMode.ClampToBorder,
                AddressModeV = VkSamplerAddressMode.ClampToBorder,
                AddressModeW = VkSamplerAddressMode.ClampToBorder,
                BorderColor = VkBorderColor.FloatOpaqueWhite,

                CompareEnable = true,
                CompareOp = VkCompareOp.LessOrEqual,
                MaxLod = 1f,
            },
            null
        );

        var view = View;
        _framebuffer = context.Device.CreateFramebuffer(
            new VkFramebufferCreateInfo
            {
                RenderPass = RenderPass,
                AttachmentCount = 1,
                Attachments = &view,
                Width = resolution,
                Height = resolution,
                Layers = 1,
            },
            null
        );
    }

    /// <summary>The width and height of the image, in texels.</summary>
    public int Resolution { get; }

    /// <summary>The render pass to record the depth-only draw calls into.</summary>
    public VkRenderPass RenderPass { get; }

    /// <summary>The view to bind in a descriptor set once the pass has ended.</summary>
    public VkImageView View { get; }

    /// <summary>The comparing sampler to bind beside <see cref="View" />.</summary>
    public VkSampler Sampler { get; }

    /// <summary>Opens the pass, clears the depth and sets a viewport over the whole image.</summary>
    /// <param name="commands">The command buffer being recorded.</param>
    public void Begin(CommandBuffer commands)
    {
        Span<VkClearValue> clearValues =
        [
            new VkClearValue { DepthStencil = new VkClearDepthStencilValue { Depth = 1f, Stencil = 0 } },
        ];

        commands.BeginRenderPass(
            new VkRenderPassBeginInfo
            {
                RenderPass = RenderPass,
                Framebuffer = _framebuffer,
                RenderArea = new VkRect2D { Extent = new VkExtent2D { Width = Resolution, Height = Resolution } },
                ClearValueCount = clearValues.Length,
                ClearValues = clearValues.AsPointer(),
            },
            VkSubpassContents.Inline
        );

        commands.SetViewport(0, [new VkViewport { Width = Resolution, Height = Resolution, MaxDepth = 1f }]);
        commands.SetScissor(
            0,
            [new VkRect2D { Extent = new VkExtent2D { Width = Resolution, Height = Resolution } }]
        );
    }

    /// <summary>Closes the pass, after which the image can be sampled.</summary>
    /// <param name="commands">The command buffer being recorded.</param>
    public void End(CommandBuffer commands) => commands.EndRenderPass();

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Device.DestroyFramebuffer(_framebuffer, null);
        _context.Device.DestroySampler(Sampler, null);
        _context.Device.DestroyImageView(View, null);
        _context.Device.DestroyImage(_image, null);
        _context.Device.FreeMemory(_memory, null);
        _context.Device.DestroyRenderPass(RenderPass, null);
    }

    private VkRenderPass CreateRenderPass()
    {
        var depth = new VkAttachmentDescription
        {
            Format = _context.DepthFormat,
            Samples = VkSampleCountFlags._1Bit,
            LoadOp = VkAttachmentLoadOp.Clear,

            // Stored, unlike the swapchain's depth buffer: the whole point is to read it back.
            StoreOp = VkAttachmentStoreOp.Store,
            StencilLoadOp = VkAttachmentLoadOp.DontCare,
            StencilStoreOp = VkAttachmentStoreOp.DontCare,
            InitialLayout = VkImageLayout.Undefined,
            FinalLayout = VkImageLayout.ShaderReadOnlyOptimal,
        };

        var depthReference = new VkAttachmentReference
        {
            Attachment = 0,
            Layout = VkImageLayout.DepthStencilAttachmentOptimal,
        };

        var subpass = new VkSubpassDescription
        {
            PipelineBindPoint = VkPipelineBindPoint.Graphics,
            ColorAttachmentCount = 0,
            DepthStencilAttachment = &depthReference,
        };

        // Two dependencies, one each way: the pass must wait for last frame's readers to finish
        // with the image, and this frame's readers must wait for the pass to have written it.
        Span<VkSubpassDependency> dependencies =
        [
            new()
            {
                SrcSubpass = VK.SUBPASS_EXTERNAL,
                DstSubpass = 0,
                SrcStageMask = VkPipelineStageFlags.FragmentShaderBit,
                DstStageMask = VkPipelineStageFlags.EarlyFragmentTestsBit,
                SrcAccessMask = VkAccessFlags.ShaderReadBit,
                DstAccessMask = VkAccessFlags.DepthStencilAttachmentWriteBit,
            },
            new()
            {
                SrcSubpass = 0,
                DstSubpass = VK.SUBPASS_EXTERNAL,
                SrcStageMask = VkPipelineStageFlags.LateFragmentTestsBit,
                DstStageMask = VkPipelineStageFlags.FragmentShaderBit,
                SrcAccessMask = VkAccessFlags.DepthStencilAttachmentWriteBit,
                DstAccessMask = VkAccessFlags.ShaderReadBit,
            },
        ];

        return _context.Device.CreateRenderPass(
            new VkRenderPassCreateInfo
            {
                AttachmentCount = 1,
                Attachments = &depth,
                SubpassCount = 1,
                Subpasses = &subpass,
                DependencyCount = dependencies.Length,
                Dependencies = dependencies.AsPointer(),
            },
            null
        );
    }
}
