using Brutal.StbApi;

namespace BrutalShore.Gpu;

/// <summary>Reads a presented frame back off the GPU and writes it to a PNG file.</summary>
/// <remarks>
/// <para>
/// This exists to make "does it look right" a question that can be answered without a person at
/// the keyboard: a build can run a fixed number of frames, save the last one and have the image
/// compared or looked at later. It is not a fast path and is not meant to be used every frame.
/// </para>
/// <para>
/// The copy goes image-to-buffer rather than image-to-image. A linear image would need its row
/// pitch queried and honoured, while a buffer's rows are exactly as wide as the image, so the
/// mapped bytes can go straight to the encoder.
/// </para>
/// </remarks>
public static unsafe class FrameCapture
{
    /// <summary>Saves the contents of one swapchain image.</summary>
    /// <param name="context">The device that owns the swapchain.</param>
    /// <param name="target">The swapchain to read from.</param>
    /// <param name="imageIndex">Which image; normally <see cref="GpuRenderer.LastImageIndex" />.</param>
    /// <param name="path">Where to write the PNG.</param>
    /// <remarks>
    /// The caller is responsible for the frame having finished: the image is read as it stands,
    /// and reading it while it is still being drawn into would capture a half-finished frame.
    /// </remarks>
    /// <exception cref="InvalidOperationException">If the file could not be written.</exception>
    public static void Save(GpuContext context, SwapchainTarget target, int imageIndex, string path)
    {
        var width = target.Extent.Width;
        var height = target.Extent.Height;
        var pixelCount = width * height;

        var buffer = context.Device.CreateBuffer(
            new VkBufferCreateInfo
            {
                Size = ByteSize.Of(pixelCount * 4),
                Usage = VkBufferUsageFlags.TransferDstBit,
                SharingMode = VkSharingMode.Exclusive,
            },
            null
        );

        var requirements = context.Device.GetBufferMemoryRequirements(buffer);
        var memory = context.Device.AllocateMemory(
            new VkMemoryAllocateInfo
            {
                AllocationSize = requirements.Size,
                MemoryTypeIndex = context.PhysicalDevice.FindMemoryType(
                    (int)requirements.MemoryTypeBits,
                    VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit
                ),
            },
            null
        );

        context.Device.BindBufferMemory(buffer, memory, ByteSize.Zero);

        try
        {
            CopyImageToBuffer(context, target.ImageAt(imageIndex), buffer, width, height);
            WritePng(context, memory, path, width, height, pixelCount, target.ColorFormat);
        }
        finally
        {
            context.Device.DestroyBuffer(buffer, null);
            context.Device.FreeMemory(memory, null);
        }
    }

    private static void CopyImageToBuffer(GpuContext context, VkImage image, VkBuffer buffer, int width, int height)
    {
        var pool = context.Device.CreateCommandPool(
            new VkCommandPoolCreateInfo { QueueFamilyIndex = context.GraphicsQueueFamily },
            null
        );

        try
        {
            var commands = context.Device.AllocateCommandBuffer(
                new VkCommandBufferAllocateInfo { CommandPool = pool, Level = VkCommandBufferLevel.Primary }
            );

            commands.Begin(new VkCommandBufferBeginInfo { Flags = VkCommandBufferUsageFlags.OneTimeSubmitBit });

            // A presented image is in the present layout, which cannot be read by a transfer. It
            // has to go back to that layout afterwards, because the swapchain will present it
            // again and Vulkan only allows a present from an image that is already in it.
            Barrier(commands, image, VkImageLayout.PresentSrcKHR, VkImageLayout.TransferSrcOptimal);

            Span<VkBufferImageCopy> regions =
            [
                new()
                {
                    ImageSubresource = new VkImageSubresourceLayers
                    {
                        AspectMask = VkImageAspectFlags.ColorBit,
                        LayerCount = 1,
                    },
                    ImageExtent = new VkExtent3D { Width = width, Height = height, Depth = 1 },
                },
            ];

            commands.CopyImageToBuffer(image, VkImageLayout.TransferSrcOptimal, buffer, regions);
            Barrier(commands, image, VkImageLayout.TransferSrcOptimal, VkImageLayout.PresentSrcKHR);
            commands.End();

            var handle = commands.Handle;
            context.GraphicsQueue.Submit(
                [new VkSubmitInfo { CommandBufferCount = 1, CommandBuffers = &handle }],
                default
            );

            // No fence: this is a one-shot path where waiting for the whole device is simpler than
            // creating and destroying a fence to wait for one command buffer.
            context.WaitIdle();
        }
        finally
        {
            context.Device.DestroyCommandPool(pool, null);
        }
    }

    private static void Barrier(CommandBuffer commands, VkImage image, VkImageLayout from, VkImageLayout to)
    {
        Span<VkImageMemoryBarrier> barriers =
        [
            new()
            {
                OldLayout = from,
                NewLayout = to,
                SrcAccessMask = VkAccessFlags.MemoryWriteBit,
                DstAccessMask = VkAccessFlags.MemoryReadBit,
                Image = image,
                SubresourceRange = new VkImageSubresourceRange
                {
                    AspectMask = VkImageAspectFlags.ColorBit,
                    LevelCount = 1,
                    LayerCount = 1,
                },
            },
        ];

        commands.PipelineBarrier(
            VkPipelineStageFlags.AllCommandsBit,
            VkPipelineStageFlags.AllCommandsBit,
            VkDependencyFlags.None,
            [],
            [],
            barriers
        );
    }

    private static void WritePng(
        GpuContext context,
        VkDeviceMemory memory,
        string path,
        int width,
        int height,
        int pixelCount,
        VkFormat format
    )
    {
        var mapped = context.Device.MapMemory(memory);

        try
        {
            var source = mapped.AsSpan<byte>(pixelCount * 4);
            var pixels = new byte[pixelCount * 4];

            // Swapchains on this platform hand out BGRA, while every image format expects RGBA.
            var swap = format is VkFormat.B8G8R8A8SRGB or VkFormat.B8G8R8A8UNorm;
            for (var index = 0; index < pixelCount; index++)
            {
                var offset = index * 4;
                pixels[offset + 0] = source[offset + (swap ? 2 : 0)];
                pixels[offset + 1] = source[offset + 1];
                pixels[offset + 2] = source[offset + (swap ? 0 : 2)];

                // The alpha channel of a presented frame is whatever the compositor left there,
                // and a screenshot of a game is opaque by definition.
                pixels[offset + 3] = 255;
            }

            if (!Stb.WritePng(path, width, height, 4, pixels))
                throw new InvalidOperationException($"Could not write '{path}'.");
        }
        finally
        {
            context.Device.UnmapMemory(memory);
        }
    }
}
