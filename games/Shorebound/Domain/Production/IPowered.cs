namespace Shorebound.Domain.Production;

/// <summary>A machine that would rather run on electricity than on something it burns.</summary>
/// <remarks>
/// <para>
/// The tier above fuel, and the reason to build a grid at all. A burner machine needs somebody to
/// walk coal to it; a dozen burner machines need somebody to walk coal to a dozen places, which
/// is the point at which hauling fuel becomes the whole game. Wiring them up moves that cost to
/// one place — the generator — and a belt can feed one of those.
/// </para>
/// <para>
/// A powered machine asks for what it wants and is told what share of it arrived. It is never
/// told whether there is a grid: a machine with no cable beside it is simply one that got
/// nothing, and falls back to burning, which is what makes a half-wired factory work rather than
/// stop.
/// </para>
/// </remarks>
public interface IPowered
{
    /// <summary>How much it would draw right now, in watts; zero when it has nothing to do.</summary>
    /// <remarks>
    /// Asked every step rather than stated once, because an idle machine must not hold a grid
    /// down. A row of smelters with nothing in them should let the one that is working run at
    /// full speed instead of all of them running at a twelfth.
    /// </remarks>
    double DemandWatts { get; }

    /// <summary>Tells it what share of what it asked for actually arrived.</summary>
    /// <param name="share">From 0 for nothing to 1 for all of it.</param>
    /// <remarks>
    /// A share rather than an amount, because what a machine does with a shortfall is its own
    /// business: a smelter runs slower, a turret simply does not fire. Factorio calls this a
    /// brownout and it is far better than a grid that cuts out, because the factory visibly
    /// struggles instead of stopping dead.
    /// </remarks>
    void Supply(double share);
}
