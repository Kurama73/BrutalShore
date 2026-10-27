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
        foreach (var textureLayer in new byte[] { 0, 1, 127, 255 })
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
                pattern,
                textureLayer,
                byte3.HexColor(0x7FB238)
            );

            Assert.Equal(new int3(x, 32 - x, 17), vertex.Position);
            Assert.Equal(face, vertex.Face);
            Assert.Equal(occlusion, vertex.AmbientOcclusion);
            Assert.Equal(light, vertex.SkyLight);
            Assert.Equal(LightVolume.MaxLevel - light, vertex.BlockLight);
            Assert.Equal(new byte3(200, 7, 255), vertex.Color);
            Assert.Equal(pattern, vertex.Pattern);
            Assert.Equal(textureLayer, vertex.TextureLayer);
            Assert.Equal(byte3.HexColor(0x7FB238), vertex.TextureTint);

            // Sixteenths, so what comes back is the opacity rounded down to one of sixteen
            // steps. Everything that reads it divides by fifteen, and nothing compares it to
            // the byte it was written as.
            Assert.Equal(opacity >> 4, vertex.Opacity);
        }
    }

    [Fact]
    public void a_vertex_is_twelve_bytes()
    {
        // The extra word carries one texture layer without inflating the remaining packed fields.
        Assert.Equal(12, System.Runtime.InteropServices.Marshal.SizeOf<VoxelVertex>());
    }
}
