using Shorebound.Domain.World;

namespace Shorebound.Tests.Domain.World;

public class GameBlocksTests
{
    [Fact]
    public void block_materials_keep_procedural_surfaces_when_faithful_is_not_enabled()
    {
        var blocks = new GameBlocks(new BrutalShore.Voxel.BlockRegistry());

        Assert.All(
            blocks.Registry.Types,
            type =>
            {
                Assert.Equal(0, type.SideTextureLayer);
                Assert.Equal(0, type.TopTextureLayer);
                Assert.Equal(0, type.BottomTextureLayer);
            }
        );
    }

    [Fact]
    public void block_materials_select_distinct_grass_and_log_faces_when_faithful_is_enabled()
    {
        var blocks = new GameBlocks(new BrutalShore.Voxel.BlockRegistry(), useFaithfulTextures: true);
        var grass = blocks.Registry[blocks.Grass];
        var log = blocks.Registry[blocks.Log];
        var leaves = blocks.Registry[blocks.Leaves];

        Assert.NotEqual(0, grass.SideTextureLayer);
        Assert.NotEqual(grass.SideTextureLayer, grass.TopTextureLayer);
        Assert.NotEqual(grass.TopTextureLayer, grass.BottomTextureLayer);
        Assert.Equal(byte3.HexColor(0xFFFFFF), grass.SideTextureTint);
        Assert.Equal(byte3.HexColor(0x7FB238), grass.TopTextureTint);
        Assert.Equal(byte3.HexColor(0xFFFFFF), grass.BottomTextureTint);
        Assert.NotEqual(log.SideTextureLayer, log.TopTextureLayer);
        Assert.Equal(log.TopTextureLayer, log.BottomTextureLayer);
        Assert.Equal(byte3.HexColor(0x48B518), leaves.SideTextureTint);
        Assert.Equal(byte3.HexColor(0x48B518), leaves.TopTextureTint);
        Assert.Equal(byte3.HexColor(0x48B518), leaves.BottomTextureTint);
    }
}
