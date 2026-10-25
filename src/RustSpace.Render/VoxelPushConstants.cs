using System.Runtime.InteropServices;

namespace RustSpace.Render;

/// <summary>Everything the voxel shaders need to know, per chunk.</summary>
/// <remarks>
/// Push constants rather than a uniform buffer and descriptor sets. The whole per-draw state is
/// eighty bytes, well inside the 128 every Vulkan implementation guarantees, and pushing it costs
/// nothing to allocate, nothing to synchronise and nothing to version across frames in flight —
/// which a uniform buffer would need all three of.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct VoxelPushConstants
{
    /// <summary>The camera's combined view and projection matrix.</summary>
    public readonly float4x4 ViewProjection;

    /// <summary>
    /// The chunk's origin relative to the camera, in metres; <c>w</c> is padding.
    /// </summary>
    /// <remarks>
    /// The padding is not optional: a <c>vec3</c> in a push-constant block is aligned to sixteen
    /// bytes, so the struct has to carry the fourth float the shader's layout reserves anyway.
    /// </remarks>
    public readonly float4 ChunkOrigin;

    /// <summary>Packs the per-chunk state.</summary>
    public VoxelPushConstants(in float4x4 viewProjection, float3 chunkOrigin)
    {
        ViewProjection = viewProjection;
        ChunkOrigin = new float4(chunkOrigin.X, chunkOrigin.Y, chunkOrigin.Z, 0f);
    }
}
