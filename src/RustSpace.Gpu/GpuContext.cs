namespace RustSpace.Gpu;

/// <summary>
/// The Vulkan objects that live as long as the window does: the instance, the surface, the device
/// and its queue.
/// </summary>
/// <remarks>
/// <para>
/// One queue, used for everything. A separate transfer queue lets uploads overlap drawing on
/// hardware that has one, and is the right thing to add when chunk uploads are measured to be in
/// the way; adding it before then means writing ownership transfers and a second set of
/// synchronisation for a problem nobody has.
/// </para>
/// <para>
/// The depth format is chosen here rather than where it is used, because every render pass,
/// pipeline and attachment has to agree on it and there is exactly one right answer per device.
/// </para>
/// </remarks>
public sealed unsafe class GpuContext : IDisposable
{
    /// <summary>Creates the instance, surface and device for a window.</summary>
    /// <param name="window">The window to present to.</param>
    /// <param name="applicationName">What to tell the driver this application is called.</param>
    /// <param name="enableValidation">
    /// Whether to turn on the Khronos validation layer, which is off unless it is installed.
    /// </param>
    public GpuContext(GameWindow window, string applicationName, bool enableValidation)
    {
        Instance = CreateInstance(applicationName, enableValidation);
        Surface = CreateSurface(window);
        (PhysicalDevice, GraphicsQueueFamily) = PickPhysicalDevice();
        Device = CreateDevice();
        GraphicsQueue = Device.GetQueue(GraphicsQueueFamily, 0);
        DepthFormat = PickDepthFormat();
        DeviceName = PhysicalDevice.GetDeviceName();
    }

    /// <summary>The Vulkan instance.</summary>
    public Instance Instance { get; }

    /// <summary>The window surface this context presents to.</summary>
    public VkSurfaceKHR Surface { get; }

    /// <summary>The chosen GPU.</summary>
    public PhysicalDevice PhysicalDevice { get; }

    /// <summary>The logical device.</summary>
    public DeviceEx Device { get; }

    /// <summary>The queue every command buffer is submitted to.</summary>
    public Queue GraphicsQueue { get; }

    /// <summary>The queue family that queue belongs to.</summary>
    public int GraphicsQueueFamily { get; }

    /// <summary>The depth format every attachment and pipeline in this context uses.</summary>
    public VkFormat DepthFormat { get; }

    /// <summary>The GPU's name, for the debug overlay.</summary>
    public string DeviceName { get; }

    /// <summary>Blocks until the device has finished everything, before anything is destroyed.</summary>
    public void WaitIdle() => Device.WaitIdle();

    /// <inheritdoc />
    public void Dispose()
    {
        Device.WaitIdle();
        Device.Dispose();
        Instance.DestroySurfaceKHR(Surface, null);
        Instance.Dispose();
    }

    private static Instance CreateInstance(string applicationName, bool enableValidation)
    {
        var extensions = new HashSet<string>();

        var required = Glfw.PInvoke.GetRequiredInstanceExtensions(out var count);
        if (required is null)
            throw new PlatformNotSupportedException("GLFW cannot create a Vulkan surface on this platform");

        for (var index = 0u; index < count; index++)
            extensions.Add(Utf8.GetString(Utf8.GetNullTerminatedSpan(required[index])));

        // On macOS this adds the portability enumeration and properties2 extensions MoltenVK needs.
        VulkanHelpers.AddPlatformRequiredExtensions(extensions);

        var layers = new HashSet<string>();
        if (enableValidation && VulkanHelpers.Validation.IsSupported)
        {
            VulkanHelpers.AddValidationLayers(layers);
            VulkanHelpers.AddDebugExtensions(extensions);
        }

        var name = Utf8.GetBytes(applicationName + '\0');
        fixed (byte* namePointer = name)
        {
            return Vulkan.CreateInstance(
                new Instance.CreateInfo
                {
                    AppInfo = new VkApplicationInfo
                    {
                        ApplicationName = namePointer,
                        EngineName = "RustSpace"u8.AsPointer(),
                        ApiVersion = VK.API_VERSION_1_3,
                    },
                    Flags = VulkanHelpers.GetPlatformRequiredInstanceCreateFlags(),
                    EnabledExtensions = extensions,
                    EnabledLayers = layers,
                }
            );
        }
    }

    private VkSurfaceKHR CreateSurface(GameWindow window)
    {
        var result = (VkResult)Glfw.CreateWindowSurface(Instance.VkHandle, window.Handle, out var handle);
        if (result != VkResult.Success)
            throw new InvalidOperationException($"could not create a Vulkan surface for the window: {result}");

        return new VkSurfaceKHR((nint)handle);
    }

    private (PhysicalDevice Device, int QueueFamily) PickPhysicalDevice()
    {
        // Prefer a discrete GPU, then anything that can draw and present. On a laptop with both,
        // the integrated one is listed first as often as not.
        PhysicalDevice? fallback = null;
        var fallbackFamily = -1;

        foreach (var candidate in Instance.PhysicalDevices)
        {
            var family = FindGraphicsAndPresentFamily(candidate);
            if (family < 0)
                continue;

            if (candidate.GetProperties().DeviceType == VkPhysicalDeviceType.DiscreteGpu)
                return (candidate, family);

            fallback ??= candidate;
            if (fallbackFamily < 0)
                fallbackFamily = family;
        }

        if (fallback is null)
            throw new PlatformNotSupportedException("no Vulkan device can both draw and present to this window");

        return (fallback, fallbackFamily);
    }

    private int FindGraphicsAndPresentFamily(PhysicalDevice candidate)
    {
        // One family that can do both, rather than two families and the ownership transfers that
        // come with them. Every device in practice has one.
        var familyCount = candidate.GetQueueFamilyProperties_Count();
        Span<VkQueueFamilyProperties> families = stackalloc VkQueueFamilyProperties[familyCount];
        candidate.GetQueueFamilyProperties_Write(families);

        for (var family = 0; family < families.Length; family++)
        {
            if (!families[family].QueueFlags.HasFlag(VkQueueFlags.GraphicsBit))
                continue;

            if (candidate.GetSurfaceSupportKHR(family, Surface))
                return family;
        }

        return -1;
    }

    private DeviceEx CreateDevice()
    {
        var priority = 1.0f;
        var queueInfo = new VkDeviceQueueCreateInfo
        {
            QueueFamilyIndex = GraphicsQueueFamily,
            QueueCount = 1,
            QueuePriorities = &priority,
        };

        var extensions = new HashSet<string>();
        VulkanHelpers.AddSwapchainExtensions(extensions);

        // On MoltenVK this adds VK_KHR_portability_subset, which the specification requires a
        // device to enable when it reports it.
        VulkanHelpers.AddMandatoryIfSupportedDeviceExtensions(extensions, PhysicalDevice);

        return PhysicalDevice.CreateDevice(
            new DeviceEx.CreateInfo { Extensions = extensions, QueueCreateInfos = [queueInfo] }
        );
    }

    private VkFormat PickDepthFormat()
    {
        // 32-bit float first. A voxel world is drawn from a floating origin with a near plane close
        // in and a far plane kilometres out, and 16 bits of depth across that range puts z-fighting
        // on every distant surface.
        var format = PhysicalDevice.FindSupportedFormat(
            [VkFormat.D32SFloat, VkFormat.D24UNormS8UInt, VkFormat.D16UNorm],
            VkImageTiling.Optimal,
            VkFormatFeatureFlags.DepthStencilAttachmentBit
        );

        if (format == VkFormat.Undefined)
            throw new PlatformNotSupportedException("the GPU supports no usable depth format");

        return format;
    }
}
