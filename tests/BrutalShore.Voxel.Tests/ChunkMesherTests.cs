using BrutalShore.Voxel;

namespace BrutalShore.Voxel.Tests;

public class ChunkMesherTests
{
    private readonly BlockRegistry _registry = new();
    private readonly ChunkMesher _mesher = new();
    private readonly VoxelMesh _mesh = new();
    private readonly BlockId _stone;
    private readonly BlockId _dirt;

    public ChunkMesherTests()
    {
        _stone = _registry.Register(new BlockType { Name = "stone", Color = byte3.HexColor(0x8A8A8A) });
        _dirt = _registry.Register(new BlockType { Name = "dirt", Color = byte3.HexColor(0x6B4F2A) });
    }

    [Fact]
    public void an_all_air_chunk_produces_nothing_to_draw()
    {
        var world = new TestWorld(new Chunk());

        Mesh(world);

        Assert.True(_mesh.IsEmpty);
        Assert.Equal(0, _mesh.TriangleCount);
    }

    [Fact]
    public void a_single_block_produces_its_six_faces()
    {
        var chunk = new Chunk();
        chunk[4, 5, 6] = _stone;

        Mesh(new TestWorld(chunk));

        Assert.Equal(6, _mesh.QuadCount);
        Assert.Equal(12, _mesh.TriangleCount);
        Assert.Equal(6, _mesh.Vertices.ToArray().Select(vertex => vertex.Face).Distinct().Count());
    }

    [Fact]
    public void a_solid_chunk_merges_down_to_one_quad_per_side()
    {
        // The headline number for greedy merging. Culling alone leaves 2,048 quads per side; the
        // merge takes each side to one, which is 12 triangles for 32,768 blocks.
        var chunk = new Chunk();
        chunk.Fill(_stone);

        Mesh(new TestWorld(chunk));

        Assert.Equal(6, _mesh.QuadCount);
        Assert.Equal(12, _mesh.TriangleCount);
    }

    [Fact]
    public void an_interior_face_between_two_solid_blocks_is_never_built()
    {
        var chunk = new Chunk();
        chunk[8, 8, 8] = _stone;
        chunk[9, 8, 8] = _stone;

        Mesh(new TestWorld(chunk));

        // Two cubes side by side share one face, so ten of the twelve faces survive and the two
        // facing each other do not. Merging then joins the four pairs that are coplanar, leaving
        // six quads: four merged sides and the two ends.
        Assert.Equal(6, _mesh.QuadCount);
        Assert.DoesNotContain(
            _mesh.Vertices.ToArray(),
            vertex => vertex.Face == VoxelFace.PositiveX && vertex.Position.X == 9
        );
    }

    [Fact]
    public void a_face_on_the_chunk_boundary_is_culled_against_the_neighbouring_chunk()
    {
        // The seam. Without reading past its own edge the chunk builds its whole top surface, and
        // so does the chunk above it: a double wall through the middle of the terrain, and the
        // single largest source of wasted triangles in a streamed world.
        var chunk = new Chunk();
        chunk.Fill(_stone);

        var sealedAbove = new TestWorld(chunk).WithSolidNeighbourAbove(_stone);
        Mesh(sealedAbove);
        var withNeighbour = _mesh.QuadCount;

        var openAbove = new TestWorld(chunk);
        Mesh(openAbove);
        var withoutNeighbour = _mesh.QuadCount;

        Assert.Equal(5, withNeighbour);
        Assert.Equal(6, withoutNeighbour);
    }

    [Fact]
    public void two_different_blocks_do_not_merge_into_one_quad()
    {
        // The merge key includes the colour, so a stone-and-dirt surface keeps its boundary.
        var chunk = new Chunk();
        for (var x = 0; x < Chunk.Size; x++)
            for (var z = 0; z < Chunk.Size; z++)
                chunk[x, 0, z] = x < 16 ? _stone : _dirt;

        Mesh(new TestWorld(chunk));

        var topQuads = CountQuads(VoxelFace.PositiveY);
        Assert.Equal(2, topQuads);
    }

    [Fact]
    public void a_checkerboard_is_the_case_with_nothing_to_merge_and_comes_out_no_worse()
    {
        // The worst case for the merge, and the one where a buggy sweep shows up: every face is
        // alone on its plane, so the output must be exactly what culling alone would give — six
        // faces per block, no more and no fewer.
        var chunk = new Chunk();
        var solid = 0;
        for (var y = 0; y < Chunk.Size; y++)
            for (var z = 0; z < Chunk.Size; z++)
                for (var x = 0; x < Chunk.Size; x++)
                    if ((x + y + z) % 2 == 0)
                    {
                        chunk[x, y, z] = _stone;
                        solid++;
                    }

        Mesh(new TestWorld(chunk));

        Assert.Equal(solid * 6, _mesh.QuadCount);
    }

