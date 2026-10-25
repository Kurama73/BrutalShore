using BrutalShore.Voxel;

namespace Shorebound;

/// <summary>
/// The block types this game has, and the one place they are registered.
/// </summary>
/// <remarks>
/// Registration order is the id order, and ids end up in saved worlds, so types are appended and
/// never reordered or removed. Keeping that discipline in one small class is the point of the
/// class.
/// </remarks>
public sealed class GameBlocks
{
    /// <summary>Registers every type into a fresh registry.</summary>
    public GameBlocks(BlockRegistry registry)
    {
        Registry = registry;
        Stone = registry.Register(new BlockType { Name = "stone", Color = byte3.HexColor(0x7F7F84) });
        Dirt = registry.Register(new BlockType { Name = "dirt", Color = byte3.HexColor(0x6B4A2F) });
        Grass = registry.Register(new BlockType { Name = "grass", Color = byte3.HexColor(0x5A8F3C) });
        Sand = registry.Register(new BlockType { Name = "sand", Color = byte3.HexColor(0xD8CB94) });
        Snow = registry.Register(new BlockType { Name = "snow", Color = byte3.HexColor(0xEDF2F7) });
        Water = registry.Register(
            new BlockType
            {
                Name = "water",
                Color = byte3.HexColor(0x2E6FA8),

                // Enough to read as water rather than as glass: the seabed shows through in the
                // shallows and is lost in the deep, which is the whole of what makes a coast
                // legible from above.
                Opacity = 170,
                Collides = false,
                IsLiquid = true,

                // Two levels a block, so the sky reaches seven blocks down and no further. That
                // is what turns depth into darkness and makes a shelf of shallows visible from
                // the shore as something other than a change of colour.
                LightOpacity = 2,
            }
        );
        Bedrock = registry.Register(new BlockType { Name = "bedrock", Color = byte3.HexColor(0x2B2B30) });
        Log = registry.Register(new BlockType { Name = "log", Color = byte3.HexColor(0x5A4228) });
        Leaves = registry.Register(new BlockType { Name = "leaves", Color = byte3.HexColor(0x3E6B2A) });
        Lantern = registry.Register(
            new BlockType
            {
                Name = "lantern",
                Color = byte3.HexColor(0xFFE2A8),

                // One below the brightest there is, so a lantern lights fourteen blocks around
                // itself and a corridor still needs a second one. Full brightness would make a
                // single lantern enough for any cave anyone digs, which is the same as having no
                // dark in the game at all.
                Emission = 14,
            }
        );
    }

    /// <summary>The registry these ids index.</summary>
    public BlockRegistry Registry { get; }

    /// <summary>Stone, what the ground is made of below the surface.</summary>
    public BlockId Stone { get; }

    /// <summary>Dirt, the few blocks under a grass surface.</summary>
    public BlockId Dirt { get; }

    /// <summary>Grass, the surface above the water line.</summary>
    public BlockId Grass { get; }

    /// <summary>Sand, the surface near and below the water line.</summary>
    public BlockId Sand { get; }

    /// <summary>Snow, the surface high up.</summary>
    public BlockId Snow { get; }

    /// <summary>
    /// Water, which fills everything below the sea level that terrain did not.
    /// </summary>
    /// <remarks>
    /// See-through, walked into rather than onto, swum in, and drawn in the pass after everything
    /// solid. What it is not is a fluid: a placed block of it would be a cube of sea, which is why
    /// the hotbar leaves it out.
    /// </remarks>
    public BlockId Water { get; }

    /// <summary>Bedrock, the floor of the world.</summary>
    public BlockId Bedrock { get; }

    /// <summary>A tree's trunk.</summary>
    public BlockId Log { get; }

    /// <summary>A tree's canopy. Opaque: a forest of see-through leaves would be a forest
    /// of blended quads, and the blending order between them is not worth what it buys.</summary>
    public BlockId Leaves { get; }

    /// <summary>A lantern: the one block that gives off light of its own.</summary>
    public BlockId Lantern { get; }
}
