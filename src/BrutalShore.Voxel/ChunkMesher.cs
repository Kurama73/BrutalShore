namespace BrutalShore.Voxel;

/// <summary>
/// Turns a chunk into the triangles of its visible surface.
/// </summary>
/// <remarks>
/// <para>
/// Three reductions happen, in this order, and each one matters at a different scale:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <b>Face culling.</b> A face between two solid blocks is never seen, so it is never built. This
/// is what turns a solid 32³ chunk from 196,608 triangles into 12,288.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Seam culling across chunks.</b> The block on the far side of a face is read through an
/// <see cref="IVoxelSource"/> rather than from the chunk, so a face on the chunk's own boundary is
/// culled against its neighbour's blocks exactly as an interior one is. Without this every chunk
/// boundary is a double-sided wall, and the wall is where most of a streamed world's triangles
/// would go.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Greedy merging.</b> Adjacent faces on the same plane, facing the same way, of the same
/// colour, with the same occlusion at every corner, become one rectangle. A merged rectangle is
/// flat, one colour and one normal, so nothing is lost. A solid 32³ chunk falls from 12,288
/// triangles to 12.
/// </description>
/// </item>
/// </list>
/// <para>
/// Ambient occlusion is computed per face corner and is part of the merge key. That pairing is not
/// optional: occlusion varies across a surface, so merging two faces whose corners are shaded
/// differently would have to discard one of the two shadings, and the result is the familiar voxel
/// artefact where a wall's shading snaps between runs. Including it in the key costs some merging
/// at the edges of shadows and nowhere else.
/// </para>
/// <para>
/// The mesh is in chunk-local voxel units with the chunk's minimum corner at the origin: block
/// <c>(i, j, k)</c> occupies the cube from <c>(i, j, k)</c> to <c>(i+1, j+1, k+1)</c>. Where the
/// chunk sits in the world is the caller's transform, not arithmetic repeated over every vertex.
/// </para>
/// <para>
/// One instance is reusable and holds its working buffer, so meshing a stream of chunks allocates
/// nothing. It is not thread-safe; a worker that meshes in parallel gives each thread its own.
/// </para>
/// </remarks>
public sealed class ChunkMesher
{
    /// <summary>
    /// One face in the plane being merged: what it looks like, and therefore what it may merge with.
    /// </summary>
    private readonly record struct MaskCell(
        byte3 Color,
        byte Opacity,
        byte Occlusion0,
        byte Occlusion1,
        byte Occlusion2,
        byte Occlusion3
    )
    {
        /// <summary>No face here.</summary>
        public static MaskCell None => default;

        /// <summary>Whether there is a face here at all.</summary>
        public bool Exists { get; init; }
    }

    private readonly MaskCell[] _mask = new MaskCell[Chunk.Size * Chunk.Size];

    /// <summary>
    /// Meshes <paramref name="chunk"/>, which sits at <paramref name="chunkCoordinate"/> in chunk
    /// units, into <paramref name="into"/>, which is cleared first. Solid faces and see-through
    /// ones land in the two halves of the surface.
    /// </summary>
    /// <param name="chunk">The chunk whose surface is wanted.</param>
    /// <param name="chunkCoordinate">Where that chunk sits, in chunk units.</param>
    /// <param name="source">
    /// Read access to the world, used for the blocks just outside the chunk. It must agree with
    /// <paramref name="chunk"/> about the chunk's own blocks.
    /// </param>
    /// <param name="registry">Where a block id's colour and opacity are resolved.</param>
    /// <param name="into">The surface to fill; both halves are cleared first.</param>
    public void Mesh(
        Chunk chunk,
        int3 chunkCoordinate,
        IVoxelSource source,
        BlockRegistry registry,
        ChunkSurface into
    )
    {
        into.Clear();

        // An all-air chunk has no faces at all. Worth checking because in a terrain world most
        // loaded chunks are sky.
        if (chunk.IsEmpty)
            return;

        var originX = chunkCoordinate.X * Chunk.Size;
        var originY = chunkCoordinate.Y * Chunk.Size;
        var originZ = chunkCoordinate.Z * Chunk.Size;

        for (var axis = 0; axis < 3; axis++)
        {
            // u and v are the axis's cyclic successors, so that u cross v is +axis. That is what
            // makes one winding rule work for all three axes instead of needing a sign table.
            var u = (axis + 1) % 3;
            var v = (axis + 2) % 3;

            // Planes from 0 to Size inclusive: both outer surfaces of the chunk get one. A plane
            // whose owning block falls outside the chunk produces nothing, which is how the two
            // ends take care of themselves.
            for (var offset = 0; offset <= Chunk.Size; offset++)
            {
                for (var facing = 0; facing < 2; facing++)
                {
                    var facingPositive = facing == 0;
                    var face = (VoxelFace)(axis * 2 + facing);
                    GatherFaces(chunk, source, registry, originX, originY, originZ, axis, u, v, offset, facingPositive);
                    MergeAndEmit(into, axis, u, v, offset, facingPositive, face);
                }
            }
        }
    }

