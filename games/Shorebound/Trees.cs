using BrutalShore.Voxel;
using BrutalShore.World;

namespace Shorebound;

/// <summary>Where the trees are, and what shape each one is.</summary>
/// <remarks>
/// <para>
/// A tree is wider than it is rooted: a trunk at one coordinate puts leaves several blocks away,
/// and those leaves land in chunks the trunk is not in. That is the case
/// <see cref="IChunkGenerator" /> is written for, and the way out of it is the one its remarks
/// describe — every chunk works out, from nothing but its own coordinate and the seed, which
/// trees could reach into it, builds each of them in full, and writes only the part that falls
/// inside itself. Two chunks agree about a tree on their boundary because both did the same
/// arithmetic, not because either asked the other.
/// </para>
/// <para>
/// The world is divided into square cells, each holding at most one tree at a jittered position
/// inside it. A grid is what makes the search finite: a chunk only has to look at the cells that
/// come within a canopy's reach of it, which is a few dozen, rather than at every tree there
/// might be. The cell is also what stops two trees growing into each other, since the jitter
/// cannot push one past its own cell's edge.
/// </para>
/// <para>
/// Nothing here is stored. Walk away from a forest and back, and it is the same forest, because
/// it was never anywhere but in the arithmetic.
/// </para>
/// </remarks>
public sealed class Trees
{
    /// <summary>How wide a cell is, in blocks: one tree per cell at most.</summary>
    public const int CellBlocks = 9;

    /// <summary>How far a canopy reaches sideways from its trunk, in blocks.</summary>
    public const int ReachBlocks = 2;

    /// <summary>The tallest a tree can stand above the ground it is rooted in, in blocks.</summary>
    public const int MaxHeightBlocks = 8;

    private const int ShortestTrunk = 4;
    private const int TallestTrunk = 6;

    /// <summary>How many cells in 256 hold a tree.</summary>
    /// <remarks>
    /// About two thirds. Against a nine-block cell that is roughly a tree every eleven blocks of
    /// open ground, which reads as woodland rather than as an orchard or as scrub.
    /// </remarks>
    private const float Density = 170f;

    private readonly GameBlocks _blocks;
    private readonly int _seed;

    /// <summary>Decides where the trees of one world are.</summary>
    /// <param name="blocks">This game's block types.</param>
    /// <param name="seed">The world's seed; the same seed is the same forest.</param>
    public Trees(GameBlocks blocks, int seed)
    {
        _blocks = blocks;
        _seed = seed;
    }

    /// <summary>Writes whatever part of any tree falls inside one chunk.</summary>
    /// <param name="chunkCoordinate">Which chunk is being generated.</param>
    /// <param name="blocks">That chunk's blocks, mid bulk write.</param>
    /// <param name="ground">How high the land is and how much grows on it.</param>
    /// <remarks>
    /// Safe to call from several threads at once, like everything else a generator does: it reads
    /// the seed and the surface and writes only into the chunk it was handed.
    /// </remarks>
    public void Grow(int3 chunkCoordinate, Span<BlockId> blocks, IGround ground)
    {
        var origin = WorldCoordinates.ChunkOrigin(chunkCoordinate);

        // The cells that could reach in: the chunk's own span, widened by a canopy either way.
        var firstCellX = FloorDivide(origin.X - ReachBlocks, CellBlocks);
        var lastCellX = FloorDivide(origin.X + Chunk.Size - 1 + ReachBlocks, CellBlocks);
        var firstCellZ = FloorDivide(origin.Z - ReachBlocks, CellBlocks);
        var lastCellZ = FloorDivide(origin.Z + Chunk.Size - 1 + ReachBlocks, CellBlocks);

        for (var cellZ = firstCellZ; cellZ <= lastCellZ; cellZ++)
        for (var cellX = firstCellX; cellX <= lastCellX; cellX++)
        {
            if (TreeIn(cellX, cellZ, ground) is { } tree)
                Write(tree, origin, blocks);
        }
    }

