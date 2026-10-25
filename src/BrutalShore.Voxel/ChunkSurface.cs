namespace BrutalShore.Voxel;

/// <summary>A chunk's geometry, split by whether anything can be seen through it.</summary>
/// <remarks>
/// <para>
/// Two meshes rather than one because the two are drawn differently and in a fixed order: the
/// solid half with the depth buffer doing the sorting, then the see-through half blended over it
/// back to front. Mixing them in one buffer would mean either sorting every triangle in the world
/// every frame, or water that paints over the mountain behind it.
/// </para>
/// <para>
/// Kept as a pair rather than as two loose meshes so that one of them cannot be meshed, uploaded
/// or pooled without the other — they are built in a single pass over the chunk, and a surface
/// holding one up-to-date half and one stale one would be a seam nobody could explain.
/// </para>
/// </remarks>
public sealed class ChunkSurface
{
    /// <summary>The faces of blocks nothing can be seen through.</summary>
    public VoxelMesh Opaque { get; } = new();

    /// <summary>The faces of blocks something can be seen through.</summary>
    public VoxelMesh Translucent { get; } = new();

    /// <summary>Whether the chunk has no surface at all.</summary>
    public bool IsEmpty => Opaque.IsEmpty && Translucent.IsEmpty;

    /// <summary>Empties both halves, keeping the memory for the next chunk.</summary>
    public void Clear()
    {
        Opaque.Clear();
        Translucent.Clear();
    }
}
