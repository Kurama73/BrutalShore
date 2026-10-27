using BrutalShore.Voxel;
using BrutalShore.World;

namespace Shorebound.Domain.World;

/// <summary>
/// Generates terrain: a height field, a surface that depends on altitude, caves, ore and sea level.
/// </summary>
/// <remarks>
/// <para>
/// Every decision here is made from the block's own absolute coordinate and the seed, so it obeys
/// the contract in <see cref="IChunkGenerator"/>: nothing is consulted but position.
/// </para>
/// <para>
/// The height is three questions rather than one field, because one field can only answer one.
/// <em>Where is there land</em> is a very slow field that decides sea floor, shelf, coast and
/// inland. <em>How worn down is it</em> is a slower one still that decides whether a region is a
/// plain or a range — it sets the amount of relief, not its shape. <em>What shape</em> is a ridged
/// field, which gives mountains a skyline instead of making them larger hills. Multiplying the
/// second into the third is what produces flat country beside mountainous country rather than one
/// uniform crumple everywhere.
/// </para>
/// <para>
/// The two shape fields are sampled through a <see cref="Warp"/>. Gradient noise is built on a
/// square lattice and its contours close into rings around every extremum; terrain made straight
/// out of it reads as a contour map, which is exactly what this used to look like. Bending the
/// plane first costs two samples and is the largest single difference between terrain that looks
/// generated and terrain that does not.
/// </para>
/// </remarks>
public sealed class TerrainGenerator : IChunkGenerator, IGround
{
    /// <summary>Where the water line sits, in blocks above the bottom of the world.</summary>
    public const int SeaLevel = 72;

    /// <summary>The highest a surface can be, which is what lets empty sky be skipped.</summary>
    /// <remarks>
    /// An upper bound rather than a typical value, and it has to be a true one: a chunk above it
    /// is never generated at all, so a peak that could exceed it would simply be cut off flat.
    /// </remarks>
    public const int HighestSurface = CoastHeight + InlandRise + MountainRelief + DetailRelief;

    /// <summary>Altitude above which a surface is snow rather than grass.</summary>
    public const int SnowLine = 108;

    /// <summary>The altitude of the deep sea floor, in blocks.</summary>
    private const int DeepFloor = 44;

    /// <summary>The altitude of land just above the waterline.</summary>
    private const int CoastHeight = 74;

    /// <summary>How far above the coast the inland plateau sits before any relief is added.</summary>
    private const int InlandRise = 16;

    /// <summary>How much relief a range has, in blocks, where nothing has worn it down.</summary>
    /// <remarks>
    /// Generous on purpose. The whole pitch of this game is that you dig down and it gets worse,
    /// which asks for a surface that is somewhere to dig down <em>from</em> — and the version of
    /// this generator that had ten blocks of relief across a kilometre had nowhere.
    /// </remarks>
    private const int MountainRelief = 72;

    /// <summary>How much relief the finest field adds, in blocks, everywhere.</summary>
    /// <remarks>
    /// Small, and its real job is not shape but scale: without a field finer than a hundred
    /// blocks, the height changes so slowly that rounding it to whole blocks draws the terrain as
    /// a contour map of wide flat bands.
    /// </remarks>
    private const int DetailRelief = 8;

    /// <summary>Blocks per cycle of the field that decides where there is land at all.</summary>
    private const float ContinentScale = 1500.0f;

    /// <summary>How much the continent field is amplified before it is read as a coastline.</summary>
    /// <remarks>
    /// Fractal noise averaged over its octaves is a far tamer signal than its range suggests: the
    /// octaves rarely peak together, so most of its time is spent near the middle. Left alone it
    /// gives a world that is all shelf and no sea and no interior.
    /// </remarks>
    private const float ContinentGain = 2.4f;

    /// <summary>Blocks per cycle of the field that decides how worn down a region is.</summary>
    private const float ErosionScale = 760.0f;

    /// <summary>Blocks per cycle of the ridged field that shapes the high ground.</summary>
    /// <remarks>
    /// Chosen against the relief rather than for its own sake. Relief over half a cycle is the
    /// slope a mountainside ends up with, and a slope below about one block per block is not a
    /// mountainside at all: rounding it to whole blocks draws it as a contour map of wide flat
    /// terraces, which is exactly what three hundred blocks of ridge gave.
    /// </remarks>
    private const float RidgeScale = 170.0f;

