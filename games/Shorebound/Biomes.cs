using BrutalShore.World;

namespace Shorebound;

/// <summary>Which biome is where.</summary>
/// <remarks>
/// <para>
/// Two noise fields, warmth and damp, read at a much larger scale than the terrain itself, and a
/// handful of thresholds over them. That is the whole model: a biome is a region of a
/// two-dimensional space, and where the regions meet on the map is wherever the two fields happen
/// to cross a threshold.
/// </para>
/// <para>
/// Reading them at thirty times the terrain's scale is what makes a biome something a player
/// walks across rather than through. At the terrain's own scale the map would change under every
/// step and the world would be speckled rather than regional.
/// </para>
/// <para>
/// Like everything else a generator consults, this is a pure function of a coordinate and the
/// seed, and safe to call from any number of threads.
/// </para>
/// </remarks>
public sealed class Biomes
{
    /// <summary>How far apart, in blocks, the warmth field varies.</summary>
    private const float WarmthScale = 1400f;

    /// <summary>How far apart, in blocks, the damp field varies.</summary>
    private const float DampScale = 1100f;

    private readonly GradientNoise _warmth;
    private readonly GradientNoise _damp;
    private readonly Biome _plains;
    private readonly Biome _forest;
    private readonly Biome _desert;
    private readonly Biome _tundra;

    /// <summary>Lays out the biomes of one world.</summary>
    /// <param name="blocks">This game's block types.</param>
    /// <param name="seed">The world's seed; the same seed is the same map.</param>
    public Biomes(GameBlocks blocks, int seed)
    {
        _warmth = new GradientNoise(seed ^ unchecked((int)0x6A09E667));
        _damp = new GradientNoise(seed ^ unchecked((int)0xBB67AE85));

        _plains = new Biome
        {
            Name = "plains",
            Surface = blocks.Grass,
            Subsurface = blocks.Dirt,
            TreeChance = 0.25f,
        };

        _forest = new Biome
        {
            Name = "forest",
            Surface = blocks.Grass,
            Subsurface = blocks.Dirt,
            TreeChance = 1f,
        };

        _desert = new Biome
        {
            Name = "desert",
            Surface = blocks.Sand,
            Subsurface = blocks.Sand,
            TreeChance = 0f,
        };

        _tundra = new Biome
        {
            Name = "tundra",
            Surface = blocks.Snow,
            Subsurface = blocks.Dirt,
            TreeChance = 0.08f,
        };
    }

    /// <summary>Which biome covers a column.</summary>
    /// <param name="blockX">The column's x, in blocks.</param>
    /// <param name="blockZ">The column's z, in blocks.</param>
    /// <returns>The biome there.</returns>
    public Biome At(int blockX, int blockZ)
    {
        var warmth = _warmth.Sample(blockX / WarmthScale, blockZ / WarmthScale);
        var damp = _damp.Sample(blockX / DampScale, blockZ / DampScale);

        // Order matters: cold wins over everything, then heat without water, then water.
        if (warmth < -0.30f)
            return _tundra;

        if (warmth > 0.30f && damp < -0.05f)
            return _desert;

        return damp > 0.12f ? _forest : _plains;
    }
}
