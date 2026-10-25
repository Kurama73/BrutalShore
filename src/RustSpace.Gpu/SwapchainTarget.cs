namespace RustSpace.Gpu;

/// <summary>
/// What a frame is drawn into: the swapchain, a depth buffer per image, the render pass and the
/// framebuffers that tie them together.
/// </summary>
/// <remarks>
/// <para>
/// The render pass is created once and survives every resize, while everything else is destroyed
/// and rebuilt. That split is deliberate and load-bearing: a render pass depends only on the
/// attachment <em>formats</em>, which do not change when a window is resized, and other things hold
/// on to it — a graphics pipeline is compiled against one, and Dear ImGui's Vulkan backend is
/// constructed with one. Recreating it on resize would invalidate all of them.
/// </para>
/// <para>
/// A depth buffer per swapchain image rather than one shared. With frames in flight, two frames can
/// be mid-render at once, and a shared depth attachment would have them writing to the same memory.
/// </para>
/// </remarks>
public sealed unsafe class SwapchainTarget : IDisposable
{
    private readonly GpuContext _context;
    private readonly GameWindow _window;

    private VkSwapchainKHR _swapchain;
    private VkImage[] _images = [];
    private VkImageView[] _imageViews = [];
    private VkImage[] _depthImages = [];
    private VkDeviceMemory[] _depthMemories = [];
    private VkImageView[] _depthViews = [];
    private VkFramebuffer[] _framebuffers = [];

    /// <summary>Creates the swapchain and the render pass for a window.</summary>
    public SwapchainTarget(GpuContext context, GameWindow window)
    {
        _context = context;
        _window = window;

        var surfaceFormat = PickSurfaceFormat();
        ColorFormat = surfaceFormat.Format;
        ColorSpace = surfaceFormat.ColorSpace;
        PresentMode = PickPresentMode();
        RenderPass = CreateRenderPass();

        Create();
    }

    /// <summary>The render pass every pipeline drawing to this target is compiled against.</summary>
    public VkRenderPass RenderPass { get; }

    /// <summary>The swapchain's colour format.</summary>
    public VkFormat ColorFormat { get; }

    /// <summary>The swapchain's colour space.</summary>
    public VkColorSpaceKHR ColorSpace { get; }

    /// <summary>How frames are queued for display.</summary>
    public VkPresentModeKHR PresentMode { get; }

    /// <summary>The current size in pixels.</summary>
    public VkExtent2D Extent { get; private set; }

    /// <summary>How many images the swapchain has, which is how many frames can be in flight.</summary>
    public int ImageCount => _images.Length;

    /// <summary>The swapchain handle, for acquire and present.</summary>
    public VkSwapchainKHR Handle => _swapchain;

    /// <summary>The framebuffer for one swapchain image.</summary>
    public VkFramebuffer FramebufferAt(int imageIndex) => _framebuffers[imageIndex];

    /// <summary>The colour image behind one framebuffer, for reading a finished frame back.</summary>
    public VkImage ImageAt(int imageIndex) => _images[imageIndex];

    /// <summary>
    /// Destroys and rebuilds everything that depends on the window's size, keeping the render pass.
    /// </summary>
    public void Recreate()
    {
        _context.WaitIdle();
        DestroySizeDependentResources();
        Create();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.WaitIdle();
        DestroySizeDependentResources();
        _context.Device.DestroyRenderPass(RenderPass, null);
    }

    private void Create()
    {
        var capabilities = _context.PhysicalDevice.GetSurfaceCapabilitiesKHR(_context.Surface);

        // One more image than the driver's minimum, so the application always has one to draw into
        // while the compositor holds another. Capped, since a maximum of zero means "no limit".
        var imageCount = capabilities.MinImageCount + 1;
        if (capabilities.MaxImageCount != 0 && imageCount > capabilities.MaxImageCount)
            imageCount = capabilities.MaxImageCount;

        Extent = ResolveExtent(capabilities);

        _swapchain = _context.Device.CreateSwapchainKHR(
            new VkSwapchainCreateInfoKHR
            {
                Surface = _context.Surface,
                MinImageCount = imageCount,
                ImageFormat = ColorFormat,
                ImageColorSpace = ColorSpace,
                ImageExtent = Extent,
                ImageArrayLayers = 1,
                ImageUsage = VkImageUsageFlags.ColorAttachmentBit,
                ImageSharingMode = VkSharingMode.Exclusive,
                PreTransform = capabilities.SupportedTransforms.HasFlag(VkSurfaceTransformFlagsKHR.IdentityBitKHR)
                    ? VkSurfaceTransformFlagsKHR.IdentityBitKHR
                    : capabilities.CurrentTransform,
                CompositeAlpha = VkCompositeAlphaFlagsKHR.OpaqueBitKHR,
                PresentMode = PresentMode,
                Clipped = true,
            },
            null
        );

        var actualCount = _context.Device.GetSwapchainImagesKHR_Count(_swapchain);
        _images = new VkImage[actualCount];
        _context.Device.GetSwapchainImagesKHR_Write(_swapchain, _images);

        _imageViews = new VkImageView[actualCount];
        _depthImages = new VkImage[actualCount];
        _depthMemories = new VkDeviceMemory[actualCount];
        _depthViews = new VkImageView[actualCount];
        _framebuffers = new VkFramebuffer[actualCount];

        for (var index = 0; index < actualCount; index++)
        {
            _imageViews[index] = CreateView(_images[index], ColorFormat, VkImageAspectFlags.ColorBit);
            CreateDepthAttachment(index);

            Span<VkImageView> attachments = [_imageViews[index], _depthViews[index]];
            _framebuffers[index] = _context.Device.CreateFramebuffer(
                new VkFramebufferCreateInfo
                {
                    RenderPass = RenderPass,
                    AttachmentCount = attachments.Length,
                    Attachments = attachments.AsPointer(),
                    Width = Extent.Width,
                    Height = Extent.Height,
                    Layers = 1,
                },
                null
            );
        }
    }

