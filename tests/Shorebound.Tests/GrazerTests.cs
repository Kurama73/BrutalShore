namespace Shorebound.Tests;

/// <summary>
/// What an animal does: it appears on ground it can stand on, it wanders, it keeps away from the
/// player, it does not walk off a ledge, and it is forgotten once nobody is near it.
/// </summary>
/// <remarks>
/// The refusals matter more than the wandering. An animal that could appear inside rock, or in
/// the sea, or walk off a cliff into the water would each be visible within a minute of play and
/// would each make the world look broken rather than inhabited.
/// </remarks>
public sealed class GrazerTests
{
    private const double Step = 1.0 / 60.0;

    private readonly BlockRegistry _registry = new();
    private readonly GameBlocks _blocks;
    private readonly VoxelWorld _world;

    /// <summary>Builds a flat world of grass at y = 40, loaded around the origin.</summary>
    public GrazerTests()
    {
        _blocks = new GameBlocks(_registry);
        _world = new VoxelWorld(_registry, new FlatGenerator(_blocks.Grass, 40));

        for (var z = -2; z <= 2; z++)
        for (var x = -2; x <= 2; x++)
        for (var y = 0; y < VoxelWorld.ChunksTall; y++)
            _world.Load(new int3(x, y, z));
    }

    [Fact]
    public void an_animal_appears_standing_on_the_ground_rather_than_in_it()
    {
        var grazers = new Grazers(seed: 3);

        Run(grazers, seconds: 10.0, playerAt: Standing);

        Assert.True(grazers.Count > 0, "nothing appeared in ten seconds of flat grass");
        Assert.All(grazers.All, grazer => Assert.Equal(41.0, grazer.FeetMeters.Y, 0.05));
    }

    [Fact]
    public void no_more_than_a_field_full_ever_appear()
    {
        var grazers = new Grazers(seed: 3);

        Run(grazers, seconds: 120.0, playerAt: Standing);

        Assert.InRange(grazers.Count, 1, 10);
    }

    [Fact]
    public void an_animal_never_appears_on_top_of_the_player()
    {
        var grazers = new Grazers(seed: 11);

        Run(grazers, seconds: 30.0, playerAt: Standing);

        Assert.All(
            grazers.All,
            grazer =>
                Assert.True(
                    Away(grazer.FeetMeters, Standing) > 8.0,
                    $"one appeared {Away(grazer.FeetMeters, Standing):F1} m from the player"
                )
        );
    }

    [Fact]
    public void an_animal_walks_away_from_a_player_standing_next_to_it()
    {
        var grazers = new Grazers(seed: 5);
        Run(grazers, seconds: 10.0, playerAt: Standing);
        Assert.True(grazers.Count > 0);

        // Stand right next to the first one and watch which way it goes.
        var startled = grazers.All[0];
        var beside = startled.FeetMeters + new double3(1.2, 0.0, 0.0);
        var before = Away(startled.FeetMeters, beside);

        Run(grazers, seconds: 2.0, playerAt: beside);

        var after = Away(grazers.All[0].FeetMeters, beside);
        Assert.True(after > before, $"it stayed put: {before:F2} m became {after:F2} m");
    }

    [Fact]
    public void animals_are_forgotten_once_the_player_is_far_away()
    {
        var grazers = new Grazers(seed: 3);
        Run(grazers, seconds: 20.0, playerAt: Standing);
        Assert.True(grazers.Count > 0);

        Run(grazers, seconds: 1.0, playerAt: new double3(100_000.0, 41.0, 100_000.0));

        Assert.Equal(0, grazers.Count);
    }

    [Fact]
    public void an_animal_stays_on_the_ground_it_was_put_on()
    {
        // Nothing below y = 40 is solid, so an animal that left the ground would fall for ever
        // and the height check would catch it.
        var grazers = new Grazers(seed: 9);

        Run(grazers, seconds: 60.0, playerAt: Standing);

        Assert.All(grazers.All, grazer => Assert.Equal(41.0, grazer.FeetMeters.Y, 0.05));
    }

    [Fact]
    public void an_animal_that_is_walking_actually_covers_ground()
    {
        var grazers = new Grazers(seed: 2);
        Run(grazers, seconds: 10.0, playerAt: Standing);
        Assert.True(grazers.Count > 0);

        var from = grazers.All[0].FeetMeters;
        Run(grazers, seconds: 20.0, playerAt: Standing);

        Assert.True(Away(from, grazers.All[0].FeetMeters) > 1.0, "it never went anywhere");
    }

    [Fact]
    public void an_animal_is_six_boxes_standing_on_the_ground_it_is_on()
    {
        var grazer = new Grazer { FeetMeters = new double3(3.0, 41.0, -2.0), Yaw = 0.7f };
        Span<GrazerShape.Part> parts = stackalloc GrazerShape.Part[GrazerShape.PartCount];

        GrazerShape.Lay(grazer, parts);

        // Nothing below the feet and nothing above a metre and a quarter: the shape has to fit
        // the box the physics moves, or an animal looks like it is wading through the ground.
        foreach (var part in parts)
        {
            Assert.True(part.MiddleMeters.Y - (part.SizeMeters.Y / 2.0) >= 41.0 - 0.01, "a part is under the ground");
            Assert.True(part.MiddleMeters.Y + (part.SizeMeters.Y / 2.0) <= 41.0 + 1.3, "a part is above the animal");
        }
    }

    [Fact]
    public void a_shape_given_too_few_boxes_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            Span<GrazerShape.Part> parts = new GrazerShape.Part[GrazerShape.PartCount - 1];
            GrazerShape.Lay(new Grazer(), parts);
        });
    }

    private static double3 Standing => new(0.5, 41.0, 0.5);

    private void Run(Grazers grazers, double seconds, double3 playerAt)
    {
        for (var tick = 0; tick < seconds / Step; tick++)
            grazers.Step(_world, _registry, Step, playerAt);
    }

    private static double Away(double3 from, double3 to)
    {
        var between = to - from;
        return double.Sqrt(double3.Dot(between, between));
    }

    /// <summary>A world that is one solid layer at a height, and air everywhere else.</summary>
    private sealed class FlatGenerator(BlockId block, int level) : IChunkGenerator
    {
        public void Generate(int3 chunkCoordinate, Chunk into)
        {
            var bottom = chunkCoordinate.Y * Chunk.Size;
            if (level < bottom || level >= bottom + Chunk.Size)
                return;

            var blocks = into.BeginBulkWrite();
            for (var z = 0; z < Chunk.Size; z++)
            for (var x = 0; x < Chunk.Size; x++)
                blocks[Chunk.IndexOf(x, level - bottom, z)] = block;

            into.EndBulkWrite();
        }
    }
}