    [Fact]
    public void every_quad_of_a_solid_chunk_faces_outward()
    {
        // Winding, checked through the geometry rather than by reading the index list: the two
        // triangles of each quad must have a normal pointing away from the chunk's centre, or
        // back-face culling keeps the inside of the world and discards the outside.
        var chunk = new Chunk();
        chunk.Fill(_stone);

        Mesh(new TestWorld(chunk));

        var vertices = _mesh.Vertices.ToArray();
        var indices = _mesh.Indices.ToArray();
        var centre = new float3(Chunk.Size / 2.0f);

        for (var triangle = 0; triangle < indices.Length; triangle += 3)
        {
            var a = ToFloat3(vertices[indices[triangle]].Position);
            var b = ToFloat3(vertices[indices[triangle + 1]].Position);
            var c = ToFloat3(vertices[indices[triangle + 2]].Position);

            var normal = float3.Cross(b - a, c - a);
            var outward = a - centre;

            Assert.True(float3.Dot(normal, outward) > 0.0f, $"triangle {triangle / 3} is wound inward");
        }
    }

    [Fact]
    public void a_corner_wedged_between_two_blocks_is_the_darkest_and_an_open_one_the_brightest()
    {
        // Ambient occlusion, checked at the two ends of its range. The classic inside corner: a
        // floor tile with a wall rising on two of its sides has its innermost corner wedged into
        // an interior angle, which is the darkest the rule produces. A lone block has no such
        // corner at all.
        var sheltered = new Chunk();
        sheltered[1, 0, 1] = _stone;
        sheltered[0, 1, 1] = _stone;
        sheltered[1, 1, 0] = _stone;
        Mesh(new TestWorld(sheltered));
        var darkest = _mesh.Vertices.ToArray().Min(vertex => vertex.AmbientOcclusion);

        var lone = new Chunk();
        lone[8, 8, 8] = _stone;
        Mesh(new TestWorld(lone));
        var allOpen = _mesh.Vertices.ToArray().All(vertex => vertex.AmbientOcclusion == 3);

        Assert.Equal(0, darkest);
        Assert.True(allOpen, "a block with nothing around it should have no occluded corner");
    }

    [Fact]
    public void occlusion_splits_a_run_that_culling_and_colour_alone_would_have_merged()
    {
        // Why occlusion has to be part of the merge key. This surface is one colour and entirely
        // visible, so colour and culling would merge it into a single quad — but one end of it is
        // shaded by the block standing on it, and a single quad cannot hold two shadings.
        var chunk = new Chunk();
        for (var x = 0; x < Chunk.Size; x++)
            chunk[x, 0, 0] = _stone;
        chunk[0, 1, 0] = _stone;

        Mesh(new TestWorld(chunk));

        Assert.True(CountQuads(VoxelFace.PositiveY) > 1, "the shaded end should not have merged into the lit run");
    }

    [Fact]
    public void meshing_the_same_chunk_twice_gives_the_same_mesh()
    {
        // The mesher is reused across a stream of chunks and keeps its working buffer, so a cell
        // left behind from the previous plane would show up as geometry appearing out of nowhere —
        // intermittently, and only under streaming.
        var chunk = new Chunk();
        for (var x = 0; x < Chunk.Size; x += 3)
            for (var y = 0; y < Chunk.Size; y += 2)
                chunk[x, y, x % Chunk.Size] = _stone;
        var world = new TestWorld(chunk);

        Mesh(world);
        var first = _mesh.Vertices.ToArray();
        Mesh(world);
        var second = _mesh.Vertices.ToArray();

        Assert.Equal(first, second);
    }

    private void Mesh(TestWorld world) => _mesher.Mesh(world.Chunk, int3.Zero, world, _registry, _mesh);

    private int CountQuads(VoxelFace face)
    {
        var vertices = _mesh.Vertices.ToArray();
        var count = 0;
        for (var quad = 0; quad < vertices.Length; quad += 4)
            if (vertices[quad].Face == face)
                count++;
        return count;
    }

    private static float3 ToFloat3(int3 value) => new(value.X, value.Y, value.Z);
}
