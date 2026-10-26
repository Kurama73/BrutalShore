using BrutalShore.Voxel;

namespace Shorebound;

/// <summary>What a smelter turns into what, and what it will burn to do it.</summary>
/// <remarks>
/// <para>
/// Two tables rather than one, because a smelter asks two different questions of its slots: is
/// there something here I can melt, and is there something here I can burn. Keeping them apart is
/// what lets coal be fuel and iron ore be work without either needing to say it is not the other.
/// </para>
/// <para>
/// This is where the game stops being about carrying things and starts being about making them.
/// Ore out of the ground is worth nothing on its own — there is no recipe that takes it — and the
/// smelter is the only thing that changes that, which is what makes the first one a player builds
/// the moment the game opens up.
/// </para>
/// </remarks>
public sealed class Smelting
{
    private readonly Melt[] _melts;
    private readonly Fuel[] _fuels;

    /// <summary>Builds the tables for a game's blocks.</summary>
    /// <param name="blocks">The game's registered block types.</param>
    public Smelting(GameBlocks blocks)
    {
        // The times are what separates the three. Glass is quick because sand is everywhere and
        // waiting on a window is not interesting; iron is slow because it is the deepest of the
        // three and the wait is what a second smelter is bought to fix.
        _melts =
        [
            new Melt(blocks.Sand, blocks.Glass, Seconds: 4f),
            new Melt(blocks.CopperOre, blocks.CopperIngot, Seconds: 6f),
            new Melt(blocks.IronOre, blocks.IronIngot, Seconds: 9f),
        ];

        // Coal is the fuel worth carrying and wood is the one that gets a player started before
        // they have found any. The ratio is deliberately harsh on wood: a forest should not be an
        // alternative to a coal seam, only a way to survive until one is found.
        _fuels =
        [
            new Fuel(blocks.CoalOre, Seconds: 32f),
            new Fuel(blocks.Planks, Seconds: 6f),
            new Fuel(blocks.Log, Seconds: 18f),
        ];
    }

    /// <summary>What a block melts into, and how long it takes.</summary>
    /// <param name="block">What is in the input slot.</param>
    /// <returns>What comes out and after how long, or nothing if it does not melt.</returns>
    public (BlockId Result, float Seconds)? Melts(BlockId block)
    {
        foreach (var melt in _melts)
        {
            if (melt.Input == block)
                return (melt.Result, melt.Seconds);
        }

        return null;
    }

    /// <summary>How long one of a block burns for.</summary>
    /// <param name="block">What is in the fuel slot.</param>
    /// <returns>Its burn time in seconds, or zero if it does not burn.</returns>
    public float BurnSeconds(BlockId block)
    {
        foreach (var fuel in _fuels)
        {
            if (fuel.Block == block)
                return fuel.Seconds;
        }

        return 0f;
    }

    /// <summary>Everything that can be melted, for a screen that lists it.</summary>
    public IEnumerable<(BlockId Input, BlockId Result, float Seconds)> All =>
        _melts.Select(melt => (melt.Input, melt.Result, melt.Seconds));

    private readonly record struct Melt(BlockId Input, BlockId Result, float Seconds);

    private readonly record struct Fuel(BlockId Block, float Seconds);
}
