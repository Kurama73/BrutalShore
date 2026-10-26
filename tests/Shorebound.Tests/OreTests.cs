namespace Shorebound.Tests;

/// <summary>
/// Where the ore is: that it exists at all, that depth decides which kind, and that a seam is a
/// body to follow rather than a scattering to grind through.
/// </summary>
/// <remarks>
/// These are claims about the shape of the resource layer, not about the particular blocks a
/// particular seed produces. The thresholds are generous on purpose: a test that pinned the exact
/// count would fail the first time a constant moved and would say nothing about whether the world
/// was still worth digging in.
/// </remarks>
public sealed class OreTests
{
    private readonly GameBlocks _blocks = new(new BlockRegistry());

    [Fact]
    public void stone_at_the_depths_is_sometimes_ore()
    {
        var ores = new Ores(_blocks, seed: 1337);

        Assert.True(Found(ores, lowest: 2, highest: 70) > 0);
    }

    [Fact]
    public void the_same_seed_puts_the_same_ore_in_the_same_place()
    {
        // The world is generated rather than stored, so a block is generated again every time its
        // chunk streams in. An ore that was not a function of its coordinate would appear and
        // disappear as a player walked away and came back.
        var first = new Ores(_blocks, seed: 99);
        var second = new Ores(_blocks, seed: 99);

        for (var y = 2; y < 72; y += 3)
        for (var x = -40; x < 40; x += 7)
            Assert.Equal(first.At(x, y, 11, _blocks.Stone), second.At(x, y, 11, _blocks.Stone));
    }

    [Fact]
    public void a_different_seed_puts_it_somewhere_else()
    {
        var first = new Ores(_blocks, seed: 1);
        var second = new Ores(_blocks, seed: 2);

        var same = 0;
        var total = 0;

        for (var y = 2; y < 60; y += 2)
        for (var x = -60; x < 60; x += 3)
        {
            total++;
            if (first.At(x, y, 5, _blocks.Stone) == second.At(x, y, 5, _blocks.Stone))
                same++;
        }

        // Most blocks are stone in both worlds, so near-equality is expected; identity is not.
        Assert.True(same < total);
    }

    [Fact]
    public void ember_is_only_found_deep()
    {
        var ores = new Ores(_blocks, seed: 404);

        for (var y = 48; y < VoxelWorld.HeightBlocks; y += 1)
        for (var x = -30; x < 30; x += 3)
            Assert.NotEqual(_blocks.EmberOre, ores.At(x, y, 7, _blocks.Stone));
    }

    [Fact]
    public void iron_runs_out_before_the_surface_does()
    {
        var ores = new Ores(_blocks, seed: 7);

        for (var y = 60; y < VoxelWorld.HeightBlocks; y += 1)
        for (var x = -30; x < 30; x += 3)
            Assert.NotEqual(_blocks.IronOre, ores.At(x, y, 13, _blocks.Stone));
    }

    [Fact]
    public void every_ore_turns_up_somewhere_in_the_world()
    {
        // Scanned block by block rather than on a stride: ember is rare enough and its band thin
        // enough that a sampled grid walks straight past it, which would make this test's failure
        // a statement about the stride rather than about the world.
        var ores = new Ores(_blocks, seed: 2024);
        var seen = new HashSet<BlockId>();

        for (var y = 2; y < 80; y++)
        for (var z = -32; z < 32; z++)
        for (var x = -32; x < 32; x++)
            seen.Add(ores.At(x, y, z, _blocks.Stone));

        Assert.Contains(_blocks.CoalOre, seen);
        Assert.Contains(_blocks.CopperOre, seen);
        Assert.Contains(_blocks.IronOre, seen);
        Assert.Contains(_blocks.EmberOre, seen);
    }

    [Fact]
    public void an_ore_block_has_an_ore_block_next_to_it()
    {
        // The property that makes excavation a thing a player can do. A seam has to be a connected
        // body: finding one block of it has to tell you where the next one is, or the only way to
        // mine is to remove everything and look at what is left.
        var ores = new Ores(_blocks, seed: 55);
        var found = 0;
        var withCompany = 0;

        for (var y = 4; y < 40; y++)
        for (var z = -24; z < 24; z++)
        for (var x = -24; x < 24; x++)
        {
            if (ores.At(x, y, z, _blocks.Stone) is var here && here == _blocks.Stone)
                continue;

            found++;
            if (Neighbours(ores, x, y, z, here) > 0)
                withCompany++;
        }

        Assert.True(found > 0);
        Assert.True(
            withCompany > found * 0.9,
            $"{withCompany} of {found} ore blocks touch another of the same kind"
        );
    }

    [Fact]
    public void most_of_the_rock_is_still_rock()
    {
        // The other half of the same claim. Ore everywhere is the same problem as ore nowhere:
        // nothing to look for, and a tunnel that pays for itself whichever way it is dug.
        var ores = new Ores(_blocks, seed: 31);
        var total = 0;
        var ore = 0;

        for (var y = 2; y < 72; y++)
        for (var z = -20; z < 20; z += 2)
        for (var x = -20; x < 20; x += 2)
        {
            total++;
            if (ores.At(x, y, z, _blocks.Stone) != _blocks.Stone)
                ore++;
        }

        // About one block of rock in seventeen, measured — and the floor matters as much as the
        // ceiling. At one in forty, which is where this started, only a third of shafts dug
        // straight down met any ore at all and the median one that did dug sixty-five blocks
        // first; the first metal the milestones ask for by the hundred was three bodies in a
        // sixty-metre square. Four in five shafts now meet something.
        Assert.InRange(ore / (double)total, 0.03, 0.08);
    }

    private static int Found(Ores ores, int lowest, int highest)
    {
        var found = 0;

        for (var y = lowest; y < highest; y++)
        for (var z = -20; z < 20; z += 2)
        for (var x = -20; x < 20; x += 2)
        {
            if (ores.At(x, y, z, new BlockId(1)) != new BlockId(1))
                found++;
        }

        return found;
    }

    private int Neighbours(Ores ores, int x, int y, int z, BlockId block)
    {
        var touching = 0;

        foreach (var (dx, dy, dz) in (ReadOnlySpan<(int, int, int)>)
            [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)])
        {
            if (ores.At(x + dx, y + dy, z + dz, _blocks.Stone) == block)
                touching++;
        }

        return touching;
    }
}