    /// <summary>Blocks per cycle of the finest field.</summary>
    private const float DetailScale = 44.0f;

    /// <summary>Blocks per cycle of the bends the plane is sampled through.</summary>
    private const float WarpScale = 260.0f;

    /// <summary>How far the warp may move a sample, in blocks.</summary>
    private const float WarpStrength = 55.0f;

    /// <summary>How big a step between neighbouring columns counts as a cliff face.</summary>
    /// <remarks>
    /// Three blocks: a player can climb one and jump two, so three is the first step that is a
    /// wall rather than a slope, which makes it the right place to stop drawing soil on it.
    /// </remarks>
    private const int BareRockStep = 3;

    /// <summary>Blocks per cycle of the cave field.</summary>
    private const float CaveScale = 44.0f;

    /// <summary>
    /// How close to zero both cave fields must be for rock to be hollow. Raising it widens the
    /// tunnels; past about 0.1 they merge into caverns and the ground starts to read as lace.
    /// </summary>
    private const float CaveThreshold = 0.055f;

    private readonly GradientNoise _continent;
    private readonly GradientNoise _erosion;
    private readonly GradientNoise _ridges;
    private readonly GradientNoise _detail;
    private readonly Warp _warp;
    private readonly GradientNoise _caveFieldA;
    private readonly GradientNoise _caveFieldB;
    private readonly GameBlocks _blocks;
    private readonly Trees _trees;
    private readonly Biomes _biomes;
    private readonly Ores _ores;

    /// <summary>Creates a generator for one world.</summary>
    /// <param name="blocks">The block types to build terrain out of.</param>
    /// <param name="seed">The world seed. The same seed gives the same world, forever.</param>
    public TerrainGenerator(GameBlocks blocks, int seed)
    {
        _blocks = blocks;
        Seed = seed;

        // A field per question, each from its own derived seed rather than from samples of one:
        // two fields sharing a seed are correlated, and the visible result is caves that follow
        // ridges and coastlines that follow the erosion.
        _continent = new GradientNoise(seed);
        _erosion = new GradientNoise(seed ^ 0x5F3759DF);
        _ridges = new GradientNoise(seed ^ 0x45D9F3B3);
        _detail = new GradientNoise(seed ^ 0x119DE1F3);
        _warp = new Warp(seed, WarpScale, WarpStrength);
        _caveFieldA = new GradientNoise(seed ^ 0x1B873593);
        _caveFieldB = new GradientNoise(seed ^ 0x27D4EB2D);
        _trees = new Trees(blocks, seed);
        _biomes = new Biomes(blocks, seed);
        _ores = new Ores(blocks, seed);
    }

    /// <summary>The seed this world was generated from.</summary>
    public int Seed { get; }

    /// <summary>
    /// The altitude of the solid surface at a horizontal position, in blocks.
    /// </summary>
    /// <remarks>
    /// Public because it is what everything else wants to know about terrain without generating a
    /// chunk: where to put the player at the start, how high to let a camera fly, whether a chunk
    /// can be skipped entirely.
    /// </remarks>
    public int SurfaceHeight(int blockX, int blockZ)
    {
        var (warpedX, warpedZ) = _warp.Of(blockX, blockZ);

        // The coastline is warped too, and it matters more here than anywhere: an unwarped
        // continent field draws a shore that is either a straight edge or an arc, because that is
        // what the contours of smooth noise are. Bending it is what puts bays and headlands in.
        var continent = float.Clamp(
            _continent.Fractal(warpedX / ContinentScale, warpedZ / ContinentScale, octaves: 5) * ContinentGain,
            -1f,
            1f
        );

        // How far inland this is, from 0 at the deep sea to 1 well past the coast. Everything
        // else is scaled by it, which is what keeps the sea floor smooth and the ranges on land.
        var land = Ramp(continent, -0.15f, 0.45f);

        // Worn down against raised up. Cubed rather than squared because most of a world should
        // be somewhere a player can build: the cube puts four fifths of it under half relief and
        // leaves the ranges as the exception they are supposed to be.
        var raised = Ramp(_erosion.Fractal(warpedX / ErosionScale, warpedZ / ErosionScale, octaves: 3), -0.35f, 0.5f);
        raised *= raised * raised;

        // Ridged rather than fractal: a sum of hills has no skyline, and a mountain that is only
        // a larger hill is not worth climbing or tunnelling into.
        // Stretched about its middle before it is used. A ridged field is already creased, but a
        // crease in a field that only climbs seven tenths of a block per block is still a slope a
        // player walks up — and whole-block rounding draws that as a staircase of wide terraces.
        // Pulling the middle half of its range across the whole of it roughly doubles the slope
        // where the mountainside is, and flattens what is left into valley floor and summit.
        var ridge = Ramp(_ridges.Ridged(warpedX / RidgeScale, warpedZ / RidgeScale, octaves: 5), 0.22f, 0.72f);
        var detail = _detail.Fractal(warpedX / DetailScale, warpedZ / DetailScale, octaves: 3);

        var height =
            Mix(DeepFloor, CoastHeight, Ramp(continent, -0.8f, -0.15f))
            + (land * InlandRise)
            + (land * raised * MountainRelief * ridge)
            + (land * detail * DetailRelief);

        return int.Clamp((int)height, 1, HighestSurface);
    }

