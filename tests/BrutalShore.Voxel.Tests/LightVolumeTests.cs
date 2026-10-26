using BrutalShore.Voxel;

namespace BrutalShore.Voxel.Tests;

/// <summary>
/// What the flood fill promises: fifteen levels of reach, exactly one lost per block of air, a
/// roof that stops light, and a hole in a roof that does not.
/// </summary>
/// <remarks>
/// The claims are about the shape of the answer, not about a number taken from a screenshot. Each
/// one is a scene a player would recognise — standing in a doorway, at the back of a cave, at the
/// bottom of a shaft, under water — because those are the cases a rewrite of the fill has to keep
/// getting right.
/// </remarks>
public sealed class LightVolumeTests
{
    private const int RoofY = 20;

    // The box the volume lights reaches sixteen blocks outside the chunk; a roof has to cover all
    // of it, or the fill finds daylight coming in from under its edge.
    private const int Reach = 48;

    private readonly BlockRegistry _registry = new();
    private readonly BlockId _rock;
    private readonly BlockId _water;
    private readonly BlockId _lamp;

    /// <summary>Registers the three kinds of block these claims need.</summary>
    public LightVolumeTests()
    {
        _rock = _registry.Register(new BlockType { Name = "rock", Color = new byte3(120, 120, 120) });

        _water = _registry.Register(
            new BlockType
            {
                Name = "water",
                Color = new byte3(60, 110, 170),
                Opacity = 170,
                LightOpacity = 2,
            }
        );

        _lamp = _registry.Register(
            new BlockType { Name = "lamp", Color = new byte3(255, 230, 170), Emission = 14 }
        );
    }

    [Fact]
    public void a_cell_with_nothing_above_it_is_as_bright_as_the_sky_gets()
    {
        var light = Illuminate(new LitWorld());

        Assert.Equal(LightVolume.MaxLevel, light.SkyLightAt(0, 0, 0));
        Assert.Equal(LightVolume.MaxLevel, light.SkyLightAt(16, 31, 16));
    }

    [Fact]
    public void a_cell_under_a_roof_loses_one_level_for_every_block_from_its_edge()
    {
        // A roof over everything from x = 0 outwards, so x = -1 is the doorway.
        var light = Illuminate(Roofed(fromX: 0));

        Assert.Equal(LightVolume.MaxLevel, light.SkyLightAt(-1, RoofY - 1, 0));
        Assert.Equal(14, light.SkyLightAt(0, RoofY - 1, 0));
        Assert.Equal(13, light.SkyLightAt(1, RoofY - 1, 0));
        Assert.Equal(9, light.SkyLightAt(5, RoofY - 1, 0));
    }

    [Fact]
    public void nothing_more_than_fifteen_blocks_under_a_roof_is_lit_at_all()
    {
        var light = Illuminate(Roofed(fromX: 0));

        // Fifteen levels from the open cell at x = -1, so x = 13 is the last one with anything
        // left. That is the reach the box's own margin is sized from.
        Assert.Equal(1, light.SkyLightAt(13, RoofY - 1, 0));
        Assert.Equal(0, light.SkyLightAt(14, RoofY - 1, 0));
        Assert.Equal(0, light.SkyLightAt(20, RoofY - 1, 0));
    }

    [Fact]
    public void a_hole_in_a_roof_carries_daylight_to_the_floor_below_it()
    {
        var world = Roofed(fromX: -Reach).WithBox(new int3(-Reach, 0, -Reach), new int3(Reach, 0, Reach), _rock);
        world.Without(10, RoofY, 10);

        var light = Illuminate(world);

        // Straight down the shaft nothing is lost: a column open to the sky is open to it all the
        // way to the ground, however far that is.
        Assert.Equal(LightVolume.MaxLevel, light.SkyLightAt(10, RoofY, 10));
        Assert.Equal(LightVolume.MaxLevel, light.SkyLightAt(10, 1, 10));

        // And sideways from the foot of the shaft it fades as it would anywhere else.
        Assert.Equal(LightVolume.MaxLevel - 6, light.SkyLightAt(16, 1, 10));
    }

    [Fact]
    public void a_roof_with_no_hole_in_it_leaves_the_floor_black()
    {
        var world = Roofed(fromX: -Reach).WithBox(new int3(-Reach, 0, -Reach), new int3(Reach, 0, Reach), _rock);

        var light = Illuminate(world);

        Assert.Equal(0, light.SkyLightAt(10, 1, 10));
        Assert.Equal(0, light.SkyLightAt(0, RoofY - 1, 0));
    }

