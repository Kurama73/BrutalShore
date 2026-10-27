namespace BrutalShore.Render;

/// <summary>Which part of a texture layer each face of a loose box is cut from.</summary>
/// <remarks>
/// <para>
/// A block's texture tiles: every face of it samples the whole layer, which is why a cube in the
/// world needs nothing but a layer number. A creature's does not. Its skin is one sheet with the
/// head, the body and a leg laid out side by side on it, and a box drawn from that sheet has to
/// be told which rectangle of it belongs to which face, or every face of its head comes out as
/// the whole sheet squashed into a square.
/// </para>
/// <para>
/// Four rectangles rather than six. The two sides of a box share one, and so do its top and its
/// bottom, because a creature is symmetrical about the axis it walks along and the saving is what
/// keeps the whole thing inside the hundred and twenty-eight bytes of push constants that every
/// Vulkan implementation is required to offer. Facing and away do not share one: a head whose
/// back wore its face would be a head with two of them.
/// </para>
/// </remarks>
/// <param name="Side">The rectangle the two faces across the box's width are cut from.</param>
/// <param name="Top">The rectangle its top and bottom are cut from.</param>
/// <param name="Front">The rectangle the face pointing the way it is turned is cut from.</param>
/// <param name="Back">The rectangle the face pointing away is cut from.</param>
public readonly record struct CubeSkin(float4 Side, float4 Top, float4 Front, float4 Back)
{
    /// <summary>The whole layer on every face, which is what a block wants.</summary>
    public static CubeSkin Whole { get; } = new(Full, Full, Full, Full);

    private static float4 Full => new(0f, 0f, 1f, 1f);
}
