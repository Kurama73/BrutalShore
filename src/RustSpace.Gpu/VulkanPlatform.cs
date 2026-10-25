using System.Runtime.InteropServices;

namespace RustSpace.Gpu;

/// <summary>
/// Everything that has to happen before GLFW or Vulkan is touched, which on macOS is not nothing.
/// </summary>
/// <remarks>
/// <para>
/// BRUTAL supports Windows and Linux. It also, as it happens, ships osx-arm64 binaries for every
/// native library it wraps, handles the Metal portability extensions, and looks for a Homebrew
/// Vulkan loader — so macOS works, with two gaps this class fills.
/// </para>
/// <para>
/// The first gap is the driver manifest. The Vulkan loader finds drivers by reading JSON manifests
/// from a list of directories, and Homebrew installs MoltenVK's manifest under its own
/// <c>etc</c> rather than any of them. The loader reads <c>VK_DRIVER_FILES</c> at instance creation
/// to be told directly, so that is what is set here.
/// </para>
/// <para>
/// The second gap is that GLFW loads the loader itself, by leaf name, which only finds a library on
/// the dynamic linker's search path — and Homebrew's prefix is not on it. Rather than asking anyone
/// to export <c>DYLD_LIBRARY_PATH</c> before running the game, the loader is opened here by
/// absolute path and handed to GLFW through <c>glfwInitVulkanLoader</c>, which exists for exactly
/// this.
/// </para>
/// <para>
/// On Windows and Linux every one of these steps is skipped: the loader is on the search path and
/// the manifests are where the loader looks.
/// </para>
/// </remarks>
public static partial class VulkanPlatform
{
    /// <summary>
    /// Where Homebrew and MacPorts put MoltenVK's driver manifest, in the order they are tried.
    /// </summary>
    private static readonly string[] MacOsDriverManifests =
    [
        "/opt/homebrew/etc/vulkan/icd.d/MoltenVK_icd.json",
        "/opt/homebrew/share/vulkan/icd.d/MoltenVK_icd.json",
        "/usr/local/etc/vulkan/icd.d/MoltenVK_icd.json",
        "/usr/local/share/vulkan/icd.d/MoltenVK_icd.json",
        "/opt/local/share/vulkan/icd.d/MoltenVK_icd.json",
    ];

    /// <summary>Where a Vulkan loader built for macOS is normally installed.</summary>
    private static readonly string[] MacOsLoaders =
    [
        "/opt/homebrew/lib/libvulkan.1.dylib",
        "/usr/local/lib/libvulkan.1.dylib",
        "/opt/local/lib/libvulkan.1.dylib",
    ];

    /// <summary>What <see cref="Prepare"/> found, for the debug overlay and for error messages.</summary>
    public static string Report { get; private set; } = "not prepared";

    /// <summary>
    /// Prepares the process so that GLFW and Vulkan can both find a driver, then initialises GLFW.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown on macOS when no Vulkan loader or no driver manifest is installed, with the command
    /// that installs them.
    /// </exception>
    public static void Prepare()
    {
        if (OperatingSystem.IsMacOS())
            PrepareMacOs();
        else
            Report = $"{Platform.CurrentFamily}: using the system Vulkan loader";

        if (!Glfw.Init())
            throw new InvalidOperationException("GLFW failed to initialise");

        if (!Glfw.VulkanSupported())
            throw new PlatformNotSupportedException(
                $"GLFW cannot find a Vulkan loader. {Report}"
            );
    }

    /// <summary>Releases GLFW. Call once, after every window is gone.</summary>
    public static void Shutdown() => Glfw.Terminate();

    private static void PrepareMacOs()
    {
        var manifest = Array.Find(MacOsDriverManifests, File.Exists);
        var loaderPath = Array.Find(MacOsLoaders, File.Exists);

        if (manifest is null || loaderPath is null)
            throw new PlatformNotSupportedException(
                "Vulkan is not installed on this Mac. BRUTAL renders through Vulkan, which reaches "
                    + "Metal by way of MoltenVK, so both the loader and MoltenVK have to be present:\n"
                    + "    brew install molten-vk vulkan-loader"
            );

        // The loader reads this with getenv at instance creation. Environment.SetEnvironmentVariable
        // would only update the managed copy of the environment on Unix, which native code never
        // sees, so the libc call is the one that works.
        if (SetEnvironmentVariable("VK_DRIVER_FILES", manifest, overwrite: 1) != 0)
            throw new InvalidOperationException("failed to point the Vulkan loader at MoltenVK");

        if (!NativeLibrary.TryLoad(loaderPath, out var loader))
            throw new PlatformNotSupportedException($"the Vulkan loader at {loaderPath} could not be opened");

        if (!NativeLibrary.TryGetExport(loader, "vkGetInstanceProcAddr", out var getInstanceProcAddress))
            throw new PlatformNotSupportedException($"{loaderPath} is not a Vulkan loader");

        // Hand GLFW the loader we just opened, instead of letting it search for one it cannot find.
        // This has to happen before Glfw.Init.
        Glfw.PInvoke.InitVulkanLoader(getInstanceProcAddress);

        Report = $"macOS: MoltenVK via {loaderPath}";
    }

    [LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SetEnvironmentVariable(string name, string value, int overwrite);
}
