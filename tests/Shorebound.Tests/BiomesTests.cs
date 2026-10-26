namespace Shorebound.Tests;

/// <summary>Pins down the biome map: it is stable, it is regional, and it has more than one biome in it.</summary>
/// <remarks>
/// Not which biome is at which coordinate — that is the noise's business and pinning it would
/// make these tests a copy of the implementation. What is asserted is what a player would notice:
/// the map does not move, a biome is something you walk across rather than through, and the world
/// is not one biome everywhere.
/// </remarks>
public sealed class BiomesTests
{
    [Fact]
    public void the_same_seed_gives_the_same_map()
    {
        var blocks = new GameBlocks(new BlockRegistry());
        var first = new Biomes(blocks, seed: 2024);
        var second = new Biomes(blocks, seed: 2024);

        for (var z = -4000; z <= 4000; z += 311)
        for (var x = -4000; x <= 4000; x += 311)
            Assert.Equal(first.At(x, z).Name, second.At(x, z).Name);
    }

    [Fact]
    public void a_different_seed_gives_a_different_map()
    {
        var blocks = new GameBlocks(new BlockRegistry());
        var first = new Biomes(blocks, seed: 2024);
        var second = new Biomes(blocks, seed: 2025);

        var differences = 0;
        for (var z = -4000; z <= 4000; z += 311)
        for (var x = -4000; x <= 4000; x += 311)
        {
            if (first.At(x, z).Name != second.At(x, z).Name)
                differences++;
        }

        Assert.True(differences > 50, $"only {differences} columns differed between two seeds");
    }

    [Fact]
    public void the_world_has_more_than_one_biome_in_it()
    {
        var biomes = new Biomes(new GameBlocks(new BlockRegistry()), seed: 1337);

        var found = new HashSet<string>();
        for (var z = -6000; z <= 6000; z += 97)
        for (var x = -6000; x <= 6000; x += 97)
            found.Add(biomes.At(x, z).Name);

        // The thresholds have to actually divide the space. A set of them that happened to put
        // everything in one region would still produce a world, and it would look like this one
        // did before biomes existed.
        Assert.True(found.Count >= 3, $"only {string.Join(", ", found)} appear in twelve kilometres");
    }

    [Fact]
    public void a_biome_is_something_you_walk_across_rather_than_through()
    {
        var biomes = new Biomes(new GameBlocks(new BlockRegistry()), seed: 1337);

        // Walking a straight kilometre, counting how often the ground underfoot changes. A map
        // read at the terrain's own scale would change under every other step.
        var changes = 0;
        var previous = biomes.At(0, 0).Name;

        for (var x = 1; x <= 1000; x++)
        {
            var here = biomes.At(x, 0).Name;
            if (here != previous)
                changes++;

            previous = here;
        }

        Assert.True(changes <= 6, $"the biome changed {changes} times in a kilometre");
    }

    [Fact]
    public void every_biome_names_a_surface_that_can_actually_be_placed()
    {
        var blocks = new GameBlocks(new BlockRegistry());
        var biomes = new Biomes(blocks, seed: 1337);

        for (var z = -6000; z <= 6000; z += 211)
        for (var x = -6000; x <= 6000; x += 211)
        {
            var biome = biomes.At(x, z);

            Assert.True(biome.Surface.IsSolid, $"{biome.Name} has air for a surface");
            Assert.True(biome.Subsurface.IsSolid, $"{biome.Name} has air under it");
            Assert.InRange(biome.TreeChance, 0f, 1f);
            Assert.False(string.IsNullOrWhiteSpace(biome.Name));
        }
    }
}