    /// <summary>
    /// Writes into the mask every face of this plane that is seen, and nothing where it is not.
    /// </summary>
    /// <remarks>
    /// The asymmetry between the two reads is the whole point and is easy to undo by accident. The
    /// <em>owning</em> block is read from the chunk, so a plane at the chunk's edge whose owner
    /// would be outside produces no face — the neighbouring chunk owns that face and will build it.
    /// The <em>facing</em> block is read from the source, so a face on the boundary is culled
    /// against what is actually next door. Reading both from the chunk gives seams; reading both
    /// from the source gives every face twice.
    /// </remarks>
    private void GatherFaces(
        Chunk chunk,
        IVoxelSource source,
        BlockRegistry registry,
        int originX,
        int originY,
        int originZ,
        int axis,
        int u,
        int v,
        int offset,
        bool facingPositive
    )
    {
        var ownerOffset = facingPositive ? offset - 1 : offset;
        var facingOffset = facingPositive ? offset : offset - 1;

        Span<int> owner = stackalloc int[3];
        Span<int> facing = stackalloc int[3];

        for (var j = 0; j < Chunk.Size; j++)
        {
            for (var i = 0; i < Chunk.Size; i++)
            {
                _mask[i + Chunk.Size * j] = MaskCell.None;

                owner[axis] = ownerOffset;
                owner[u] = i;
                owner[v] = j;

                if (!Chunk.Contains(owner[0], owner[1], owner[2]))
                    continue;

                var ownerBlock = chunk[owner[0], owner[1], owner[2]];
                if (ownerBlock.IsAir)
                    continue;

                facing[axis] = facingOffset;
                facing[u] = i;
                facing[v] = j;

                var facingWorldX = originX + facing[0];
                var facingWorldY = originY + facing[1];
                var facingWorldZ = originZ + facing[2];

                var facingBlock = source.BlockAt(facingWorldX, facingWorldY, facingWorldZ);
                var ownerType = registry[ownerBlock];
                if (IsHidden(ownerBlock, ownerType, facingBlock, registry))
                    continue;

                _mask[i + Chunk.Size * j] = new MaskCell(
                    ownerType.Color,
                    ownerType.Opacity,
                    Occlusion(source, u, v, facingWorldX, facingWorldY, facingWorldZ, -1, -1),
                    Occlusion(source, u, v, facingWorldX, facingWorldY, facingWorldZ, +1, -1),
                    Occlusion(source, u, v, facingWorldX, facingWorldY, facingWorldZ, +1, +1),
                    Occlusion(source, u, v, facingWorldX, facingWorldY, facingWorldZ, -1, +1)
                )
                {
                    Exists = true,
                };
            }
        }
    }

    /// <summary>Whether the block in front of a face hides it well enough not to build it.</summary>
    /// <remarks>
    /// <para>
    /// Air hides nothing. An opaque block hides everything. A see-through block hides only a face
    /// of its own kind — which is the rule that turns a body of water into a surface instead of a
    /// lattice of every cell's six sides, while still letting the sand underneath be drawn.
    /// </para>
    /// <para>
    /// Comparing the ids rather than the types is deliberate: two different see-through blocks
    /// meeting should show the boundary between them, and two of the same should not.
    /// </para>
    /// </remarks>
    private static bool IsHidden(BlockId owner, BlockType ownerType, BlockId facing, BlockRegistry registry)
    {
        if (facing.IsAir)
            return false;

        if (!registry[facing].IsTranslucent)
            return true;

        return ownerType.IsTranslucent && owner == facing;
    }

    /// <summary>
    /// How shadowed one corner of a face is, from 0 for a corner wedged into an inside angle to 3
    /// for one out in the open.
    /// </summary>
    /// <remarks>
    /// The three blocks that can shade a corner are the two beside it along the face's own axes and
    /// the one diagonally between them, all on the open side of the face. Two sides present means
    /// the corner is in an interior angle and gets the darkest value regardless of the diagonal,
    /// which is what stops a seam appearing along the inside of a corner. This is the standard
    /// voxel occlusion rule and it is cheap: three block reads per corner, twelve per face, all of
    /// them hitting the same chunk the face came from.
    /// </remarks>
    private static byte Occlusion(
        IVoxelSource source,
        int u,
        int v,
        int facingX,
        int facingY,
        int facingZ,
        int stepU,
        int stepV
    )
    {
        Span<int> offset = stackalloc int[3];

        offset.Clear();
        offset[u] = stepU;
        var sideU = source.BlockAt(facingX + offset[0], facingY + offset[1], facingZ + offset[2]).IsSolid;

        offset.Clear();
        offset[v] = stepV;
        var sideV = source.BlockAt(facingX + offset[0], facingY + offset[1], facingZ + offset[2]).IsSolid;

        if (sideU && sideV)
            return 0;

        offset.Clear();
        offset[u] = stepU;
        offset[v] = stepV;
        var diagonal = source.BlockAt(facingX + offset[0], facingY + offset[1], facingZ + offset[2]).IsSolid;

        return (byte)(3 - ((sideU ? 1 : 0) + (sideV ? 1 : 0) + (diagonal ? 1 : 0)));
    }