    /// <summary>Where a value sits between two thresholds, smoothly, from 0 to 1.</summary>
    /// <param name="value">The value.</param>
    /// <param name="from">Where the ramp starts.</param>
    /// <param name="to">Where it finishes.</param>
    /// <returns>0 at or below <paramref name="from" />, 1 at or above <paramref name="to" />.</returns>
    /// <remarks>
    /// Smooth rather than linear because these are multiplied into the height: a linear ramp has
    /// a corner at each end, and a corner in a multiplier is a crease across the landscape in the
    /// place where one kind of country becomes another.
    /// </remarks>
    private static float Ramp(float value, float from, float to)
    {
        var along = float.Clamp((value - from) / (to - from), 0f, 1f);
        return along * along * (3f - (2f * along));
    }

    private static float Mix(float from, float to, float along) => from + ((to - from) * along);

    /// <inheritdoc />
    /// <inheritdoc />
    public float TreeChance(int blockX, int blockZ) => _biomes.At(blockX, blockZ).TreeChance;

    /// <summary>Which biome covers a column; shown in the overlay.</summary>
    /// <param name="blockX">The column's x, in blocks.</param>
    /// <param name="blockZ">The column's z, in blocks.</param>
    /// <returns>The biome there.</returns>
    public Biome BiomeAt(int blockX, int blockZ) => _biomes.At(blockX, blockZ);

    /// <inheritdoc />
    public void Generate(int3 chunkCoordinate, Chunk into)
    {
        var origin = WorldCoordinates.ChunkOrigin(chunkCoordinate);

        // A chunk entirely above the highest possible surface and the water line is sky, and the
        // cheapest thing to do with it is nothing. This is what makes a tall world affordable.
        if (origin.Y > HighestSurface && origin.Y > SeaLevel)
            return;

        // Every column's height once, with a one-block skirt around the chunk. The skirt is what
        // makes steepness free: a column can be asked how far it stands above its neighbours
        // without computing their heights a second time, and a height is fifteen noise samples.
        const int Stride = Chunk.Size + 2;
        Span<int> heights = stackalloc int[Stride * Stride];

        for (var skirtZ = -1; skirtZ <= Chunk.Size; skirtZ++)
        for (var skirtX = -1; skirtX <= Chunk.Size; skirtX++)
            heights[((skirtZ + 1) * Stride) + skirtX + 1] = SurfaceHeight(origin.X + skirtX, origin.Z + skirtZ);

        var blocks = into.BeginBulkWrite();

        for (var localZ = 0; localZ < Chunk.Size; localZ++)
        {
            for (var localX = 0; localX < Chunk.Size; localX++)
            {
                var worldX = origin.X + localX;
                var worldZ = origin.Z + localZ;
                var at = ((localZ + 1) * Stride) + localX + 1;
                var surface = heights[at];
                var steepness = Steepness(heights, at, Stride);
                var biome = _biomes.At(worldX, worldZ);

                for (var localY = 0; localY < Chunk.Size; localY++)
                {
                    var worldY = origin.Y + localY;
                    blocks[Chunk.IndexOf(localX, localY, localZ)] = BlockFor(
                        worldX,
                        worldY,
                        worldZ,
                        surface,
                        steepness,
                        biome
                    );
                }
            }
        }

        _trees.Grow(chunkCoordinate, blocks, this);
        into.EndBulkWrite();
    }

