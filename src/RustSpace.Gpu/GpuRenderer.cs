namespace RustSpace.Gpu;

/// <summary>
/// The frame loop: waits for a frame slot, acquires a swapchain image, opens the render pass, and
/// on <see cref="EndFrame" /> submits and presents.
/// </summary>
/// <remarks>
/// <para>
/// Two frames in flight. One would idle the CPU on every fence wait; three adds a frame of input
/// latency to buy throughput this renderer does not need. Each slot owns its own command pool, so
/// recording a frame never touches memory another in-flight frame is reading — which is also why
/// the pool is reset rather than the individual command buffer.
/// </para>
/// <para>
/// Resizing is handled by observing it rather than by listening for it: the swapchain reports
/// <c>ErrorOutOfDateKHR</c>, and that is the authoritative signal. A window-resize callback would
/// race with the acquire, and on some platforms fires before the surface has actually changed size.
/// </para>
/// </remarks>
public sealed unsafe class GpuRenderer : IDisposable
{
    /// <summary>How many frames the CPU may run ahead of the GPU.</summary>
    public const int MaxFramesInFlight = 2;

    private readonly GpuContext _context;
    private readonly SwapchainTarget _target;
    private readonly Frame[] _frames = new Frame[MaxFramesInFlight];

    private int _frameIndex;
    private int _imageIndex;
    private bool _recording;

    /// <summary>Creates the per-frame resources for a swapchain.</summary>
    public GpuRenderer(GpuContext context, SwapchainTarget target)
    {
        _context = context;
        _target = target;

        for (var index = 0; index < MaxFramesInFlight; index++)
            _frames[index] = CreateFrame();
    }

    /// <summary>What the colour attachment is cleared to, in linear light.</summary>
    public float4 ClearColor { get; set; } = new(0.42f, 0.62f, 0.86f, 1f);

    /// <summary>
    /// Which swapchain image the most recent frame was drawn into, which is the image still on
    /// screen once that frame has been presented.
    /// </summary>
    public int LastImageIndex => _imageIndex;

    /// <summary>The command buffer of the frame being recorded.</summary>
    /// <exception cref="InvalidOperationException">Outside a frame.</exception>
    public CommandBuffer Commands =>
        _recording
            ? _frames[_frameIndex].CommandBuffer
            : throw new InvalidOperationException("No frame is being recorded.");

    /// <summary>
    /// Waits for a frame slot, acquires an image and starts the render pass.
    /// </summary>
    /// <returns>
    /// <see langword="false" /> when the swapchain had to be rebuilt and this frame must be skipped;
    /// the caller should simply try again next tick rather than draw into a stale target.
    /// </returns>
    public bool BeginFrame()
    {
        var frame = _frames[_frameIndex];

        // Waiting here, rather than after submitting, is what bounds the queue depth: the CPU can
        // only be MaxFramesInFlight frames ahead before this blocks.
        _context.Device.WaitForFences([frame.Fence], true, -1);

        // The fence is signalled, so everything this slot submitted last time has completed and
        // whatever it was reading can now be destroyed.
        frame.ReleaseRetired();

        var imageAvailable = frame.ImageAvailable;
        var acquire = _context.Device.AcquireNextImageKHR(_target.Handle, -1, imageAvailable, default, out var imageIndex);
        if (acquire == VkResult.ErrorOutOfDateKHR)
        {
            _target.Recreate();
            return false;
        }

        // Suboptimal still presents correctly, so the frame is drawn and the swapchain is rebuilt
        // on present instead; throwing away a frame that would have displayed costs more.
        if (acquire is not VkResult.Success and not VkResult.SuboptimalKHR)
            throw new InvalidOperationException($"Could not acquire a swapchain image: {acquire}.");

        _imageIndex = imageIndex;
        _context.Device.ResetFences([frame.Fence]);
        _context.Device.ResetCommandPool(frame.Pool, VkCommandPoolResetFlags.None);

        var commands = frame.CommandBuffer;
        commands.Begin(new VkCommandBufferBeginInfo { Flags = VkCommandBufferUsageFlags.OneTimeSubmitBit });

        Span<VkClearValue> clearValues =
        [
            new VkClearValue { Color = new VkClearColorValue { Float32 = ClearColor } },
            new VkClearValue { DepthStencil = new VkClearDepthStencilValue { Depth = 1f, Stencil = 0 } },
        ];

        commands.BeginRenderPass(
            new VkRenderPassBeginInfo
            {
                RenderPass = _target.RenderPass,
                Framebuffer = _target.FramebufferAt(imageIndex),
                RenderArea = new VkRect2D { Extent = _target.Extent },
                ClearValueCount = clearValues.Length,
                ClearValues = clearValues.AsPointer(),
            },
            VkSubpassContents.Inline
        );

        // Viewport and scissor are dynamic state, so a pipeline survives a resize; setting them
        // once per frame here means no draw call has to remember to.
        commands.SetViewport(
            0,
            [new VkViewport { Width = _target.Extent.Width, Height = _target.Extent.Height, MaxDepth = 1f }]
        );
        commands.SetScissor(0, [new VkRect2D { Extent = _target.Extent }]);

        _recording = true;
        return true;
    }

