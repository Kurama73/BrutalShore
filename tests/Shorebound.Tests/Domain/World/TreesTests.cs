using Shorebound.Domain.World;

namespace Shorebound.Tests.Domain.World;

/// <summary>
/// Pins down the one thing a structure wider than a chunk has to get right: both chunks either
/// side of it build the same tree, having never spoken to each other.
/// </summary>
/// <remarks>
/// The failure this guards against is a seam — half a canopy, a trunk cut off at a chunk edge —
/// and it only appears at particular coordinates, which makes it exactly the kind of thing that
/// is found by a player and not by looking.
/// </remarks>
public sealed class TreesTests
{
    /// <summary>A flat surface high enough for trees and clear of the snow line.</summary>
    private const int Ground = 86;

    /// <summary>Flat ground at a height trees like, with the whole of the usual forest on it.</summary>
    private static readonly IGround FlatGround = new Everywhere(Ground, treeChance: 1f);

    [Fact]
    public void a_tree_on_a_boundary_is_built_the_same_way_by_both_chunks()
    {
        var blocks = new GameBlocks(new BlockRegistry());
        var trees = new Trees(blocks, seed: 1337);
        var (tree, chunkZ) = FindTreeCrossingTheFirstBoundary(trees);

        var left = Grow(trees, new int3(0, 2, chunkZ));
        var right = Grow(trees, new int3(1, 2, chunkZ));

        // Every block the canopy covers, checked against whichever chunk owns it. A chunk that
        // built a different tree, or no tree, would disagree on its half of it.
        var checked_ = 0;
        foreach (var world in CanopyBlocks(tree))
        {
            var chunk = world.X < Chunk.Size ? left : right;
            var localX = world.X - (world.X < Chunk.Size ? 0 : Chunk.Size);
            var local = new int3(localX, world.Y - (2 * Chunk.Size), world.Z - (chunkZ * Chunk.Size));

            if (!Chunk.Contains(local.X, local.Y, local.Z))
                continue;

            Assert.False(chunk[local].IsAir, $"{world} was empty in the chunk that owns it");
            checked_++;
        }

        Assert.True(checked_ > 20, $"only {checked_} blocks of the tree were checked");
    }

    [Fact]
    public void a_canopy_reaches_into_a_chunk_its_trunk_is_not_in()
    {
        var blocks = new GameBlocks(new BlockRegistry());
        var trees = new Trees(blocks, seed: 1337);
        var (tree, chunkZ) = FindTreeCrossingTheFirstBoundary(trees);

        var right = Grow(trees, new int3(1, 2, chunkZ));

        // The trunk is in the left chunk, so the right one knows about this tree only because it
        // went looking for trees that could reach it.
        Assert.True(tree.Root.X < Chunk.Size);
        Assert.Contains(right.Blocks.ToArray(), block => block == blocks.Leaves);
    }

    [Fact]
    public void the_same_seed_grows_the_same_forest()
    {
        var blocks = new GameBlocks(new BlockRegistry());

        var first = Grow(new Trees(blocks, seed: 99), new int3(3, 2, -4));
        var second = Grow(new Trees(blocks, seed: 99), new int3(3, 2, -4));

        // The whole promise of a generated world: walk away and back, close the game and reopen
        // it, and the forest is the forest it was.
        Assert.Equal(first.Blocks.ToArray(), second.Blocks.ToArray());
    }

    [Fact]
    public void a_different_seed_grows_a_different_forest()
    {
        var blocks = new GameBlocks(new BlockRegistry());

        var first = Grow(new Trees(blocks, seed: 99), new int3(3, 2, -4));
        var second = Grow(new Trees(blocks, seed: 100), new int3(3, 2, -4));

        Assert.NotEqual(first.Blocks.ToArray(), second.Blocks.ToArray());
    }

    [Fact]
    public void cells_on_either_side_of_the_origin_are_different_cells()
    {
        var trees = new Trees(new GameBlocks(new BlockRegistry()), seed: 7);

        // Integer division rounds towards zero, so a grid built on it folds the world in half at
        // the origin and every tree west of it has a twin to the east.
        var west = trees.TreeIn(-1, 0, FlatGround);
        var east = trees.TreeIn(0, 0, FlatGround);

        Assert.True(west is null || east is null || west.Value.Root.X != east.Value.Root.X);
    }

    [Fact]
    public void nothing_takes_root_on_a_beach_or_in_the_sea()
    {
        var trees = new Trees(new GameBlocks(new BlockRegistry()), seed: 1337);

        for (var cellZ = -20; cellZ <= 20; cellZ++)
        for (var cellX = -20; cellX <= 20; cellX++)
            Assert.Null(trees.TreeIn(cellX, cellZ, new Everywhere(TerrainGenerator.SeaLevel, treeChance: 1f)));
    }

    [Fact]
    public void nothing_takes_root_above_the_snow_line()
    {
        var trees = new Trees(new GameBlocks(new BlockRegistry()), seed: 1337);

        for (var cellZ = -20; cellZ <= 20; cellZ++)
        for (var cellX = -20; cellX <= 20; cellX++)
            Assert.Null(trees.TreeIn(cellX, cellZ, new Everywhere(TerrainGenerator.SnowLine, treeChance: 1f)));
    }

