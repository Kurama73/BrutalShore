using System.Runtime.InteropServices;

namespace BrutalShore.Voxel;

/// <summary>
/// The triangles of a meshed chunk: packed vertices and the indices that wind them.
/// </summary>
/// <remarks>
/// <para>
/// Reusable on purpose. A world that streams re-meshes chunks constantly, and a mesh that was a
/// fresh pair of arrays every time would hand the garbage collector a few megabytes per second of
/// large-object-heap traffic. <see cref="Clear"/> keeps the capacity and the next chunk writes over
/// it.
/// </para>
/// <para>
/// Vertices are not shared between quads. Two quads meeting along an edge disagree about both their
/// normal and their occlusion there, so a shared corner would have to pick one of them and round
/// the edge off; four fresh corners per quad is what keeps a cube's edges square.
/// </para>
/// </remarks>
public sealed class VoxelMesh
{
    private readonly List<VoxelVertex> _vertices = [];
    private readonly List<uint> _indices = [];

    /// <summary>
    /// The vertices, as a span over the backing storage.
    /// </summary>
    /// <remarks>
    /// Handed out as a span so a GPU upload copies straight from this memory with no intermediate
    /// array. It is only valid until the next write, which is the normal contract for a span over a
    /// mutable collection.
    /// </remarks>
    public ReadOnlySpan<VoxelVertex> Vertices => CollectionsMarshal.AsSpan(_vertices);

    /// <summary>The indices, as a span over the backing storage.</summary>
    public ReadOnlySpan<uint> Indices => CollectionsMarshal.AsSpan(_indices);

    /// <summary>How many triangles the mesh holds.</summary>
    public int TriangleCount => _indices.Count / 3;

    /// <summary>How many quads the mesh holds; exact, since every quad owns four vertices.</summary>
    public int QuadCount => _vertices.Count / 4;

    /// <summary>Whether there is nothing to draw.</summary>
    public bool IsEmpty => _indices.Count == 0;

    /// <summary>Empties the mesh, keeping its capacity for the next chunk.</summary>
    public void Clear()
    {
        _vertices.Clear();
        _indices.Clear();
    }

    /// <summary>
    /// Appends one quad, wound <c>a</c> to <c>b</c> to <c>c</c> to <c>d</c> counter-clockwise seen
    /// from the side it faces, as two triangles.
    /// </summary>
    public void AddQuad(in VoxelVertex a, in VoxelVertex b, in VoxelVertex c, in VoxelVertex d)
    {
        var first = (uint)_vertices.Count;
        _vertices.Add(a);
        _vertices.Add(b);
        _vertices.Add(c);
        _vertices.Add(d);

        // Split along the diagonal that joins the two corners whose occlusion is closest. A quad
        // is two triangles, and the occlusion gradient across it is interpolated per triangle, so
        // the wrong diagonal puts a visible crease through the middle of a corner — the classic
        // voxel "anisotropy" artefact. Comparing the two diagonals and using the flatter one costs
        // one addition per quad and removes it.
        var alongAC = a.AmbientOcclusion + c.AmbientOcclusion;
        var alongBD = b.AmbientOcclusion + d.AmbientOcclusion;

        if (alongBD > alongAC)
        {
            _indices.Add(first + 1);
            _indices.Add(first + 2);
            _indices.Add(first + 3);
            _indices.Add(first + 1);
            _indices.Add(first + 3);
            _indices.Add(first);
        }
        else
        {
            _indices.Add(first);
            _indices.Add(first + 1);
            _indices.Add(first + 2);
            _indices.Add(first);
            _indices.Add(first + 2);
            _indices.Add(first + 3);
        }
    }
}
