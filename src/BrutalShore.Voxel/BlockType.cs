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

    /// <summary>What the block's surface looks like closer than a whole block.</summary>
    /// <remarks>
    /// Flat by default, which is what a block looks like with nothing said about it. A pattern is
    /// appearance in the same sense the colour is, which is why it sits beside it rather than in
    /// some separate description of materials: there is no behaviour attached to either.
    /// </remarks>
    public BlockPattern Pattern { get; init; } = BlockPattern.Smooth;

    /// <summary>How much light a block takes out of anything that passes through it, 0 to 15.</summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="Opacity" />, which is how much of the world behind a face the face
    /// hides. This is how much of a <em>light level</em> a block costs, and the two are not the
    /// same question: water hides very little and dims a lot, and a pane of glass would hide
    /// nothing and dim nothing.
    /// </para>
    /// <para>
    /// 15, the default, is opaque — the whole range, so no light crosses it at all. 0 means light
    /// crosses untouched, which only air does: everything else costs at least one level per block
    /// whatever this says, because a light that spread without ever weakening would fill the world.
    /// </para>
    /// </remarks>
    public byte LightOpacity { get; init; } = 15;

    /// <summary>What light level this block gives off, 0 for most of them and up to 15.</summary>
    /// <remarks>
    /// The level <em>at</em> the block. What reaches a neighbour is one less, and so on, so a
    /// block at 14 lights fourteen blocks of open air around it and nothing further.
    /// </remarks>
    public byte Emission { get; init; }

    /// <summary>How long it takes to break this block by hand, in seconds.</summary>
    /// <remarks>
    /// <para>
    /// Zero breaks the moment it is touched, which is what a block of nothing much should do.
    /// <see cref="float.PositiveInfinity" /> never breaks at all, which is how the floor of a
    /// world stays the floor of a world.
    /// </para>
    /// <para>
    /// A time rather than a hardness on some scale of its own, because a time is the thing a
    /// player experiences and the thing a designer has an opinion about. What a tool would do to
    /// it is a tool's business, and there are no tools.
    /// </para>
    /// </remarks>
    public float BreakSeconds { get; init; }

    /// <summary>Whether this block can be broken at all.</summary>
    public bool IsBreakable => !float.IsPositiveInfinity(BreakSeconds);

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
