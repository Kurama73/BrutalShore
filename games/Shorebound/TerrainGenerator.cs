using BrutalShore.Voxel;
using BrutalShore.World;

namespace Shorebound;

/// <summary>
/// Generates terrain: a height field, a surface that depends on altitude, caves, and sea level.
/// </summary>
/// <remarks>
/// <para>
/// Every decision here is made from the block's own absolute coordinate and the seed, so it obeys
/// the contract in <see cref="IChunkGenerator"/>: nothing is consulted but position.
/// </para>
/// <para>
/// The height field is two fractal fields rather than one. The first is the terrain, the second
/// varies how strongly the first is applied, which is what produces regions that are flat beside
/// regions that are mountainous instead of one uniform roughness everywhere. It is the cheapest
/// thing that makes generated terrain stop looking generated.
/// </para>
/// </remarks>
public sealed class TerrainGenerator : IChunkGenerator, IGround
{
    /// <summary>Where the water line sits, in blocks above the bottom of the world.</summary>
    public const int SeaLevel = 72;

    /// <summary>The altitude the height field varies around.</summary>
    private const int BaseHeight = 76;

    /// <summary>How far the height field reaches above and below <see cref="BaseHeight"/>.</summary>
    private const int HeightVariation = 56;

    /// <summary>Blocks per cycle of the terrain field's first octave.</summary>
    private const float TerrainScale = 280.0f;

    /// <summary>
    /// How much the terrain field is amplified before it is used as a height.
    /// </summary>
    /// <remarks>
    /// Fractal noise averaged over its octaves is a much tamer signal than its [-1, 1] range
    /// suggests: the octaves rarely peak together, so a five-octave field spends nearly all its
    /// time within about a fifth of that. Left alone it produces a world that is flat to within a
    /// few blocks everywhere. Amplifying and clipping restores the relief and costs only that the
    /// highest peaks and deepest basins are flattened, which reads as plateaus and valley floors.
    /// </remarks>
    private const float TerrainGain = 2.6f;

    /// <summary>Blocks per cycle of the field that decides how rough the terrain is.</summary>
    private const float RoughnessScale = 900.0f;

    /// <summary>Blocks per cycle of the cave field.</summary>
    private const float CaveScale = 44.0f;

    /// <summary>
    /// How close to zero both cave fields must be for rock to be hollow. Raising it widens the
    /// tunnels; past about 0.1 they merge into caverns and the ground starts to read as lace.
    /// </summary>
    private const float CaveThreshold = 0.055f;

    /// <summary>Altitude above which a surface is snow rather than grass.</summary>
    /// <summary>The height above which the ground is snow rather than grass.</summary>
    public const int SnowLine = 108;

    private readonly GradientNoise _terrain;
    private readonly GradientNoise _roughness;
    private readonly GradientNoise _caveFieldA;
    private readonly GradientNoise _caveFieldB;
    private readonly GameBlocks _blocks;
    private readonly Trees _trees;
    private readonly Biomes _biomes;

    /// <summary>Creates a generator for one world.</summary>
    /// <param name="blocks">The block types to build terrain out of.</param>
    /// <param name="seed">The world seed. The same seed gives the same world, forever.</param>
    public TerrainGenerator(GameBlocks blocks, int seed)
    {
        _blocks = blocks;
        Seed = seed;

        // Four fields from four derived seeds rather than several samples of one: two fields
        // sharing a seed are correlated, and the visible result is caves that follow ridges.
        _terrain = new GradientNoise(seed);
        _roughness = new GradientNoise(seed ^ 0x5F3759DF);
        _caveFieldA = new GradientNoise(seed ^ 0x1B873593);
        _caveFieldB = new GradientNoise(seed ^ 0x27D4EB2D);
        _trees = new Trees(blocks, seed);
        _biomes = new Biomes(blocks, seed);
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
        var roughness = _roughness.Sample(blockX / RoughnessScale, blockZ / RoughnessScale) * 0.5f + 0.5f;

        // Squared so that smooth regions dominate: most of the world is gentle and the rough parts
        // are the exception, which is the opposite of what a linear mix gives.
        roughness *= roughness;

        var shape = float.Clamp(_terrain.Fractal(blockX / TerrainScale, blockZ / TerrainScale, octaves: 5) * TerrainGain, -1f, 1f);

        // The floor of 0.3 is what keeps the flat regions from being perfectly flat: a plain with
        // no relief at all looks like a bug rather than a plain.
        return BaseHeight + (int)(shape * HeightVariation * (0.3f + 0.7f * roughness));
    }

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
        if (origin.Y > BaseHeight + HeightVariation && origin.Y > SeaLevel)
            return;

        var blocks = into.BeginBulkWrite();

        for (var localZ = 0; localZ < Chunk.Size; localZ++)
        {
            for (var localX = 0; localX < Chunk.Size; localX++)
            {
                var worldX = origin.X + localX;
                var worldZ = origin.Z + localZ;
                var surface = SurfaceHeight(worldX, worldZ);
                var biome = _biomes.At(worldX, worldZ);

                for (var localY = 0; localY < Chunk.Size; localY++)
                {
                    var worldY = origin.Y + localY;
                    blocks[Chunk.IndexOf(localX, localY, localZ)] = BlockFor(worldX, worldY, worldZ, surface, biome);
                }
            }
        }

        _trees.Grow(chunkCoordinate, blocks, this);
        into.EndBulkWrite();
    }

    /// <summary>What belongs at one absolute block coordinate, given its column's surface.</summary>
    private BlockId BlockFor(int worldX, int worldY, int worldZ, int surface, in Biome biome)
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
            return _blocks.Stone;

        // A shore is a shore and a mountain top is snow whatever the biome says: both are facts
        // about the height, and a desert that ran into the sea without a beach, or a tundra whose
        // peaks were bare, would read as a mistake rather than as a boundary.
        if (surface <= SeaLevel + 1)
            return _blocks.Sand;

        if (surface >= SnowLine)
            return depth > 0 ? _blocks.Dirt : _blocks.Snow;

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
