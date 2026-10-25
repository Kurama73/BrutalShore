namespace RustSpace.Voxel;

/// <summary>
/// What one kind of block is: its name and how it looks.
/// </summary>
/// <remarks>
/// A block type carries appearance but no geometry and no behaviour. Geometry is the mesher's
/// business, because every solid block is the same cube and describing that cube once per type
/// would be the same cube written fifty times. Behaviour arrives when something has behaviour;
/// until then a field for it would be a field nothing reads.
/// </remarks>
public sealed record BlockType
{
    /// <summary>The name used in save files, commands and the debug overlay.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The block's colour in sRGB bytes, which is the space colours are picked and written in.
    /// </summary>
    /// <remarks>
    /// Converted to linear light in the shader rather than here. Lighting has to be done in linear
    /// space to be correct, and converting on the GPU keeps one representation of a colour in the
    /// whole pipeline: the one a person typed.
    /// </remarks>
    public required byte3 Color { get; init; }

    /// <inheritdoc />
    public override string ToString() => Name;
}
