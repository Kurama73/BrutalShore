using BrutalShore.Voxel;

namespace Shorebound.Domain.World;

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
    private readonly bool _useFaithfulTextures;

    /// <summary>Registers every type into a fresh registry.</summary>
    /// <param name="registry">The registry to populate.</param>
    /// <param name="useFaithfulTextures">Whether mapped Faithful texture layers are available.</param>
    public GameBlocks(BlockRegistry registry, bool useFaithfulTextures = false)
    {
        _useFaithfulTextures = useFaithfulTextures;
        Registry = registry;
        Stone = registry.Register(
            new BlockType
            {
                Name = "stone",
                Color = byte3.HexColor(0x7F7F84),
                Pattern = BlockPattern.Mottled,
                SideTextureLayer = Texture(GameTextureLayers.Stone),
                TopTextureLayer = Texture(GameTextureLayers.Stone),
                BottomTextureLayer = Texture(GameTextureLayers.Stone),
                BreakSeconds = 1.5f,
            }
        );
        Dirt = registry.Register(
            new BlockType
            {
                Name = "dirt",
                Color = byte3.HexColor(0x6B4A2F),
                Pattern = BlockPattern.Crumbly,
                SideTextureLayer = Texture(GameTextureLayers.Dirt),
                TopTextureLayer = Texture(GameTextureLayers.Dirt),
                BottomTextureLayer = Texture(GameTextureLayers.Dirt),
                BreakSeconds = 0.5f,
            }
        );
        Grass = registry.Register(
            new BlockType
            {
                Name = "grass",
                Color = byte3.HexColor(0x5A8F3C),
                Pattern = BlockPattern.Bladed,
                SideTextureLayer = Texture(GameTextureLayers.GrassSide),
                TopTextureLayer = Texture(GameTextureLayers.GrassTop),
                BottomTextureLayer = Texture(GameTextureLayers.Dirt),
                TopTextureTint = byte3.HexColor(0x7FB238),
                BreakSeconds = 0.6f,
            }
        );
        Sand = registry.Register(
            new BlockType
            {
                Name = "sand",
                Color = byte3.HexColor(0xD8CB94),
                Pattern = BlockPattern.Grainy,
                SideTextureLayer = Texture(GameTextureLayers.Sand),
                TopTextureLayer = Texture(GameTextureLayers.Sand),
                BottomTextureLayer = Texture(GameTextureLayers.Sand),
                BreakSeconds = 0.4f,
            }
        );
        Snow = registry.Register(
            new BlockType
            {
                Name = "snow",
                Color = byte3.HexColor(0xEDF2F7),
                Pattern = BlockPattern.Glinting,
                SideTextureLayer = Texture(GameTextureLayers.Snow),
                TopTextureLayer = Texture(GameTextureLayers.Snow),
                BottomTextureLayer = Texture(GameTextureLayers.Snow),
                BreakSeconds = 0.3f,
            }
        );
        Water = registry.Register(
            new BlockType
            {
                Name = "water",
                Color = byte3.HexColor(0x2E6FA8),

                // Enough to read as water rather than as glass: the seabed shows through in the
                // shallows and is lost in the deep, which is the whole of what makes a coast
                // legible from above.
                Opacity = 170,
                Pattern = BlockPattern.Rippled,
                SideTextureLayer = Texture(GameTextureLayers.Water),
                TopTextureLayer = Texture(GameTextureLayers.Water),
                BottomTextureLayer = Texture(GameTextureLayers.Water),
                Collides = false,
                IsLiquid = true,

                // Two levels a block, so the sky reaches seven blocks down and no further. That
                // is what turns depth into darkness and makes a shelf of shallows visible from
                // the shore as something other than a change of colour.
                LightOpacity = 2,
            }
        );
        Bedrock = registry.Register(
            new BlockType
            {
                Name = "bedrock",
                Color = byte3.HexColor(0x2B2B30),
                Pattern = BlockPattern.Mottled,
                SideTextureLayer = Texture(GameTextureLayers.Bedrock),
                TopTextureLayer = Texture(GameTextureLayers.Bedrock),
                BottomTextureLayer = Texture(GameTextureLayers.Bedrock),

                // The floor of the world, and the one block that never comes up. Without it a
                // player who digs long enough falls out of the bottom of everything.
                BreakSeconds = float.PositiveInfinity,
            }
        );
        Log = registry.Register(
            new BlockType
            {
                Name = "log",
                Color = byte3.HexColor(0x5A4228),
                Pattern = BlockPattern.Grained,
                SideTextureLayer = Texture(GameTextureLayers.LogSide),
                TopTextureLayer = Texture(GameTextureLayers.LogEnd),
                BottomTextureLayer = Texture(GameTextureLayers.LogEnd),
                BreakSeconds = 1.1f,
            }
        );
        Leaves = registry.Register(
            new BlockType
            {
                Name = "leaves",
                Color = byte3.HexColor(0x3E6B2A),
                Pattern = BlockPattern.Scattered,
                SideTextureLayer = Texture(GameTextureLayers.Leaves),
                TopTextureLayer = Texture(GameTextureLayers.Leaves),
                BottomTextureLayer = Texture(GameTextureLayers.Leaves),
                SideTextureTint = byte3.HexColor(0x48B518),
                TopTextureTint = byte3.HexColor(0x48B518),
                BottomTextureTint = byte3.HexColor(0x48B518),
                BreakSeconds = 0.2f,
            }
        );
        Lantern = registry.Register(
            new BlockType
            {
                Name = "lantern",
                Color = byte3.HexColor(0xFFE2A8),
                Pattern = BlockPattern.Radiant,
                SideTextureLayer = Texture(GameTextureLayers.Lantern),
                TopTextureLayer = Texture(GameTextureLayers.Lantern),
                BottomTextureLayer = Texture(GameTextureLayers.Lantern),
                BreakSeconds = 0.4f,

                // One below the brightest there is, so a lantern lights fourteen blocks around
                // itself and a corridor still needs a second one. Full brightness would make a
                // single lantern enough for any cave anyone digs, which is the same as having no
                // dark in the game at all.
                Emission = 14,
            }
        );

        // Made things, not found ones: nothing in the world generates these, and the only way to
        // hold one is to make it. They are what the crafting grid is for.
        Planks = registry.Register(
            new BlockType
            {
                Name = "planks",
                Color = byte3.HexColor(0xB08A52),
                Pattern = BlockPattern.Grained,
                SideTextureLayer = Texture(GameTextureLayers.Planks),
                TopTextureLayer = Texture(GameTextureLayers.Planks),
                BottomTextureLayer = Texture(GameTextureLayers.Planks),
                BreakSeconds = 0.8f,
            }
        );
        StoneBrick = registry.Register(
            new BlockType
            {
                Name = "stone brick",
                Color = byte3.HexColor(0x8C8C92),
                Pattern = BlockPattern.Mottled,
                SideTextureLayer = Texture(GameTextureLayers.StoneBricks),
                TopTextureLayer = Texture(GameTextureLayers.StoneBricks),
                BottomTextureLayer = Texture(GameTextureLayers.StoneBricks),
                BreakSeconds = 1.6f,
            }
        );
        CopperIngot = registry.Register(
            new BlockType
            {
                Name = "copper ingot",
                Color = byte3.HexColor(0xD9873F),
                Pattern = BlockPattern.Smooth,
                SideTextureLayer = Texture(GameTextureLayers.CopperBlock),
                TopTextureLayer = Texture(GameTextureLayers.CopperBlock),
                BottomTextureLayer = Texture(GameTextureLayers.CopperBlock),
                BreakSeconds = 1.2f,
            }
        );
        IronIngot = registry.Register(
            new BlockType
            {
                Name = "iron ingot",
                Color = byte3.HexColor(0xBDB5AC),
                Pattern = BlockPattern.Smooth,
                SideTextureLayer = Texture(GameTextureLayers.IronBlock),
                TopTextureLayer = Texture(GameTextureLayers.IronBlock),
                BottomTextureLayer = Texture(GameTextureLayers.IronBlock),
                BreakSeconds = 1.4f,
            }
        );
        Glass = registry.Register(
            new BlockType
            {
                Name = "glass",
                Color = byte3.HexColor(0xBFD8E4),
                Pattern = BlockPattern.Smooth,
                SideTextureLayer = Texture(GameTextureLayers.Glass),
                TopTextureLayer = Texture(GameTextureLayers.Glass),
                BottomTextureLayer = Texture(GameTextureLayers.Glass),
                BreakSeconds = 0.5f,

                // See-through but not water: it is in the way of a body and it lets nearly all
                // the light past, which is the whole of what a window is.
                Opacity = 70,
                LightOpacity = 1,
            }
        );
        Cable = registry.Register(
            new BlockType
            {
                Name = "cable",
                Color = byte3.HexColor(0x3E4044),
                Pattern = BlockPattern.Smooth,
                SideTextureLayer = Texture(GameTextureLayers.IronBlock),
                TopTextureLayer = Texture(GameTextureLayers.IronBlock),
                BottomTextureLayer = Texture(GameTextureLayers.IronBlock),
                BreakSeconds = 0.3f,

                // Run along a floor or up a wall and walked through, like a belt.
                Collides = false,
            }
        );
        Generator = registry.Register(
            new BlockType
            {
                Name = "generator",
                Color = byte3.HexColor(0x6B5A3E),
                Pattern = BlockPattern.Mottled,
                SideTextureLayer = Texture(GameTextureLayers.BlastFurnace),
                TopTextureLayer = Texture(GameTextureLayers.BlastFurnace),
                BottomTextureLayer = Texture(GameTextureLayers.BlastFurnace),
                BreakSeconds = 2.6f,
                Emission = 6,
            }
        );
        Turret = registry.Register(
            new BlockType
            {
                Name = "turret",
                Color = byte3.HexColor(0x7C5248),
                Pattern = BlockPattern.Smooth,
                SideTextureLayer = Texture(GameTextureLayers.IronBlock),
                TopTextureLayer = Texture(GameTextureLayers.DispenserFront),
                BottomTextureLayer = Texture(GameTextureLayers.IronBlock),
                BreakSeconds = 2.8f,
                Emission = 3,
            }
        );
        Lift = registry.Register(
            new BlockType
            {
                Name = "lift",
                Color = byte3.HexColor(0x8A7A58),
                Pattern = BlockPattern.Grained,
                SideTextureLayer = Texture(GameTextureLayers.Scaffolding),
                TopTextureLayer = Texture(GameTextureLayers.IronBlock),
                BottomTextureLayer = Texture(GameTextureLayers.IronBlock),
                BreakSeconds = 0.8f,
                Collides = false,
            }
        );
        Miner = registry.Register(
            new BlockType
            {
                Name = "miner",
                Color = byte3.HexColor(0x5A5F66),
                Pattern = BlockPattern.Smooth,
                SideTextureLayer = Texture(GameTextureLayers.StonecutterSide),
                TopTextureLayer = Texture(GameTextureLayers.StonecutterTop),
                BottomTextureLayer = Texture(GameTextureLayers.IronBlock),
                BreakSeconds = 2.4f,
                Emission = 4,
            }
        );
        Conveyor = registry.Register(
            new BlockType
            {
                Name = "conveyor",
                Color = byte3.HexColor(0x6E6A62),
                Pattern = BlockPattern.Grained,
                SideTextureLayer = Texture(GameTextureLayers.IronBlock),
                TopTextureLayer = Texture(GameTextureLayers.Scaffolding),
                BottomTextureLayer = Texture(GameTextureLayers.IronBlock),
                BreakSeconds = 0.6f,

                // Walked over rather than climbed. A belt that was a full block in the way would
                // make a factory floor a maze, and nobody builds a line they have to jump along.
                Collides = false,
            }
        );
        Smelter = registry.Register(
            new BlockType
            {
                Name = "smelter",
                Color = byte3.HexColor(0x4A4036),
                Pattern = BlockPattern.Mottled,
                SideTextureLayer = Texture(GameTextureLayers.BlastFurnace),
                TopTextureLayer = Texture(GameTextureLayers.BlastFurnace),
                BottomTextureLayer = Texture(GameTextureLayers.BlastFurnace),
                BreakSeconds = 2f,

                // A little light, so a line of them along a wall is visible from down the tunnel.
                // Not enough to be a lamp: a player who lit their base with smelters would be
                // getting light for free out of something built for something else.
                Emission = 5,
            }
        );
        CopperBlock = registry.Register(
            new BlockType
            {
                Name = "copper block",
                Color = byte3.HexColor(0xC87F42),
                Pattern = BlockPattern.Smooth,
                SideTextureLayer = Texture(GameTextureLayers.CopperBlock),
                TopTextureLayer = Texture(GameTextureLayers.CopperBlock),
                BottomTextureLayer = Texture(GameTextureLayers.CopperBlock),
                BreakSeconds = 2.4f,
            }
        );
        IronBlock = registry.Register(
            new BlockType
            {
                Name = "iron block",
                Color = byte3.HexColor(0xA8A099),
                Pattern = BlockPattern.Smooth,
                SideTextureLayer = Texture(GameTextureLayers.IronBlock),
                TopTextureLayer = Texture(GameTextureLayers.IronBlock),
                BottomTextureLayer = Texture(GameTextureLayers.IronBlock),
                BreakSeconds = 3f,
            }
        );

        // The ores, appended last because registration order is id order and a saved world holds
        // the ids. Their names are what the save file and the recipes both refer to.
        CoalOre = registry.Register(
            new BlockType
            {
                Name = "coal ore",
                Color = byte3.HexColor(0x3A3A40),
                Pattern = BlockPattern.Scattered,
                SideTextureLayer = Texture(GameTextureLayers.CoalOre),
                TopTextureLayer = Texture(GameTextureLayers.CoalOre),
                BottomTextureLayer = Texture(GameTextureLayers.CoalOre),

                // Softer than the stone it sits in, so a seam gives way faster than the rock
                // around it and following one reads as following something.
                BreakSeconds = 1.2f,
            }
        );
        CopperOre = registry.Register(
            new BlockType
            {
                Name = "copper ore",
                Color = byte3.HexColor(0xB4703C),
                Pattern = BlockPattern.Mottled,
                SideTextureLayer = Texture(GameTextureLayers.CopperOre),
                TopTextureLayer = Texture(GameTextureLayers.CopperOre),
                BottomTextureLayer = Texture(GameTextureLayers.CopperOre),
                BreakSeconds = 1.8f,
            }
        );
        IronOre = registry.Register(
            new BlockType
            {
                Name = "iron ore",
                Color = byte3.HexColor(0x9C8C7C),
                Pattern = BlockPattern.Mottled,
                SideTextureLayer = Texture(GameTextureLayers.IronOre),
                TopTextureLayer = Texture(GameTextureLayers.IronOre),
                BottomTextureLayer = Texture(GameTextureLayers.IronOre),
                BreakSeconds = 2.2f,
            }
        );
        EmberOre = registry.Register(
            new BlockType
            {
                Name = "ember ore",
                Color = byte3.HexColor(0xE8873C),
                Pattern = BlockPattern.Radiant,
                SideTextureLayer = Texture(GameTextureLayers.Magma),
                TopTextureLayer = Texture(GameTextureLayers.Magma),
                BottomTextureLayer = Texture(GameTextureLayers.Magma),
                BreakSeconds = 2.8f,

                // It glows, which is the whole reason to go that deep for it: a seam of it is
                // visible down a tunnel before the tunnel reaches it. Dimmer than a lantern, so
                // finding one is not the same as lighting the cave.
                Emission = 9,
            }
        );
    }

    /// <summary>The registry these ids index.</summary>
    public BlockRegistry Registry { get; }

    /// <summary>Planks, cut from a log and the start of everything else.</summary>
    public BlockId Planks { get; }

    /// <summary>Stone brick, cut stone, for building with rather than for anything else.</summary>
    public BlockId StoneBrick { get; }

    /// <summary>A copper ingot, which is what a smelter makes out of copper ore.</summary>
    public BlockId CopperIngot { get; }

    /// <summary>An iron ingot, which is what a smelter makes out of iron ore.</summary>
    public BlockId IronIngot { get; }

    /// <summary>Glass, melted out of sand, and the only block worth building a window from.</summary>
    public BlockId Glass { get; }

    /// <summary>Cable, which joins machines into one grid.</summary>
    public BlockId Cable { get; }

    /// <summary>A generator, which burns fuel into electricity for everything wired to it.</summary>
    public BlockId Generator { get; }

    /// <summary>A turret, which shoots what comes out of the dark as long as it has power.</summary>
    public BlockId Turret { get; }

    /// <summary>A lift, which is a belt that goes up.</summary>
    public BlockId Lift { get; }

    /// <summary>A miner, which stands on a seam and keeps bringing it up.</summary>
    public BlockId Miner { get; }

    /// <summary>A conveyor, which carries what is put on it to the block in front.</summary>
    public BlockId Conveyor { get; }

    /// <summary>A smelter, which burns fuel to turn one block into another.</summary>
    public BlockId Smelter { get; }

    /// <summary>A block of copper, which is nine ingots packed into one.</summary>
    public BlockId CopperBlock { get; }

    /// <summary>A block of iron, which is nine ingots packed into one.</summary>
    public BlockId IronBlock { get; }

    /// <summary>Coal ore, the fuel seam, common and shallow.</summary>
    public BlockId CoalOre { get; }

    /// <summary>Copper ore, the first metal, found at most depths.</summary>
    public BlockId CopperOre { get; }

    /// <summary>Iron ore, the structural metal, deeper than copper.</summary>
    public BlockId IronOre { get; }

    /// <summary>Ember ore, which glows, and is only down where the bedrock starts.</summary>
    public BlockId EmberOre { get; }

    private byte Texture(byte layer) => _useFaithfulTextures ? layer : (byte)0;

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
    /// the inventory leaves it out.
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