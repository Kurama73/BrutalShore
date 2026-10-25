namespace BrutalShore.Voxel.Tests;

/// <summary>
/// Pins down what the mesher does with blocks something can be seen through.
/// </summary>
/// <remarks>
/// Three rules, and getting any of them wrong produces a world that still renders: a sea drawn as
/// a lattice of every cell's six sides, a seabed invisible under it, or water that paints over
/// the mountain behind it because it went in the solid half.
/// </remarks>
public sealed class TranslucentMeshingTests
{
    private readonly BlockRegistry _registry = new();
    private readonly ChunkMesher _mesher = new();
    private readonly ChunkSurface _surface = new();
    private readonly BlockId _stone;
    private readonly BlockId _water;
    private readonly BlockId _glass;

    public TranslucentMeshingTests()
    {
        _stone = _registry.Register(new BlockType { Name = "stone", Color = byte3.HexColor(0x8A8A8A) });
        _water = _registry.Register(
            new BlockType { Name = "water", Color = byte3.HexColor(0x2E6FA8), Opacity = 170 }
        );
        _glass = _registry.Register(
            new BlockType { Name = "glass", Color = byte3.HexColor(0xBFE6F0), Opacity = 60 }
        );
    }

    [Fact]
    public void a_see_through_block_is_meshed_into_the_other_half()
    {
        var chunk = new Chunk();
        chunk[4, 4, 4] = _water;

        Mesh(chunk);

        Assert.True(_surface.Opaque.IsEmpty);
        Assert.Equal(6, _surface.Translucent.QuadCount);
    }

    [Fact]
    public void two_blocks_of_the_same_liquid_cost_no_more_surface_than_one()
    {
        var one = new Chunk();
        one[4, 4, 4] = _water;
        Mesh(one);
        var single = _surface.Translucent.QuadCount;

        var two = new Chunk();
        two[4, 4, 4] = _water;
        two[5, 4, 4] = _water;
        Mesh(two);

        // The faces where they meet are gone, and the four that run along both merge into one
        // quad each — so a body of water costs its surface rather than its volume, which is the
        // whole difference between a sea and a lattice.
        Assert.Equal(single, _surface.Translucent.QuadCount);
        Assert.DoesNotContain(
            _surface.Translucent.Vertices.ToArray(),
            vertex => vertex.Face == VoxelFace.PositiveX && vertex.Position.X == 5
        );
    }

    [Fact]
    public void two_different_see_through_blocks_still_show_the_boundary()
    {
        var chunk = new Chunk();
        chunk[4, 4, 4] = _water;
        chunk[5, 4, 4] = _glass;

        Mesh(chunk);

        // Twelve: both faces of the boundary are built, because one is water seen against glass
        // and the other is glass seen against water, and they do not look the same.
        Assert.Equal(12, _surface.Translucent.QuadCount);
    }

    [Fact]
    public void a_solid_block_under_water_still_has_its_top_drawn()
    {
        var chunk = new Chunk();
        chunk[4, 4, 4] = _stone;
        chunk[4, 5, 4] = _water;

        Mesh(chunk);

        // The seabed is the whole reason the water is see-through. A face pointing into water has
        // to be built, or there is nothing to see through it to.
        Assert.Equal(6, _surface.Opaque.QuadCount);
        Assert.Contains(
            _surface.Opaque.Vertices.ToArray(),
            vertex => vertex.Face == VoxelFace.PositiveY
        );
    }

    [Fact]
    public void water_against_a_solid_block_does_not_draw_the_face_it_cannot_be_seen_through()
    {
        var chunk = new Chunk();
        chunk[4, 4, 4] = _stone;
        chunk[4, 5, 4] = _water;

        Mesh(chunk);

        // Five, not six: the water's underside is against stone and nothing could see it.
        Assert.Equal(5, _surface.Translucent.QuadCount);
        Assert.DoesNotContain(
            _surface.Translucent.Vertices.ToArray(),
            vertex => vertex.Face == VoxelFace.NegativeY
        );
    }

    [Fact]
    public void the_opacity_travels_all_the_way_into_the_vertex()
    {
        var chunk = new Chunk();
        chunk[4, 4, 4] = _water;
        chunk[8, 4, 4] = _stone;

        Mesh(chunk);

        // What the shader blends with, so a wrong value here is a sea that is either invisible
        // or a solid wall, and nothing in between would say which.
        Assert.All(_surface.Translucent.Vertices.ToArray(), vertex => Assert.Equal(170 >> 4, vertex.Opacity));
        Assert.All(_surface.Opaque.Vertices.ToArray(), vertex => Assert.Equal(255 >> 4, vertex.Opacity));
    }

    [Fact]
    public void a_merged_quad_is_never_half_solid_and_half_see_through()
    {
        var chunk = new Chunk();

        // A row that alternates, so the greedy merge is tempted at every step.
        for (var x = 0; x < Chunk.Size; x++)
            chunk[x, 4, 4] = x % 2 == 0 ? _stone : _water;

        Mesh(chunk);

        Assert.All(_surface.Opaque.Vertices.ToArray(), vertex => Assert.Equal(255 >> 4, vertex.Opacity));
        Assert.All(_surface.Translucent.Vertices.ToArray(), vertex => Assert.Equal(170 >> 4, vertex.Opacity));
    }

    private void Mesh(Chunk chunk)
    {
        var world = new TestWorld(chunk);
        _mesher.Mesh(chunk, int3.Zero, world, _registry, world.Light(_registry), _surface);
    }
}
