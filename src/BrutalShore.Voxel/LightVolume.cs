namespace BrutalShore.Voxel;

/// <summary>
/// The light reaching every cell that one chunk's surface can be shaded with: how much of it comes
/// from the sky, and how much from blocks that glow.
/// </summary>
/// <remarks>
/// <para>
/// Two levels per cell, each 0 to 15, each spread by the same flood fill: a level moves to a
/// neighbour weakened by what that neighbour costs to cross, and the strongest claim on a cell
/// wins. Sky light starts at 15 in every cell with nothing above it and is otherwise what
/// trickles in sideways from somewhere that is, which is what makes a cave mouth bright, the
/// twentieth metre of a tunnel black, and the ground under a canopy of leaves neither.
/// </para>
/// <para>
/// Nothing is stored per chunk. A level only matters while the chunk is being meshed, where it
/// is baked into the vertices, so the volume is a worker's scratch space reused from one chunk to
/// the next — the alternative is another 32 KiB per resident chunk kept in step with every edit,
/// for a quantity the frame never reads.
/// </para>
/// <para>
/// The box is the chunk grown by sixteen blocks on all six sides, which is the smallest box that
/// gives the right answer. The mesher reads a cell one block outside the chunk, light travels
/// fifteen blocks, and a source any further out than that arrives at zero — so the dark boundary
/// of the box cannot be seen from inside the chunk, and light entering from the world beyond it
/// never needed to be known.
/// </para>
/// <para>
/// One instance per thread. It is a mutable buffer with no locking, exactly like
/// <see cref="ChunkMesher" />, and for the same reason.
/// </para>
/// </remarks>
public sealed class LightVolume
{
    /// <summary>The brightest a cell can be, and so also how far a light reaches.</summary>
    public const int MaxLevel = 15;

    /// <summary>
    /// How far the box reaches outside the chunk: the distance light travels, plus the one block
    /// outside the chunk that a face on its boundary is shaded from.
    /// </summary>
    private const int Margin = MaxLevel + 1;

    private const int Span = Chunk.Size + (Margin * 2);
    private const int CellCount = Span * Span * Span;

    private readonly byte[] _sky = new byte[CellCount];
    private readonly byte[] _block = new byte[CellCount];

    // What each cell costs to cross, cached rather than resolved through the registry inside the
    // flood fill: every cell is looked at from six sides and several times over as stronger
    // claims arrive, and a block id lookup per visit dominated everything else.
    private readonly byte[] _cost = new byte[CellCount];

    // Where each column of the box has its sky floor, as a y inside the box. Worked out once per
    // run because every column is asked about four times over, once by each of its neighbours.
    private readonly int[] _tops = new int[Span * Span];

    // Reused between runs. Queue<T>.Clear keeps the array it has, so a worker allocates this
    // once however many chunks it lights.
    private readonly Queue<int> _pending = new();

    private int _originX;
    private int _originY;
    private int _originZ;

    /// <summary>Works out the light around one chunk, replacing whatever was here before.</summary>
    /// <param name="chunkCoordinate">The chunk whose surface is about to be shaded.</param>
    /// <param name="source">Read access to the blocks, which must cover the chunk and the box around it.</param>
    /// <param name="registry">Where a block's light cost and emission are resolved.</param>
    /// <param name="skyline">Where the open sky stops, per column.</param>
    public void Illuminate(int3 chunkCoordinate, IVoxelSource source, BlockRegistry registry, ISkyline skyline)
    {
        _originX = (chunkCoordinate.X * Chunk.Size) - Margin;
        _originY = (chunkCoordinate.Y * Chunk.Size) - Margin;
        _originZ = (chunkCoordinate.Z * Chunk.Size) - Margin;

        Array.Clear(_sky);
        Array.Clear(_block);
        _pending.Clear();

        GatherCostsAndEmitters(source, registry);
        Spread(_block);
        SpreadSkyLight(skyline);
    }

    /// <summary>How much sky light reaches a cell, 0 to <see cref="MaxLevel" />.</summary>
    /// <param name="x">World x of the cell.</param>
    /// <param name="y">World y of the cell.</param>
    /// <param name="z">World z of the cell.</param>
    /// <exception cref="ArgumentOutOfRangeException">The cell is outside the box.</exception>
    public byte SkyLightAt(int x, int y, int z) => _sky[IndexOf(x, y, z)];

    /// <summary>How much light from glowing blocks reaches a cell, 0 to <see cref="MaxLevel" />.</summary>
    /// <param name="x">World x of the cell.</param>
    /// <param name="y">World y of the cell.</param>
    /// <param name="z">World z of the cell.</param>
    /// <exception cref="ArgumentOutOfRangeException">The cell is outside the box.</exception>
    public byte BlockLightAt(int x, int y, int z) => _block[IndexOf(x, y, z)];

    private int IndexOf(int x, int y, int z)
    {
        var localX = x - _originX;
        var localY = y - _originY;
        var localZ = z - _originZ;

        if ((uint)localX >= Span || (uint)localY >= Span || (uint)localZ >= Span)
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"({x}, {y}, {z}) is outside the box lit around chunk ({_originX + Margin}, {_originY + Margin}, {_originZ + Margin})."
            );

