using BrutalShore.Voxel;

namespace BrutalShore.Voxel.Tests;

public class VoxelVertexTests
{
    [Fact]
    public void every_field_survives_being_packed_and_unpacked()
    {
        // The pack is the vertex format the shader reads. If a field is clipped by one bit the
        // symptom is geometry subtly in the wrong place, which is far cheaper to catch here.
        foreach (var x in new[] { 0, 1, 31, 32 })
        foreach (var face in Enum.GetValues<VoxelFace>())
        foreach (var occlusion in new[] { 0, 1, 2, 3 })
        foreach (var opacity in new byte[] { 0, 16, 176, 255 })
        foreach (var light in new[] { 0, 1, 14, 15 })
        foreach (var pattern in Enum.GetValues<BlockPattern>())
        {
            var vertex = new VoxelVertex(
                x,
                32 - x,
                17,
                face,
                occlusion,
                light,
                LightVolume.MaxLevel - light,
                new byte3(200, 7, 255),
                opacity,
                pattern
            );

            Assert.Equal(new int3(x, 32 - x, 17), vertex.Position);
            Assert.Equal(face, vertex.Face);
            Assert.Equal(occlusion, vertex.AmbientOcclusion);
            Assert.Equal(light, vertex.SkyLight);
            Assert.Equal(LightVolume.MaxLevel - light, vertex.BlockLight);
            Assert.Equal(new byte3(200, 7, 255), vertex.Color);
            Assert.Equal(pattern, vertex.Pattern);

            // Sixteenths, so what comes back is the opacity rounded down to one of sixteen
            // steps. Everything that reads it divides by fifteen, and nothing compares it to
            // the byte it was written as.
            Assert.Equal(opacity >> 4, vertex.Opacity);
        }
    }

    [Fact]
    public void a_vertex_is_eight_bytes()
    {
        // Stated as a test because the whole point of packing is the size, and a field quietly
        // added to the struct would double a streamed world's vertex bandwidth without any other
        // symptom.
        Assert.Equal(8, System.Runtime.InteropServices.Marshal.SizeOf<VoxelVertex>());
    }
}