    /// <summary>How far a column stands above or below the columns beside it, in blocks.</summary>
    /// <param name="heights">The chunk's height field, with its skirt.</param>
    /// <param name="at">The column's index into it.</param>
    /// <param name="stride">How wide the height field is.</param>
    /// <returns>The largest step to any of the four neighbours.</returns>
    private static int Steepness(ReadOnlySpan<int> heights, int at, int stride)
    {
        var here = heights[at];

        return int.Max(
            int.Max(int.Abs(here - heights[at - 1]), int.Abs(here - heights[at + 1])),
            int.Max(int.Abs(here - heights[at - stride]), int.Abs(here - heights[at + stride]))
        );
    }

    /// <summary>What belongs at one absolute block coordinate, given its column's surface.</summary>
    private BlockId BlockFor(int worldX, int worldY, int worldZ, int surface, int steepness, in Biome biome)
    {
        if (worldY < 0 || worldY >= VoxelWorld.HeightBlocks)
            return BlockId.Air;

        // The bottom two layers are bedrock, so there is a floor rather than a hole into nothing.
        if (worldY < 2)
            return _blocks.Bedrock;

        if (worldY > surface)
            return worldY <= SeaLevel ? _blocks.Water : BlockId.Air;

        // Caves are carved out of rock only, so they never open a hole in the sea floor or eat the
        // surface a player is standing on.
        var depth = surface - worldY;
        if (depth > 3 && IsCave(worldX, worldY, worldZ))
            return BlockId.Air;

        if (depth > 3)
            return _ores.At(worldX, worldY, worldZ, _blocks.Stone);

        // A shore is a shore and a mountain top is snow whatever the biome says: both are facts
        // about the height, and a desert that ran into the sea without a beach, or a tundra whose
        // peaks were bare, would read as a mistake rather than as a boundary.
        if (surface <= SeaLevel + 1)
            return _blocks.Sand;

        // Snow on top and rock underneath, not soil. Every step of a snowfield shows its own side
        // to the camera, and a mountain whose every ledge is a brown stripe reads as a ploughed
        // field seen from a long way off.
        if (surface >= SnowLine)
            return depth > 0 ? _blocks.Stone : _blocks.Snow;

        // Nothing holds onto a cliff. Where the ground climbs three blocks between one column and
        // the next there is no soil on it, whatever the biome grows on the flat — which is also
        // the only thing that tells a player, from a distance, where the rock they want is
        // already exposed.
        if (steepness >= BareRockStep)
            return _blocks.Stone;

        return depth > 0 ? biome.Subsurface : biome.Surface;
    }

    /// <summary>Whether a point in rock is hollowed out.</summary>
    /// <remarks>
    /// <para>
    /// Two fields, both required to be near zero. The reason is geometric: the set of points where
    /// one 3D field is zero is a surface, so carving a band around it would cut sheets through the
    /// rock. Where <em>two</em> independent fields are both zero is the intersection of two
    /// surfaces, which is a curve — and a band around a curve is a tunnel. That is what makes these
    /// caves something to walk along rather than a crack to fall into.
    /// </para>
    /// <para>
    /// The vertical axis is sampled at half frequency, so tunnels run wider than they are tall
    /// instead of being as likely to be vertical shafts as horizontal passages.
    /// </para>
    /// </remarks>
    private bool IsCave(int worldX, int worldY, int worldZ)
    {
        var x = worldX / CaveScale;
        var y = worldY / (CaveScale * 2.0f);
        var z = worldZ / CaveScale;

        // The first field decides most of the volume, so it is tested first and the second is only
        // sampled for the small fraction of points that passed.
        if (float.Abs(_caveFieldA.Fractal(x, y, z, octaves: 2)) > CaveThreshold)
            return false;

        return float.Abs(_caveFieldB.Fractal(x, y, z, octaves: 2)) <= CaveThreshold;
    }
}