        return localX + (localZ * Span) + (localY * Span * Span);
    }

    /// <summary>
    /// Reads the box's blocks once, keeping what the flood fill needs: what each cell costs to
    /// cross, and which cells are already lit because they glow.
    /// </summary>
    /// <remarks>
    /// One pass rather than two, and an array rather than a registry lookup, because this is
    /// where the time goes: a quarter of a million cells, each one visited again from every side
    /// it has a neighbour on.
    /// </remarks>
    private void GatherCostsAndEmitters(IVoxelSource source, BlockRegistry registry)
    {
        var index = 0;
        for (var y = 0; y < Span; y++)
        for (var z = 0; z < Span; z++)
        for (var x = 0; x < Span; x++, index++)
        {
            var type = registry[source.BlockAt(_originX + x, _originY + y, _originZ + z)];

            // At least one level per block whatever the type says. A light that crossed a cell
            // for free would spread for ever along any corridor of them.
            _cost[index] = byte.Max(type.LightOpacity, 1);

            if (type.Emission == 0)
                continue;

            _block[index] = type.Emission;
            _pending.Enqueue(index);
        }
    }

    /// <summary>
    /// Fills every cell that sees the sky, then spreads that light into everything that does not.
    /// </summary>
    /// <remarks>
    /// The cells worth queueing are far fewer than the cells worth filling. Most of the box is
    /// open air hundreds of blocks above the ground, and a cell at 15 whose neighbours are all at
    /// 15 has nothing to give anyone. What has somewhere to send light is a cell of open sky
    /// beside a column whose own sky stops higher up — the face of a cliff, the mouth of a cave,
    /// the underside of a canopy — so the queue is bounded by the ground's relief rather than by
    /// the size of the box.
    /// </remarks>
    private void SpreadSkyLight(ISkyline skyline)
    {
        for (var z = 0; z < Span; z++)
        for (var x = 0; x < Span; x++)
            _tops[x + (z * Span)] = HighestInBox(skyline, _originX + x, _originZ + z);

        for (var z = 0; z < Span; z++)
        for (var x = 0; x < Span; x++)
        {
            var column = x + (z * Span);
            var top = _tops[column];
            var floor = int.Max(top + 1, 0);

            // Everything above this column's own sky floor is in the open, whether or not it has
            // anywhere to pass light on to.
            for (var y = floor; y < Span; y++)
                _sky[column + (y * Span * Span)] = MaxLevel;

            var reaches = int.Max(
                int.Max(x > 0 ? _tops[column - 1] : top, x + 1 < Span ? _tops[column + 1] : top),
                int.Max(z > 0 ? _tops[column - Span] : top, z + 1 < Span ? _tops[column + Span] : top)
            );

            // Queue the stretch of this column that stands beside something enclosed, plus the
            // one cell at the bottom, which is what lights the ground the column stands on.
            for (var y = floor; y <= int.Min(reaches + 1, Span - 1); y++)
                _pending.Enqueue(column + (y * Span * Span));
        }

        Spread(_sky);
    }

    /// <summary>
    /// The highest block in a column, as a y inside the box: below it if the column's sky stops
    /// under the box, above it if the box is entirely underground.
    /// </summary>
    private int HighestInBox(ISkyline skyline, int worldX, int worldZ)
    {
        var highest = skyline.HighestBlock(worldX, worldZ);
        if (highest == int.MinValue)
            return -1;

        return int.Clamp(highest - _originY, -1, Span);
    }

    /// <summary>
    /// Spreads whatever is already in <paramref name="levels" /> until no cell can be brightened.
    /// </summary>
    /// <remarks>
    /// Breadth first, so a cell is reached by the strongest claim before the weaker ones and is
    /// queued again only when something stronger arrives. With sixteen levels that bounds the
    /// work at a small constant per cell, which is what makes lighting a chunk comparable to
    /// meshing it rather than an order of magnitude dearer.
    /// </remarks>
    private void Spread(byte[] levels)
    {
        while (_pending.TryDequeue(out var index))
        {
            var level = levels[index];
            if (level <= 1)
                continue;

            var x = index % Span;
            var z = (index / Span) % Span;
            var y = index / (Span * Span);

            if (x > 0)
                Claim(levels, index - 1, level);
            if (x + 1 < Span)
                Claim(levels, index + 1, level);
            if (z > 0)
                Claim(levels, index - Span, level);
            if (z + 1 < Span)
                Claim(levels, index + Span, level);
            if (y > 0)
                Claim(levels, index - (Span * Span), level);
            if (y + 1 < Span)
                Claim(levels, index + (Span * Span), level);
        }
    }

    private void Claim(byte[] levels, int neighbour, byte from)
    {
        var cost = _cost[neighbour];
        if (cost >= from)
            return;

        var arriving = (byte)(from - cost);
        if (arriving <= levels[neighbour])
            return;

        levels[neighbour] = arriving;
        _pending.Enqueue(neighbour);
    }
}
