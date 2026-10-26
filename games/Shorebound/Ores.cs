using BrutalShore.Voxel;
using BrutalShore.World;

namespace Shorebound;

/// <summary>Where the metal is: which blocks of stone are a seam of something instead.</summary>
/// <remarks>
/// <para>
/// Separate from <see cref="TerrainGenerator" /> because it answers a different question. The
/// terrain decides where rock is; this decides what that rock is made of, and it does so from the
/// block's own coordinate alone, so it keeps the generator's contract of never consulting a
/// neighbour.
/// </para>
/// <para>
/// A seam is a region where one 3D field runs high, not a scattering of lucky blocks. That is the
/// difference between a world worth excavating and a world worth strip-mining: a field makes a
/// connected body of ore that can be followed, so finding one edge of it tells the player where to
/// dig next. Random blocks tell them nothing, and the only strategy against them is to remove
/// everything.
/// </para>
/// <para>
/// Depth is what separates the four. Each has a band it is found in and fades out at both ends of
/// it, so going deeper is how the materials change rather than how much there is of them — which
/// is what makes a shaft a decision rather than a chore.
/// </para>
/// </remarks>
public sealed class Ores
{
    /// <summary>
    /// How far either side of a band's edge the chance of finding it fades to nothing, in blocks.
    /// </summary>
    private const float Fade = 14f;

    private readonly Seam[] _seams;

    /// <summary>Builds the seams for one world.</summary>
    /// <param name="blocks">The game's registered block types.</param>
    /// <param name="seed">The world seed. The same seed puts the same metal in the same place.</param>
    public Ores(GameBlocks blocks, int seed)
    {
        // The depths are stated against the floor of the world rather than against the surface:
        // a seam that followed the terrain up a mountain would mean the shortest way to iron is
        // to climb, which is the opposite of what the material is supposed to say.
        // Two numbers decide what a seam is like to mine, and they are not interchangeable. The
        // threshold sets how much of the band is ore; the scale sets whether that ore is in a few
        // large bodies or many small ones. Raising the threshold alone makes a seam rarer *and*
        // makes each one smaller, until the whole ore is a handful of single blocks nobody ever
        // walks into. Rarity therefore comes from the threshold and findability from the scale.
        //
        // The thresholds are read off the field's own distribution rather than guessed: sampled
        // over a hundred thousand blocks, a two-octave fractal here reaches 0.37 at its 95th
        // percentile, 0.51 at its 99th and 0.56 at its 99.5th. Together these come to roughly one
        // block of rock in forty across the whole depth of the world, which a test pins down.
        _seams =
        [
            // Coal first, so it is the one a player meets on the way down, and the one that is
            // still worth something once they have gone past it: everything burns it. Its scale
            // is the largest, so a coal seam is the one that is worth setting up next to.
            new Seam(blocks.CoalOre, Field(seed, 0x3C6EF372), Lowest: 6, Highest: 72, Scale: 34f, Threshold: 0.50f),
            new Seam(blocks.CopperOre, Field(seed, 0x6A09E667), Lowest: 4, Highest: 60, Scale: 29f, Threshold: 0.545f),
            new Seam(blocks.IronOre, Field(seed, unchecked((int)0xBB67AE85)), Lowest: 2, Highest: 44, Scale: 25f, Threshold: 0.565f),

            // Deep, rare and lit. The band stops well short of where a cave system usually
            // reaches, so the first one is nearly always found by digging rather than by walking.
            // Its scale is the smallest: many small pockets rather than one body somewhere, which
            // is the difference between an ore a player can go and look for and one they have to
            // be lucky about.
            new Seam(blocks.EmberOre, Field(seed, 0x510E527F), Lowest: 2, Highest: 22, Scale: 15f, Threshold: 0.56f),
        ];
    }

    /// <summary>What a block of stone is actually made of at a coordinate.</summary>
    /// <param name="blockX">Its x, in blocks.</param>
    /// <param name="blockY">Its y, in blocks above the bottom of the world.</param>
    /// <param name="blockZ">Its z, in blocks.</param>
    /// <param name="stone">What to give back when there is no seam here.</param>
    /// <returns>The ore at that coordinate, or <paramref name="stone" />.</returns>
    /// <remarks>
    /// The seams are tested in order and the first one that claims the block wins, so a coal field
    /// and an iron field overlapping produce a boundary rather than a blend. Which of the two gets
    /// the block does not matter; that the answer is stable does, because the block is generated
    /// again every time its chunk is streamed in.
    /// </remarks>
    public BlockId At(int blockX, int blockY, int blockZ, BlockId stone)
    {
        foreach (var seam in _seams)
        {
            var reach = Depth(blockY, seam);
            if (reach <= 0f)
                continue;

            var field = seam.Field.Fractal(
                blockX / seam.Scale,
                blockY / seam.Scale,
                blockZ / seam.Scale,
                octaves: 2
            );

            // The threshold is raised as the band fades rather than the field being scaled, so a
            // seam shrinks towards its middle at the edges of its band instead of thinning out
            // everywhere at once. A seam that thinned would be a seam not worth mining.
            if (field > seam.Threshold + ((1f - reach) * (1f - seam.Threshold)))
                return seam.Block;
        }

        return stone;
    }

    /// <summary>How fully a seam is present at an altitude, from 0 outside its band to 1 inside.</summary>
    private static float Depth(int blockY, in Seam seam)
    {
        if (blockY < seam.Lowest - Fade || blockY > seam.Highest + Fade)
            return 0f;

        var below = (blockY - (seam.Lowest - Fade)) / Fade;
        var above = ((seam.Highest + Fade) - blockY) / Fade;
        return float.Clamp(float.Min(below, above), 0f, 1f);
    }

    /// <summary>
    /// A field of its own for one seam, so that two ores are never found in the same place.
    /// </summary>
    /// <remarks>
    /// Derived seeds rather than several samples of one field: two fields sharing a seed are
    /// correlated, and the visible result is copper that is always next to iron.
    /// </remarks>
    private static GradientNoise Field(int seed, int salt) => new(seed ^ salt);

    /// <summary>One kind of ore: what it is, where it is, how big it is and how much of it.</summary>
    private readonly record struct Seam(
        BlockId Block,
        GradientNoise Field,
        int Lowest,
        int Highest,
        float Scale,
        float Threshold
    );
}