    /// <summary>Closes the render pass, submits the frame and presents it.</summary>
    public void EndFrame()
    {
        if (!_recording)
            throw new InvalidOperationException("No frame is being recorded.");

        _recording = false;

        var frame = _frames[_frameIndex];
        var commands = frame.CommandBuffer;
        commands.EndRenderPass();
        commands.End();

        var imageAvailable = frame.ImageAvailable;
        var renderFinished = frame.RenderFinished;
        var handle = commands.Handle;

        // The colour attachment is the first thing that touches the acquired image, so that is the
        // stage the acquire semaphore has to hold back. Earlier stages are free to run meanwhile.
        var waitStage = VkPipelineStageFlags.ColorAttachmentOutputBit;

        _context.GraphicsQueue.Submit(
            [
                new VkSubmitInfo
                {
                    WaitSemaphoreCount = 1,
                    WaitSemaphores = &imageAvailable,
                    WaitDstStageMask = &waitStage,
                    CommandBufferCount = 1,
                    CommandBuffers = &handle,
                    SignalSemaphoreCount = 1,
                    SignalSemaphores = &renderFinished,
                },
            ],
            frame.Fence
        );

        var swapchain = _target.Handle;
        var imageIndex = _imageIndex;
        var present = _context.GraphicsQueue.PresentKHR(
            new VkPresentInfoKHR
            {
                WaitSemaphoreCount = 1,
                WaitSemaphores = &renderFinished,
                SwapchainCount = 1,
                Swapchains = &swapchain,
                ImageIndices = &imageIndex,
            }
        );

        if (present is VkResult.ErrorOutOfDateKHR or VkResult.SuboptimalKHR)
            _target.Recreate();
        else if (present != VkResult.Success)
            throw new InvalidOperationException($"Could not present a frame: {present}.");

        _frameIndex = (_frameIndex + 1) % MaxFramesInFlight;
    }

    /// <summary>
    /// Hands ownership of a resource the current frame may still be reading to the renderer, which
    /// disposes it once that frame has finished on the GPU.
    /// </summary>
    /// <remarks>
    /// This is the alternative to a device-wide idle wait every time a chunk is remeshed. Attaching
    /// the resource to the frame slot works because the slot's fence is the exact signal that no
    /// command referring to it is still queued.
    /// </remarks>
    public void Retire(IDisposable resource) => _frames[_frameIndex].Retire(resource);

    /// <inheritdoc />
    public void Dispose()
    {
        _context.WaitIdle();

        foreach (var frame in _frames)
        {
            frame.ReleaseRetired();
            _context.Device.DestroySemaphore(frame.RenderFinished, null);
            _context.Device.DestroySemaphore(frame.ImageAvailable, null);
            _context.Device.DestroyFence(frame.Fence, null);
            _context.Device.DestroyCommandPool(frame.Pool, null);
        }
    }

    private Frame CreateFrame()
    {
        var pool = _context.Device.CreateCommandPool(
            new VkCommandPoolCreateInfo { QueueFamilyIndex = _context.GraphicsQueueFamily },
            null
        );

        return new Frame
        {
            Pool = pool,
            CommandBuffer = _context.Device.AllocateCommandBuffer(
                new VkCommandBufferAllocateInfo { CommandPool = pool, Level = VkCommandBufferLevel.Primary }
            ),

            // Created signalled, so the first BeginFrame does not wait for work that never happened.
            Fence = _context.Device.CreateFence(new VkFenceCreateInfo { Flags = VkFenceCreateFlags.SignaledBit }, null),
            ImageAvailable = _context.Device.CreateSemaphore(new VkSemaphoreCreateInfo(), null),
            RenderFinished = _context.Device.CreateSemaphore(new VkSemaphoreCreateInfo(), null),
        };
    }

    /// <summary>Everything one in-flight frame owns.</summary>
    private sealed class Frame
    {
        private readonly List<IDisposable> _retired = [];

        public required VkCommandPool Pool { get; init; }

        public required CommandBuffer CommandBuffer { get; init; }

        public required VkFence Fence { get; init; }

        public required VkSemaphore ImageAvailable { get; init; }

        public required VkSemaphore RenderFinished { get; init; }

        public void Retire(IDisposable resource) => _retired.Add(resource);

        public void ReleaseRetired()
        {
            foreach (var resource in _retired)
                resource.Dispose();

            _retired.Clear();
        }
    }
}
