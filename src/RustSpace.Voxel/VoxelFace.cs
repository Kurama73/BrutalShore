namespace RustSpace.Voxel;

/// <summary>
/// Which of a cube's six faces a quad is, and so which way its normal points.
/// </summary>
/// <remarks>
/// The order is the one the shader indexes a normal table with, so it is part of the vertex format
/// and changing it means changing the shader. Paired by axis — positive then negative — because
/// that is the order the mesher walks its planes in.
/// </remarks>
public enum VoxelFace
{
    /// <summary>Facing +X.</summary>
    PositiveX = 0,

    /// <summary>Facing -X.</summary>
    NegativeX = 1,

    /// <summary>Facing +Y, which is up.</summary>
    PositiveY = 2,

    /// <summary>Facing -Y, which is down.</summary>
    NegativeY = 3,

    /// <summary>Facing +Z.</summary>
    PositiveZ = 4,

    /// <summary>Facing -Z.</summary>
    NegativeZ = 5,
}