    [Fact]
    public void a_glowing_block_is_at_its_own_level_and_its_neighbour_one_below()
    {
        var light = Illuminate(new LitWorld().With(4, 4, 4, _lamp));

        Assert.Equal(14, light.BlockLightAt(4, 4, 4));
        Assert.Equal(13, light.BlockLightAt(5, 4, 4));
        Assert.Equal(12, light.BlockLightAt(6, 4, 4));

        // Fourteen at the block itself leaves thirteen blocks of open air around it.
        Assert.Equal(1, light.BlockLightAt(4 + 13, 4, 4));
        Assert.Equal(0, light.BlockLightAt(4 + 14, 4, 4));
    }

    [Fact]
    public void a_glowing_block_lights_nothing_through_a_wall()
    {
        var world = new LitWorld()
            .With(4, 4, 4, _lamp)
            .WithBox(new int3(5, -Reach, -Reach), new int3(5, Reach, Reach), _rock);

        var light = Illuminate(world);

        Assert.Equal(0, light.BlockLightAt(6, 4, 4));
        Assert.Equal(0, light.BlockLightAt(10, 4, 4));
    }

    [Fact]
    public void the_sky_and_the_glow_are_counted_separately()
    {
        var light = Illuminate(new LitWorld().With(4, 4, 4, _lamp));

        // The lamp is in the open, so its cell is both fully daylit and fully glowing. Keeping
        // the two apart is what lets a shader treat them differently — one goes out at night.
        Assert.Equal(LightVolume.MaxLevel, light.SkyLightAt(5, 4, 4));
        Assert.Equal(13, light.BlockLightAt(5, 4, 4));

        // And a cell out of the lamp's reach is daylit and nothing else.
        Assert.Equal(LightVolume.MaxLevel, light.SkyLightAt(24, 24, 24));
        Assert.Equal(0, light.BlockLightAt(24, 24, 24));
    }

    [Fact]
    public void a_block_that_dims_light_costs_its_own_share_of_a_level()
    {
        // Water to the horizon, surface at y = 19, so nothing reaches any of it sideways.
        var world = new LitWorld().WithBox(new int3(-Reach, -Reach, -Reach), new int3(Reach, 19, Reach), _water);

        var light = Illuminate(world);

        Assert.Equal(LightVolume.MaxLevel, light.SkyLightAt(0, 20, 0));
        Assert.Equal(13, light.SkyLightAt(0, 19, 0));
        Assert.Equal(11, light.SkyLightAt(0, 18, 0));

        // Two levels a block, so seven blocks of water is as far as the sky gets.
        Assert.Equal(1, light.SkyLightAt(0, 13, 0));
        Assert.Equal(0, light.SkyLightAt(0, 12, 0));
    }

    [Fact]
    public void lighting_a_second_chunk_leaves_nothing_of_the_first_behind()
    {
        var volume = new LightVolume();
        var roofed = Roofed(fromX: -Reach).WithBox(new int3(-Reach, 0, -Reach), new int3(Reach, 0, Reach), _rock);

        volume.Illuminate(int3.Zero, new LitWorld(), _registry, new LitWorld());
        volume.Illuminate(int3.Zero, roofed, _registry, roofed);

        // The buffers are reused between chunks, which is the point of them; a level left over
        // from the open world would light this cave from nowhere.
        Assert.Equal(0, volume.SkyLightAt(10, 1, 10));
    }

    [Fact]
    public void a_cell_outside_the_box_is_refused_rather_than_guessed_at()
    {
        var light = Illuminate(new LitWorld());

        // Reading further out than the box reaches would be reading a cell belonging to some
        // other coordinate, which is the kind of mistake that shows up as a stripe of shadow.
        Assert.Throws<ArgumentOutOfRangeException>(() => light.SkyLightAt(48, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => light.BlockLightAt(0, -17, 0));
    }

    private LitWorld Roofed(int fromX) =>
        new LitWorld().WithBox(new int3(fromX, RoofY, -Reach), new int3(Reach, RoofY, Reach), _rock);

    private LightVolume Illuminate(LitWorld world)
    {
        var volume = new LightVolume();
        volume.Illuminate(int3.Zero, world, _registry, world);
        return volume;
    }
}