    [Fact]
    public void a_tree_always_stands_inside_its_own_cell()
    {
        // Which is what keeps two of them from growing into each other, without any tree having
        // to know that another exists.
        var trees = new Trees(new GameBlocks(new BlockRegistry()), seed: 1337);

        for (var cellZ = -8; cellZ <= 8; cellZ++)
        for (var cellX = -8; cellX <= 8; cellX++)
        {
            if (trees.TreeIn(cellX, cellZ, FlatGround) is not { } tree)
                continue;

            Assert.InRange(tree.Root.X, cellX * Trees.CellBlocks, ((cellX + 1) * Trees.CellBlocks) - 1);
            Assert.InRange(tree.Root.Z, cellZ * Trees.CellBlocks, ((cellZ + 1) * Trees.CellBlocks) - 1);
        }
    }

    [Fact]
    public void a_tree_stands_on_the_ground_rather_than_in_it_or_over_it()
    {
        var trees = new Trees(new GameBlocks(new BlockRegistry()), seed: 1337);

        var standing = Enumerable
            .Range(-8, 17)
            .SelectMany(cellX => Enumerable.Range(-8, 17).Select(cellZ => trees.TreeIn(cellX, cellZ, FlatGround)))
            .Where(tree => tree is not null)
            .ToArray();

        Assert.NotEmpty(standing);
        Assert.All(standing, tree => Assert.Equal(Ground + 1, tree!.Value.Root.Y));
    }

    [Fact]
    public void a_biome_with_no_trees_in_it_grows_none()
    {
        var trees = new Trees(new GameBlocks(new BlockRegistry()), seed: 1337);
        var barren = new Everywhere(Ground, treeChance: 0f);

        for (var cellZ = -10; cellZ <= 10; cellZ++)
        for (var cellX = -10; cellX <= 10; cellX++)
            Assert.Null(trees.TreeIn(cellX, cellZ, barren));
    }

    [Fact]
    public void a_thinner_biome_grows_a_subset_of_the_same_forest()
    {
        var trees = new Trees(new GameBlocks(new BlockRegistry()), seed: 1337);
        var thick = new Everywhere(Ground, treeChance: 1f);
        var thin = new Everywhere(Ground, treeChance: 0.3f);

        var thickCount = 0;
        var thinCount = 0;

        for (var cellZ = -20; cellZ <= 20; cellZ++)
        for (var cellX = -20; cellX <= 20; cellX++)
        {
            var dense = trees.TreeIn(cellX, cellZ, thick);
            var sparse = trees.TreeIn(cellX, cellZ, thin);

            if (dense is not null)
                thickCount++;

            if (sparse is null)
                continue;

            thinCount++;

            // Thinning takes trees away rather than moving them, which is what lets a forest
            // fade into a plain instead of the two having a seam between them.
            Assert.Equal(dense, sparse);
        }

        Assert.True(thinCount > 0, "a biome with some trees grew none");
        Assert.True(thinCount < thickCount, $"{thinCount} is not fewer than {thickCount}");
    }

    /// <summary>Ground of one height, with one tree density, everywhere.</summary>
    private sealed class Everywhere(int height, float treeChance) : IGround
    {
        public int SurfaceHeight(int blockX, int blockZ) => height;

        public float TreeChance(int blockX, int blockZ) => treeChance;
    }

    /// <summary>Generates one chunk of nothing but trees, over flat ground.</summary>
    private static Chunk Grow(Trees trees, int3 chunkCoordinate)
    {
        var chunk = new Chunk();
        trees.Grow(chunkCoordinate, chunk.BeginBulkWrite(), FlatGround);
        chunk.EndBulkWrite();
        return chunk;
    }

    /// <summary>
    /// A tree whose trunk is just inside the first chunk and whose canopy reaches past its edge,
    /// along with the chunk row it stands in.
    /// </summary>
    /// <remarks>
    /// Searched for rather than hard-coded, because which cells hold a tree is the hash's
    /// business and pinning a coordinate here would make this test a test of the hash.
    /// </remarks>
    private static (Tree Tree, int ChunkZ) FindTreeCrossingTheFirstBoundary(Trees trees)
    {
        for (var cellZ = 0; cellZ < 200; cellZ++)
        for (var cellX = 0; cellX < 6; cellX++)
        {
            if (trees.TreeIn(cellX, cellZ, FlatGround) is not { } tree)
                continue;

            // Crossing the boundary at x = 32, and well inside its own chunk along z so the test
            // is about one boundary rather than two.
            if (tree.Root.X >= Chunk.Size || tree.Root.X + Trees.ReachBlocks < Chunk.Size)
                continue;

            var withinZ = tree.Root.Z - (WorldCoordinates.ToChunk(tree.Root.Z) * Chunk.Size);
            if (withinZ < Trees.ReachBlocks || withinZ >= Chunk.Size - Trees.ReachBlocks)
                continue;

            return (tree, WorldCoordinates.ToChunk(tree.Root.Z));
        }

        throw new InvalidOperationException("no tree in this forest crosses the boundary being tested");
    }

    /// <summary>Every column the canopy covers, at the height of its thickest ring.</summary>
    private static IEnumerable<int3> CanopyBlocks(Tree tree)
    {
        var top = tree.Root.Y + tree.TrunkBlocks - 1;

        for (var dz = -Trees.ReachBlocks; dz <= Trees.ReachBlocks; dz++)
        for (var dx = -Trees.ReachBlocks; dx <= Trees.ReachBlocks; dx++)
            yield return new int3(tree.Root.X + dx, top - 1, tree.Root.Z + dz);

        for (var height = 0; height < tree.TrunkBlocks; height++)
            yield return new int3(tree.Root.X, tree.Root.Y + height, tree.Root.Z);
    }
}
