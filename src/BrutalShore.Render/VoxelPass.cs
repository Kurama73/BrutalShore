namespace BrutalShore.Render;

/// <summary>Which half of a chunk's surface a pipeline draws, and therefore how.</summary>
/// <remarks>
/// The two differ in three settings — blending, depth writing and face culling — and in when they
/// run. Everything else about them is the same, which is why they are one pipeline class told
/// which it is rather than two classes that would drift.
/// </remarks>
public enum VoxelPass
{
    /// <summary>Blocks nothing can be seen through. Drawn first, writing depth, back faces culled.</summary>
    Opaque,

    /// <summary>
    /// Blocks something can be seen through. Drawn after, blended, writing no depth, and from
    /// both sides.
    /// </summary>
    Translucent,
}
