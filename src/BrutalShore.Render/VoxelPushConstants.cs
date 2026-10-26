using System.Runtime.InteropServices;

namespace BrutalShore.Render;

/// <summary>What changes between one chunk's draw call and the next.</summary>
/// <remarks>
/// <para>
/// Where the chunk is, twice over. The first is relative to the camera and is what the geometry
/// is actually built from; the second is where it is in the world, and is only ever used to
/// decide what a block looks like.
/// </para>
/// <para>
/// Both are needed because they answer different questions. Render space moves with the camera,
/// so hashing a position in it would make every block flicker as the player walks. World space
/// does not move, but it is also too large to build geometry from in f32, which is the whole
/// reason render space exists.
/// </para>
/// <para>
/// Thirty-two bytes against a guaranteed budget of 128, which is the cheapest place there is to
/// put per-draw data.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct VoxelPushConstants
{
    /// <summary>The chunk's origin relative to the camera, in metres. w is spare padding.</summary>
    public readonly float4 ChunkOrigin;

    /// <summary>The chunk's origin in the world, in blocks. w is spare padding.</summary>
    public readonly float4 ChunkWorld;

    /// <summary>Packs a chunk's two origins for the shader.</summary>
    /// <param name="chunkOrigin">The chunk's origin relative to the camera, in metres.</param>
    /// <param name="chunkWorld">The chunk's origin in the world, in blocks.</param>
    public VoxelPushConstants(float3 chunkOrigin, int3 chunkWorld)
    {
        ChunkOrigin = new float4(chunkOrigin, 0f);
        ChunkWorld = new float4(chunkWorld.X, chunkWorld.Y, chunkWorld.Z, 0f);
    }
}
