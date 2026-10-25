namespace BrutalShore.World;

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
        Water = registry.Register(new BlockType { Name = "water", Color = byte3.HexColor(0x2E6FA8) });
        Bedrock = registry.Register(new BlockType { Name = "bedrock", Color = byte3.HexColor(0x2B2B30) });
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
    /// Opaque for now, and the mesher treats it as solid like everything else. Transparency is a
    /// change to the mesher and the draw order, not to this type, so it stays out until that work
    /// is done rather than being half-declared here.
    /// </remarks>
    public BlockId Water { get; }

    /// <summary>Bedrock, the floor of the world.</summary>
    public BlockId Bedrock { get; }
}
