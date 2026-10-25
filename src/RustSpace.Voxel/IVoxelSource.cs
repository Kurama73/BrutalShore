namespace RustSpace.Voxel;

/// <summary>
/// Read access to blocks by absolute world coordinate, however they are stored.
/// </summary>
/// <remarks>
/// <para>
/// This exists for one reason: the mesher has to see one block past the chunk it is meshing. A
/// chunk that only knows its own 32³ blocks reads everything outside itself as air, so it builds
/// the faces along its own boundary — and the result is a visible wall between every pair of
/// neighbouring chunks, drawn twice, from both sides. Letting the mesher ask for a coordinate
/// outside the chunk is what removes those seams.
/// </para>
/// <para>
/// It is an interface rather than a concrete world because the test suite needs the second
/// implementation: a hand-written source of a few blocks is how the mesher's output is pinned down
/// without generating terrain or loading a world.
/// </para>
/// </remarks>
public interface IVoxelSource
{
    /// <summary>
    /// The block at an absolute world coordinate, or <see cref="BlockId.Air"/> where nothing is
    /// loaded or the world does not reach.
    /// </summary>
    /// <remarks>
    /// Returning air for an unloaded coordinate is a deliberate choice with a visible consequence:
    /// the edge of the loaded world grows the faces that face into the void. The alternative —
    /// treating unloaded space as solid — hides those faces but then shows holes the moment a
    /// chunk does load. A caller that cares meshes only chunks whose neighbours are present.
    /// </remarks>
    BlockId BlockAt(int x, int y, int z);
}