    /// <summary>
    /// The size to create the swapchain at.
    /// </summary>
    /// <remarks>
    /// A current extent of -1 in both axes is how the surface says "you choose", which is what a
    /// tiling window manager or a high-DPI compositor reports. The framebuffer size is the answer,
    /// clamped to what the surface will accept.
    /// </remarks>
    private VkExtent2D ResolveExtent(in VkSurfaceCapabilitiesKHR capabilities)
    {
        if (capabilities.CurrentExtent.Width != -1)
            return capabilities.CurrentExtent;

        var size = _window.FramebufferSize;
        return new VkExtent2D
        {
            Width = Math.Clamp(size.X, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
            Height = Math.Clamp(size.Y, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height),
        };
    }

    private VkSurfaceFormatKHR PickSurfaceFormat()
    {
        var count = _context.PhysicalDevice.GetSurfaceFormatsKHR_Count(_context.Surface);
        Span<VkSurfaceFormatKHR> formats = stackalloc VkSurfaceFormatKHR[count];
        _context.PhysicalDevice.GetSurfaceFormatsKHR_Write(_context.Surface, formats);

        // An sRGB surface, so the hardware does the encoding on write and the shader can work in
        // linear light. Doing it by hand in the shader instead gets the blending wrong, because
        // blending would then happen in the encoded space.
        foreach (var format in formats)
        {
            if (format.Format is VkFormat.B8G8R8A8SRGB or VkFormat.R8G8B8A8SRGB
                && format.ColorSpace == VkColorSpaceKHR.SRGBNonlinearKHR)
                return format;
        }

        return formats[0];
    }

    private VkPresentModeKHR PickPresentMode()
    {
        var count = _context.PhysicalDevice.GetSurfacePresentModesKHR_Count(_context.Surface);
        Span<VkPresentModeKHR> modes = stackalloc VkPresentModeKHR[count];
        _context.PhysicalDevice.GetSurfacePresentModesKHR_Write(_context.Surface, modes);

        // Mailbox when it is there: it presents the most recent frame without tearing and without
        // blocking the application on the display's refresh. FIFO is the only mode a Vulkan
        // implementation must support, so it is the fallback rather than a choice.
        foreach (var mode in modes)
            if (mode == VkPresentModeKHR.MailboxKHR)
                return mode;

        return VkPresentModeKHR.FifoKHR;
    }

    private VkRenderPass CreateRenderPass()
    {
        var color = new VkAttachmentDescription
        {
            Format = ColorFormat,
            Samples = VkSampleCountFlags._1Bit,
            LoadOp = VkAttachmentLoadOp.Clear,
            StoreOp = VkAttachmentStoreOp.Store,
            StencilLoadOp = VkAttachmentLoadOp.DontCare,
            StencilStoreOp = VkAttachmentStoreOp.DontCare,
            InitialLayout = VkImageLayout.Undefined,
            FinalLayout = VkImageLayout.PresentSrcKHR,
        };

        var depth = new VkAttachmentDescription
        {
            Format = _context.DepthFormat,
            Samples = VkSampleCountFlags._1Bit,
            LoadOp = VkAttachmentLoadOp.Clear,

            // Nothing reads the depth buffer after the frame, so there is no reason to write it
            // back out to memory.
            StoreOp = VkAttachmentStoreOp.DontCare,
            StencilLoadOp = VkAttachmentLoadOp.DontCare,
            StencilStoreOp = VkAttachmentStoreOp.DontCare,
            InitialLayout = VkImageLayout.Undefined,
            FinalLayout = VkImageLayout.DepthStencilAttachmentOptimal,
        };

        var colorReference = new VkAttachmentReference { Attachment = 0, Layout = VkImageLayout.ColorAttachmentOptimal };
        var depthReference = new VkAttachmentReference
        {
            Attachment = 1,
            Layout = VkImageLayout.DepthStencilAttachmentOptimal,
        };

        var subpass = new VkSubpassDescription
        {
            PipelineBindPoint = VkPipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            ColorAttachments = &colorReference,
            DepthStencilAttachment = &depthReference,
        };

        // The dependency the acquire semaphore does not cover. Waiting on the semaphore orders the
        // image's availability, but the attachment's layout transition and first write still need
        // to be ordered against anything the previous use of the image did.
        var dependency = new VkSubpassDependency
        {
            SrcSubpass = VK.SUBPASS_EXTERNAL,
            DstSubpass = 0,
            SrcStageMask = VkPipelineStageFlags.ColorAttachmentOutputBit | VkPipelineStageFlags.EarlyFragmentTestsBit,
            DstStageMask = VkPipelineStageFlags.ColorAttachmentOutputBit | VkPipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = VkAccessFlags.None,
            DstAccessMask = VkAccessFlags.ColorAttachmentWriteBit | VkAccessFlags.DepthStencilAttachmentWriteBit,
        };

        Span<VkAttachmentDescription> attachments = [color, depth];
        return _context.Device.CreateRenderPass(
            new VkRenderPassCreateInfo
            {
                AttachmentCount = attachments.Length,
                Attachments = attachments.AsPointer(),
                SubpassCount = 1,
                Subpasses = &subpass,
                DependencyCount = 1,
                Dependencies = &dependency,
            },
            null
        );
    }

    private VkImageView CreateView(VkImage image, VkFormat format, VkImageAspectFlags aspect) =>
        _context.Device.CreateImageView(
            new VkImageViewCreateInfo
            {
                Image = image,
                ViewType = VkImageViewType._2D,
                Format = format,
                Components = new VkComponentMapping
                {
                    R = VkComponentSwizzle.Identity,
                    G = VkComponentSwizzle.Identity,
                    B = VkComponentSwizzle.Identity,
                    A = VkComponentSwizzle.Identity,
                },
                SubresourceRange = new VkImageSubresourceRange
                {
                    AspectMask = aspect,
                    LevelCount = 1,
                    LayerCount = 1,
                },
            },
            null
        );

    private void CreateDepthAttachment(int index)
    {
        var image = _context.Device.CreateImage(
            new VkImageCreateInfo
            {
                ImageType = VkImageType._2D,
                Format = _context.DepthFormat,
                Extent = new VkExtent3D { Width = Extent.Width, Height = Extent.Height, Depth = 1 },
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = VkSampleCountFlags._1Bit,
                Tiling = VkImageTiling.Optimal,
                Usage = VkImageUsageFlags.DepthStencilAttachmentBit,
                SharingMode = VkSharingMode.Exclusive,
                InitialLayout = VkImageLayout.Undefined,
            },
            null
        );

        var requirements = _context.Device.GetImageMemoryRequirements(image);
        var memory = _context.Device.AllocateMemory(
            new VkMemoryAllocateInfo
            {
                AllocationSize = requirements.Size,
                MemoryTypeIndex = _context.PhysicalDevice.FindMemoryType(
                    (int)requirements.MemoryTypeBits,
                    VkMemoryPropertyFlags.DeviceLocalBit
                ),
            },
            null
        );

        _context.Device.BindImageMemory(image, memory, ByteSize.Zero);

        _depthImages[index] = image;
        _depthMemories[index] = memory;
        _depthViews[index] = CreateView(image, _context.DepthFormat, VkImageAspectFlags.DepthBit);
    }

    private void DestroySizeDependentResources()
    {
        var device = _context.Device;

        foreach (var framebuffer in _framebuffers)
            device.DestroyFramebuffer(framebuffer, null);

        foreach (var view in _depthViews)
            device.DestroyImageView(view, null);

        foreach (var image in _depthImages)
            device.DestroyImage(image, null);

        foreach (var memory in _depthMemories)
            device.FreeMemory(memory, null);

        foreach (var view in _imageViews)
            device.DestroyImageView(view, null);

        if (!_swapchain.IsNull())
            device.DestroySwapchainKHR(_swapchain, null);

        _framebuffers = [];
        _depthViews = [];
        _depthImages = [];
        _depthMemories = [];
        _imageViews = [];
        _images = [];
        _swapchain = default;
    }
}
