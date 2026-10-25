using BrutalShore.Voxel;

namespace BrutalShore.Voxel.Tests;

/// <summary>
/// That the light worked out for a chunk ends up in the chunk's vertices, and that two faces lit
/// differently stay two faces.
/// </summary>
/// <remarks>
/// The second half is the part worth a test. Light is in the merge key, so a wall shading from
/// bright to dark has to come out as a run of quads; dropping it from the key would merge them
/// and the wall would be one flat brightness, which is the artefact ambient occlusion was already
/// kept in the key to avoid.
/// </remarks>
public sealed class LitMeshingTests
{
    private const int FloorY = 0;
    private const int RoofY = 10;
    private const int RoofFromX = 8;

    // The box lit around the chunk reaches sixteen blocks past it, so a roof meant to be sealed
    // has to cover all of that or daylight arrives under its far edge.
    private const int Reach = 48;

    private readonly BlockRegistry _registry = new();
    private readonly ChunkMesher _mesher = new();
    private readonly ChunkSurface _surface = new();
    private readonly BlockId _stone;
    private readonly BlockId _lamp;

    /// <summary>Registers the two blocks these claims need.</summary>
    public LitMeshingTests()
    {
        _stone = _registry.Register(new BlockType { Name = "stone", Color = new byte3(128, 128, 128) });
        _lamp = _registry.Register(
            new BlockType { Name = "lamp", Color = new byte3(255, 230, 170), Emission = 14 }
        );
    }

    [Fact]
    public void a_surface_with_nothing_above_it_is_meshed_at_full_sky_light()
    {
        var world = Floored();

        Mesh(world);

        var top = FloorFaces();
        Assert.NotEmpty(top);
        Assert.All(top, vertex => Assert.Equal(LightVolume.MaxLevel, vertex.SkyLight));
        Assert.All(top, vertex => Assert.Equal(0, vertex.BlockLight));
    }

    [Fact]
    public void a_surface_loses_sky_light_the_further_under_a_roof_it_is()
    {
        Mesh(Roofed());

        // The darkest corner at each depth under the roof, in order of depth. Reading the darkest
        // one keeps the comparison between like and like: a corner averages the cells around it,
        // so the two corners of a quad are a level apart by construction.
        var byDepth = FloorFaces()
            .Where(vertex => vertex.Position.X >= RoofFromX)
            .GroupBy(vertex => vertex.Position.X)
            .OrderBy(group => group.Key)
            .Select(group => group.Min(vertex => vertex.SkyLight))
            .ToArray();

        Assert.Equal(byDepth.OrderDescending(), byDepth);
        Assert.True(byDepth[0] > 0, "the roof's own edge is already black");
        Assert.Equal(0, byDepth[^1]);
    }

    [Fact]
    public void a_surface_shading_from_bright_to_dark_is_meshed_as_more_than_one_quad()
    {
        Mesh(Floored());
        var evenlyLit = CountFloorQuads();

        Mesh(Roofed());
        var shaded = CountFloorQuads();

        // One quad for the whole floor when it is lit evenly, and one per step of brightness once
        // it is not. The ratio is the cost of smooth lighting, paid only where the light changes.
        Assert.Equal(1, evenlyLit);
        Assert.True(shaded > evenlyLit, $"a shaded floor came out as {shaded} quads, the same as an even one");
    }

    [Fact]
    public void a_glowing_block_shows_up_in_the_faces_around_it()
    {
        var world = Floored().Place(4, FloorY, 4, _lamp);

        Mesh(world);

        var beside = FloorFaces()
            .Where(vertex => vertex.Position.X is 4 or 5 && vertex.Position.Z is 4 or 5)
            .ToArray();

        Assert.NotEmpty(beside);
        Assert.All(beside, vertex => Assert.True(vertex.BlockLight > 0, "a corner beside the lamp is unlit"));

        // And nothing fifteen blocks away does, because that is as far as a lamp reaches.
        Assert.All(
            FloorFaces().Where(vertex => vertex.Position.X > 4 + LightVolume.MaxLevel),
            vertex => Assert.Equal(0, vertex.BlockLight)
        );
    }

    /// <summary>A floor across the whole chunk, under an open sky.</summary>
    private TestWorld Floored()
    {
        var chunk = new Chunk();
        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
            chunk[x, FloorY, z] = _stone;

        var world = new TestWorld(chunk);

        // The floor continues past the chunk, so the faces at its edge are culled and what is
        // left is one surface rather than a lip the light treats differently.
        for (var z = -1; z <= Chunk.Size; z++)
        for (var x = -1; x <= Chunk.Size; x++)
            world.Place(x, FloorY, z, _stone);

        return world;
    }

    /// <summary>The same floor, with half of it roofed over and the other half open to the sky.</summary>
    private TestWorld Roofed()
    {
        var world = Floored();

        for (var z = -Reach; z <= Reach; z++)
        for (var x = RoofFromX; x <= Reach; x++)
            world.Place(x, RoofY, z, _stone);

        return world;
    }

    private void Mesh(TestWorld world) =>
        _mesher.Mesh(world.Chunk, int3.Zero, world, _registry, world.Light(_registry), _surface);

    private VoxelVertex[] TopFaces() =>
        _surface.Opaque.Vertices.ToArray().Where(vertex => vertex.Face == VoxelFace.PositiveY).ToArray();

    /// <summary>The upward faces of the floor, which leaves out the roof's own.</summary>
    private VoxelVertex[] FloorFaces() =>
        TopFaces().Where(vertex => vertex.Position.Y == FloorY + 1).ToArray();

    private int CountFloorQuads()
    {
        var vertices = _surface.Opaque.Vertices.ToArray();
        var count = 0;
        for (var quad = 0; quad < vertices.Length; quad += 4)
            if (vertices[quad].Face == VoxelFace.PositiveY && vertices[quad].Position.Y == FloorY + 1)
                count++;

        return count;
    }
}
