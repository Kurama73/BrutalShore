namespace BrutalShore.Voxel;

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

    /// <summary>How much of what is behind this block it hides, from 0 to 255.</summary>
    /// <remarks>
    /// <para>
    /// 255, the default, means opaque: a face pointing into one is never built, because nothing
    /// could see it. Anything less is see-through, and three things follow at once — a face
    /// pointing into it <em>is</em> built, two blocks of the same see-through type still hide the
    /// face between them so a sea is a surface rather than a lattice, and the faces are drawn in
    /// a second pass after everything solid.
    /// </para>
    /// <para>
    /// A number rather than a flag because the value is what the shader blends with, and because
    /// the degree is the whole difference between water and glass.
    /// </para>
    /// </remarks>
    public byte Opacity { get; init; } = 255;

    /// <summary>Whether anything can be seen through this block.</summary>
    public bool IsTranslucent => Opacity < 255;

    /// <summary>Whether a body is stopped by this block.</summary>
    /// <remarks>
    /// Not the same question as being see-through: glass stops a body and water does not, and a
    /// block that is neither — tall grass — would be both solid-looking and walked through.
    /// </remarks>
    public bool Collides { get; init; } = true;

    /// <summary>Whether a body inside this block is swimming rather than falling.</summary>
    /// <remarks>
    /// Separate from <see cref="Collides" /> because they are different questions: tall grass
    /// stops nothing and holds nothing up, and water does the second without the first. What
    /// being in a liquid means is a game's to decide; that a block is one is the block's.
    /// </remarks>
    public bool IsLiquid { get; init; }

    /// <inheritdoc />
    public override string ToString() => Name;
}
