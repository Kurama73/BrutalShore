using System.Runtime.InteropServices;

namespace RustSpace.Voxel;

/// <summary>
/// One corner of one meshed quad, packed into eight bytes.
/// </summary>
/// <remarks>
/// <para>
/// A voxel surface is the one case where packing a vertex by hand is clearly worth it. The
/// quantities are all small integers — a position on a 33-point lattice, one of six face
/// directions, one of four occlusion levels, a colour — and there are millions of them, so the
/// difference between eight bytes and a comfortable thirty-two is the difference between a view
/// distance that fits in memory and bandwidth and one that does not.
/// </para>
/// <para>
/// Two things are deliberately <em>not</em> in here. The texture coordinate is not, because the
/// shader can derive it from the position: every quad is axis-aligned, so the two position
/// components that are not along the face's normal already are the coordinate across it, which is
/// also what makes a merged quad tile per cell instead of stretching. And the block id is not,
/// because the mesh is geometry: the colour has already been resolved from the registry, and
/// anything that needs to know which block it is asks the chunk, which is where the answer is
/// authoritative.
/// </para>
/// <para>
/// Layout, little-endian, as the shader unpacks it:
/// <code>
/// Packed0:  bits  0..5   x, in chunk-local voxel units, 0..32
///           bits  6..11  y
///           bits 12..17  z
///           bits 18..20  face, a <see cref="VoxelFace"/>
///           bits 21..22  ambient occlusion, 0 darkest to 3 unoccluded
/// Packed1:  bits  0..7   red, sRGB
///           bits  8..15  green
///           bits 16..23  blue
/// </code>
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct VoxelVertex
{
    /// <summary>Position, face and occlusion.</summary>
    public readonly uint Packed0;

    /// <summary>Colour.</summary>
    public readonly uint Packed1;

    /// <summary>Packs one corner.</summary>
    /// <param name="x">Chunk-local x in voxel units, 0 to <see cref="Chunk.Size"/> inclusive.</param>
    /// <param name="y">Chunk-local y in voxel units, 0 to <see cref="Chunk.Size"/> inclusive.</param>
    /// <param name="z">Chunk-local z in voxel units, 0 to <see cref="Chunk.Size"/> inclusive.</param>
    /// <param name="face">Which way the quad faces.</param>
    /// <param name="ambientOcclusion">0 darkest to 3 unoccluded.</param>
    /// <param name="color">The block's colour in sRGB bytes.</param>
    public VoxelVertex(int x, int y, int z, VoxelFace face, int ambientOcclusion, byte3 color)
    {
        Packed0 =
            (uint)x
            | ((uint)y << 6)
            | ((uint)z << 12)
            | ((uint)face << 18)
            | ((uint)ambientOcclusion << 21);

        Packed1 = color.X | ((uint)color.Y << 8) | ((uint)color.Z << 16);
    }

    /// <summary>The chunk-local position this corner was packed with.</summary>
    public int3 Position => new((int)(Packed0 & 0x3F), (int)((Packed0 >> 6) & 0x3F), (int)((Packed0 >> 12) & 0x3F));

    /// <summary>The face this corner was packed with.</summary>
    public VoxelFace Face => (VoxelFace)((Packed0 >> 18) & 0x7);

    /// <summary>The occlusion level this corner was packed with.</summary>
    public int AmbientOcclusion => (int)((Packed0 >> 21) & 0x3);

    /// <summary>The colour this corner was packed with.</summary>
    public byte3 Color =>
        new((byte)(Packed1 & 0xFF), (byte)((Packed1 >> 8) & 0xFF), (byte)((Packed1 >> 16) & 0xFF));
}
