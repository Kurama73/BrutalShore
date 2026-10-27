namespace Shorebound.Domain.World;

/// <remarks>
/// Texture-layer identifiers are part of the block and creature definitions. Their ordering is
/// stable because meshed vertices store them; the infrastructure loader maps files to these slots.
/// </remarks>
internal static class GameTextureLayers
{
    internal const byte Stone = 1;
    internal const byte Dirt = 2;
    internal const byte GrassSide = 3;
    internal const byte GrassTop = 4;
    internal const byte Sand = 5;
    internal const byte Snow = 6;
    internal const byte Water = 7;
    internal const byte Bedrock = 8;
    internal const byte LogSide = 9;
    internal const byte LogEnd = 10;
    internal const byte Leaves = 11;
    internal const byte Lantern = 12;
    internal const byte Planks = 13;
    internal const byte StoneBricks = 14;
    internal const byte CopperBlock = 15;
    internal const byte IronBlock = 16;
    internal const byte Glass = 17;
    internal const byte BlastFurnace = 18;
    internal const byte DispenserFront = 19;
    internal const byte Scaffolding = 20;
    internal const byte StonecutterSide = 21;
    internal const byte CoalOre = 22;
    internal const byte CopperOre = 23;
    internal const byte IronOre = 24;
    internal const byte Magma = 25;
    internal const byte StonecutterTop = 26;
    internal const byte Cow = 27;
    internal const byte Wolf = 28;
    internal const byte Player = 29;
}