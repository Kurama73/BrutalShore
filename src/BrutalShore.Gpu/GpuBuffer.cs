namespace BrutalShore.Gpu;

/// <summary>A block of GPU memory holding one array of vertices or indices.</summary>
/// <remarks>
/// <para>
/// The memory is host-visible and coherent, so uploading is a single memory copy with no staging
/// buffer, no transfer command and no queue submission. On Apple Silicon that is also the fastest
/// option available, because the GPU reads the same physical memory the CPU wrote; on a discrete
/// GPU it costs bandwidth on every draw, and this is the one place to revisit — a staging copy into
/// device-local memory — if a discrete card ever shows up in a profile.
/// </para>
/// <para>
/// Geometry is treated as immutable once created: a chunk whose mesh changed gets a new buffer
/// rather than a rewrite of this one, because the GPU may still be reading the old contents for a
/// frame that has not finished. <see cref="GpuRenderer.Retire" /> is what makes that safe.
/// </para>
/// <para>
/// <see cref="Rewrite{T}" /> is the deliberate exception, and it is only sound for a buffer kept
/// one per frame in flight: the fence waited on at the top of a frame proves the GPU has finished
/// reading that frame's copy, so overwriting it cannot race. Rewriting a buffer any draw call
/// still in flight might read is a use-after-free with no managed signal.
/// </para>
/// </remarks>
public sealed class GpuBuffer : IDisposable
{
    private readonly GpuContext _context;

    private GpuBuffer(GpuContext context, VkBuffer handle, VkDeviceMemory memory, ByteSize size, int count)
    {
        _context = context;
        Handle = handle;
        Memory = memory;
        Size = size;
        Count = count;
    }

    /// <summary>The buffer to bind.</summary>
    public VkBuffer Handle { get; }

    /// <summary>The allocation backing the buffer.</summary>
    public VkDeviceMemory Memory { get; }

    /// <summary>How many bytes the buffer holds.</summary>
    public ByteSize Size { get; }

    /// <summary>How many elements were uploaded, which is the draw count for an index buffer.</summary>
    public int Count { get; }

    /// <summary>Allocates a buffer and copies <paramref name="data" /> into it.</summary>
    /// <exception cref="ArgumentException">If <paramref name="data" /> is empty.</exception>
    public static GpuBuffer Create<T>(GpuContext context, ReadOnlySpan<T> data, VkBufferUsageFlags usage)
        where T : unmanaged
    {
        // Vulkan rejects a zero-sized buffer, and a caller asking for one has a bug upstream: an
        // empty mesh should not reach the GPU at all.
        if (data.IsEmpty)
            throw new ArgumentException("A GPU buffer cannot be empty.", nameof(data));

        var size = ByteSize.Of(data);
        var handle = context.Device.CreateBuffer(
            new VkBufferCreateInfo
            {
                Size = size,
                Usage = usage,
                SharingMode = VkSharingMode.Exclusive,
            },
            null
        );

        var requirements = context.Device.GetBufferMemoryRequirements(handle);
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

        context.Device.BindBufferMemory(handle, memory, ByteSize.Zero);

        // Coherent memory needs no flush: the write is visible to the device as soon as it lands.
        context.Device.WriteMemory(data, memory);

        return new GpuBuffer(context, handle, memory, size, data.Length);
    }

    /// <summary>Replaces the whole contents of the buffer.</summary>
    /// <typeparam name="T">The element type, which must match the one the buffer was created with.</typeparam>
    /// <param name="data">The new contents; must be no larger than the buffer.</param>
    /// <exception cref="ArgumentException">The data does not fit.</exception>
    /// <remarks>
    /// Only safe on a buffer the GPU is provably not reading; see the remarks on the type for what
    /// makes that provable.
    /// </remarks>
    public void Rewrite<T>(ReadOnlySpan<T> data)
        where T : unmanaged
    {
        var size = ByteSize.Of(data);
        if (size > Size)
            throw new ArgumentException($"{size} does not fit in a buffer of {Size}.", nameof(data));

        // Coherent memory needs no flush: the write is visible to the device as soon as it lands.
        _context.Device.WriteMemory(data, Memory);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Device.DestroyBuffer(Handle, null);
        _context.Device.FreeMemory(Memory, null);
    }
}