    /// <summary>Whether a cell holds a tree, and what it looks like if so.</summary>
    /// <param name="cellX">The cell's x, in cells.</param>
    /// <param name="cellZ">The cell's z, in cells.</param>
    /// <param name="ground">How high the land is and how much grows on it.</param>
    /// <returns>The tree, or <see langword="null" /> if nothing grows there.</returns>
    public Tree? TreeIn(int cellX, int cellZ, IGround ground)
    {
        var hash = Hash(cellX, cellZ, _seed);

        // Jittered inside the cell, but never past its edge: that is what keeps two trees from
        // growing into each other without any tree having to know about another.
        var rootX = (cellX * CellBlocks) + (int)((hash >> 8) % CellBlocks);
        var rootZ = (cellZ * CellBlocks) + (int)((hash >> 16) % CellBlocks);

        // The biome's share of the forest, taken against the same hash the cell is chosen
        // with: where the share changes gradually, so does the density, and a forest thins
        // out into a plain rather than stopping at a line.
        if ((hash & 0xFF) >= Density * ground.TreeChance(rootX, rootZ))
            return null;

        var surface = ground.SurfaceHeight(rootX, rootZ);

        // Nothing takes root on a beach, in the sea, or above the snow line. Checking the ground
        // rather than the block there keeps this a function of the terrain's own shape, which is
        // what lets two chunks agree without consulting each other.
        if (surface <= TerrainGenerator.SeaLevel + 2 || surface >= TerrainGenerator.SnowLine - 4)
            return null;

        var trunk = ShortestTrunk + (int)((hash >> 24) % (uint)(TallestTrunk - ShortestTrunk + 1));
        return new Tree(new int3(rootX, surface + 1, rootZ), trunk);
    }

    private void Write(in Tree tree, int3 origin, Span<BlockId> blocks)
    {
        for (var height = 0; height < tree.TrunkBlocks; height++)
            Put(blocks, origin, tree.Root.X, tree.Root.Y + height, tree.Root.Z, _blocks.Log);

        var top = tree.Root.Y + tree.TrunkBlocks - 1;

        // Two wide rings around the top of the trunk, then a narrower cap: the shape is a
        // rounded blob rather than a cube, which is the whole difference between a tree and a
        // lollipop at this resolution.
        Ring(blocks, origin, tree.Root, top - 1, ReachBlocks, trimCorners: false);
        Ring(blocks, origin, tree.Root, top, ReachBlocks, trimCorners: true);
        Ring(blocks, origin, tree.Root, top + 1, 1, trimCorners: false);
        Ring(blocks, origin, tree.Root, top + 2, 1, trimCorners: true);
    }

    private void Ring(Span<BlockId> blocks, int3 origin, int3 root, int y, int reach, bool trimCorners)
    {
        for (var dz = -reach; dz <= reach; dz++)
        for (var dx = -reach; dx <= reach; dx++)
        {
            if (trimCorners && int.Abs(dx) == reach && int.Abs(dz) == reach)
                continue;

            Put(blocks, origin, root.X + dx, y, root.Z + dz, _blocks.Leaves);
        }
    }

    /// <summary>Writes one block, if it falls inside this chunk and there is room for it.</summary>
    /// <remarks>
    /// Leaves never replace anything: a canopy that grew into a hillside would carve a hole in it,
    /// and the trunk is the only part with a claim on the space it occupies.
    /// </remarks>
    private void Put(Span<BlockId> blocks, int3 origin, int x, int y, int z, BlockId block)
    {
        var localX = x - origin.X;
        var localY = y - origin.Y;
        var localZ = z - origin.Z;

        if (!Chunk.Contains(localX, localY, localZ))
            return;

        var index = Chunk.IndexOf(localX, localY, localZ);
        if (block == _blocks.Leaves && !blocks[index].IsAir)
            return;

        blocks[index] = block;
    }

    /// <summary>Division that rounds towards negative infinity, as a grid needs.</summary>
    /// <remarks>
    /// C# integer division rounds towards zero, so -1 / 9 is 0 and the cell to the left of the
    /// origin would be the same cell as the one at it. Half the world would be a mirror of the
    /// other half.
    /// </remarks>
    private static int FloorDivide(int value, int by) => (int)double.Floor(value / (double)by);

    private static uint Hash(int x, int z, int seed)
    {
        var hash = (uint)(x * 0x27D4EB2D) ^ (uint)(z * 0x9E3779B1) ^ (uint)(seed * 0x165667B1);
        hash ^= hash >> 15;
        hash *= 0x2C1B3C6D;
        hash ^= hash >> 12;
        hash *= 0x297A2D39;
        hash ^= hash >> 15;
        return hash;
    }
}

/// <summary>One tree: where it is rooted and how tall its trunk is.</summary>
/// <param name="Root">The lowest block of the trunk, which sits on the ground.</param>
/// <param name="TrunkBlocks">How many blocks of trunk there are.</param>
public readonly record struct Tree(int3 Root, int TrunkBlocks);
