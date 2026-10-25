using System.Runtime.InteropServices;

namespace BrutalShore.Render;

/// <summary>What changes between one chunk's draw call and the next.</summary>
/// <remarks>
/// Only the chunk's origin: everything else a voxel shader reads is the same for the whole frame
/// and lives in <see cref="SceneUniforms" />. Keeping this to sixteen bytes matters because the
/// guaranteed push constant budget is 128 and it is the cheapest place to put per-draw data.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct VoxelPushConstants
{
    /// <summary>The chunk's origin relative to the camera, in metres. w is spare padding.</summary>
    public readonly float4 ChunkOrigin;

    /// <summary>Packs a chunk's origin for the shader.</summary>
    /// <param name="chunkOrigin">The chunk's origin relative to the camera, in metres.</param>
    public VoxelPushConstants(float3 chunkOrigin) => ChunkOrigin = new float4(chunkOrigin, 0f);
}
