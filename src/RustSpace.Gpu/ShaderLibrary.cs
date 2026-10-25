namespace RustSpace.Gpu;

/// <summary>Compiles GLSL to SPIR-V at startup and owns the resulting shader modules.</summary>
/// <remarks>
/// <para>
/// Compiling at runtime rather than in the build keeps shaders as plain text files next to the
/// executable: editing one and restarting is the whole loop, with no build step to re-run and no
/// generated SPIR-V to keep in the repository. The cost is ShaderC in the shipping binary and a
/// few milliseconds at startup, which for a handful of shaders is not measurable.
/// </para>
/// <para>
/// Modules are cached by file name and disposed together. A <c>VkShaderModule</c> is only needed
/// while a pipeline is being created, but keeping them alive costs nothing and makes rebuilding a
/// pipeline — on a format change, say — free of a recompile.
/// </para>
/// </remarks>
public sealed unsafe class ShaderLibrary : IDisposable
{
    private readonly GpuContext _context;
    private readonly string _directory;
    private readonly Dictionary<string, VkShaderModule> _modules = [];
    private readonly Compiler _compiler;
    private readonly CompileOptions _options;

    /// <summary>Opens a shader directory and configures the compiler for this device.</summary>
    /// <param name="context">The device the modules are created on.</param>
    /// <param name="directory">Where the GLSL files live, usually next to the executable.</param>
    public ShaderLibrary(GpuContext context, string directory)
    {
        _context = context;
        _directory = directory;

        _compiler = ShaderC.CreateCompiler();
        _options = ShaderC.CreateCompilerOptions();
        _options.SetSourceLanguage(SourceLanguage.Glsl);
        _options.SetTargetEnv(TargetEnv.Vulkan, (uint)EnvVersion.Vulkan_1_3);
        _options.SetOptimizationLevel(OptimizationLevel.Performance);

        // A warning in a shader is nearly always a mistake about a type or a layout, and it is far
        // cheaper to see it at startup than to debug the geometry it produces.
        _options.SetWarningsAsErrors();
    }

    /// <summary>Compiles a shader file, or returns the module already compiled from it.</summary>
    /// <param name="fileName">The file's name within the shader directory, such as <c>voxel.vert</c>.</param>
    /// <exception cref="FileNotFoundException">If the file is not in the shader directory.</exception>
    public VkShaderModule Load(string fileName)
    {
        if (_modules.TryGetValue(fileName, out var existing))
            return existing;

        var path = Path.Combine(_directory, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Shader '{fileName}' is not in '{_directory}'.", path);

        // The stage comes from the extension, which is ShaderC's own convention and keeps the
        // stage in one place — the file name — rather than repeated at every call site.
        var kind = KindOf(fileName);

        // The UTF-8 overloads throughout: BRUTAL's analyzer rejects the string ones, since every
        // one of them would re-encode on the way to the native call.
        using var result = _compiler.Compile(File.ReadAllBytes(path), kind, new String8(fileName), "main"u8, _options);
        int length = result.GetLength();
        var module = ShaderModuleExtensions.FromSpirv(
            _context.Device,
            new ReadOnlySpan<byte>(result.GetBytes(), length),
            null
        );

        _modules[fileName] = module;
        return module;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var module in _modules.Values)
            _context.Device.DestroyShaderModule(module, null);

        _modules.Clear();
        _options.Dispose();
        _compiler.Dispose();
    }

    private static ShaderKind KindOf(string fileName) =>
        Path.GetExtension(fileName) switch
        {
            ".vert" => ShaderKind.VertexShader,
            ".frag" => ShaderKind.FragmentShader,
            ".comp" => ShaderKind.ComputeShader,
            _ => throw new ArgumentException(
                $"Cannot tell the shader stage of '{fileName}'; name it .vert, .frag or .comp.",
                nameof(fileName)
            ),
        };
}