    /// <summary>
    /// Emits one quad per maximal rectangle of identical faces in the mask, consuming it as it goes.
    /// </summary>
    /// <remarks>
    /// Rectangles grow along u first and then along v: the usual greedy sweep. It does not find the
    /// fewest rectangles that cover the mask — that problem costs far more than the drawing it
    /// would save — but it is linear in the mask, and because every cell it emits it also clears,
    /// it can neither emit two overlapping rectangles nor leave a face uncovered.
    /// </remarks>
    private void MergeAndEmit(
        ChunkSurface into,
        int axis,
        int u,
        int v,
        int offset,
        bool facingPositive,
        VoxelFace face
    )
    {
        for (var j = 0; j < Chunk.Size; j++)
        {
            var i = 0;
            while (i < Chunk.Size)
            {
                var cell = _mask[i + Chunk.Size * j];
                if (!cell.Exists)
                {
                    i++;
                    continue;
                }

                var width = 1;
                while (i + width < Chunk.Size && _mask[i + width + Chunk.Size * j] == cell)
                    width++;

                var height = 1;
                while (j + height < Chunk.Size)
                {
                    var rowMatches = true;
                    for (var step = 0; step < width; step++)
                    {
                        if (_mask[i + step + Chunk.Size * (j + height)] != cell)
                        {
                            rowMatches = false;
                            break;
                        }
                    }

                    if (!rowMatches)
                        break;

                    height++;
                }

                for (var row = 0; row < height; row++)
                    for (var column = 0; column < width; column++)
                        _mask[i + column + Chunk.Size * (j + row)] = MaskCell.None;

                // The merge has already kept the two kinds apart: a cell's opacity is part of
                // what makes two of them equal, so no merged quad is ever half one and half
                // the other.
                var mesh = cell.Opacity == byte.MaxValue ? into.Opaque : into.Translucent;
                Emit(mesh, cell, axis, u, v, offset, i, j, width, height, facingPositive, face);
                i += width;
            }
        }
    }

    /// <summary>
    /// Appends the quad for one merged rectangle, wound counter-clockwise seen from the side it
    /// faces.
    /// </summary>
    /// <remarks>
    /// The two windings differ only by reversal, and the occlusion values travel with their
    /// corners through that reversal rather than staying in place — a corner's shading belongs to
    /// the corner, not to its position in the list.
    /// </remarks>
    private static void Emit(
        VoxelMesh into,
        in MaskCell cell,
        int axis,
        int u,
        int v,
        int offset,
        int i,
        int j,
        int width,
        int height,
        bool facingPositive,
        VoxelFace face
    )
    {
        if (facingPositive)
            into.AddQuad(
                Corner(axis, u, v, offset, i, j, 0, 0, face, cell.Occlusion0, cell),
                Corner(axis, u, v, offset, i, j, width, 0, face, cell.Occlusion1, cell),
                Corner(axis, u, v, offset, i, j, width, height, face, cell.Occlusion2, cell),
                Corner(axis, u, v, offset, i, j, 0, height, face, cell.Occlusion3, cell)
            );
        else
            into.AddQuad(
                Corner(axis, u, v, offset, i, j, 0, 0, face, cell.Occlusion0, cell),
                Corner(axis, u, v, offset, i, j, 0, height, face, cell.Occlusion3, cell),
                Corner(axis, u, v, offset, i, j, width, height, face, cell.Occlusion2, cell),
                Corner(axis, u, v, offset, i, j, width, 0, face, cell.Occlusion1, cell)
            );
    }

    /// <summary>One corner of a merged rectangle, in chunk-local voxel units.</summary>
    private static VoxelVertex Corner(
        int axis,
        int u,
        int v,
        int offset,
        int i,
        int j,
        int deltaU,
        int deltaV,
        VoxelFace face,
        int occlusion,
        in MaskCell cell
    )
    {
        Span<int> position = stackalloc int[3];
        position[axis] = offset;
        position[u] = i + deltaU;
        position[v] = j + deltaV;
        return new VoxelVertex(position[0], position[1], position[2], face, occlusion, cell.Color, cell.Opacity);
    }
}
