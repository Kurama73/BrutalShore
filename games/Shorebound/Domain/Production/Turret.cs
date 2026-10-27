using BrutalShore.Voxel;
using Shorebound.Domain.Creatures;
using Shorebound.Domain.Player;

namespace Shorebound.Domain.Production;

/// <summary>A turret: something the player builds that shoots back while they are elsewhere.</summary>
/// <remarks>
/// <para>
/// The piece that closes the loop. Up to here the two halves of the game pulled against each
/// other — every machine running draws more out of the dark, and the only answer was to be
/// standing there with a swing. A turret turns defence into something that is built, which means
/// the answer to a bigger horde is a bigger factory rather than a longer night awake.
/// </para>
/// <para>
/// It runs on electricity and nothing else. That is deliberate and it is where the grid earns its
/// place: a player who wires their turrets to the same generator as their smelters finds out, the
/// first time a fight starts during a smelting run, that they have built one grid where they
/// needed two. Nothing explains that as well as it happening.
/// </para>
/// </remarks>
public sealed class Turret : IMachine, IPowered
{
    /// <summary>How far it will shoot, in metres.</summary>
    /// <remarks>
    /// Twelve, which is a little further than a stalker can cross before it is shot twice, and
    /// well short of being able to cover a base from one corner. Overlapping fire is meant to be
    /// something a player arranges.
    /// </remarks>
    public const double ReachMeters = 12.0;

    /// <summary>How long between one shot and the next, in seconds.</summary>
    private const double EverySeconds = 0.9;

    /// <summary>How much one shot takes off.</summary>
    /// <remarks>
    /// Three, against a stalker's six, so two shots finish one — the same as a player's swing.
    /// A turret is not better than a person, it is merely awake.
    /// </remarks>
    public const float Damage = 3f;

    /// <summary>How much it draws while it has something to shoot at, in watts.</summary>
    public const double DrawWatts = 120.0;

    private double _reloadLeftSeconds;
    private double _powerShare;
    private bool _hasTarget;

    /// <summary>Whether it fired within the last moment, for drawing a muzzle flash.</summary>
    public bool JustFired { get; private set; }

    /// <summary>How many times it has fired since it was built.</summary>
    /// <remarks>
    /// Not saved and not restored. It answers one question — whether the thing on the wire is
    /// actually doing anything over a night — and a count that survived a reload would answer it
    /// about some other night as well.
    /// </remarks>
    public int Shots { get; private set; }

    /// <summary>Which way it is pointing, in radians, for drawing it.</summary>
    public float Yaw { get; private set; }

    /// <summary>Whether it has power and something in range.</summary>
    public bool IsLive => _hasTarget && _reloadLeftSeconds < EverySeconds;

    /// <inheritdoc />
    public void Step(in MachineStep step)
    {
        // A turret has to see the horde, and the horde is the game's rather than the machine
        // map's, so the shooting happens elsewhere. What is done here is forgetting last step's
        // target, so that a turret whose horde has gone stops drawing on the grid.
        JustFired = false;
        _hasTarget = false;
    }

    /// <summary>Looks for something in range and shoots it.</summary>
    /// <param name="position">Where this turret stands, in blocks.</param>
    /// <param name="stalkers">The horde, which this shoots into.</param>
    /// <param name="seconds">How long this step is.</param>
    /// <returns>Whether it fired.</returns>
    /// <remarks>
    /// Reloading runs at the share of power it got, so a turret on a browning-out grid fires
    /// slower rather than not at all. A player watching their defences get sluggish as the
    /// smelters come up to heat is being told exactly what is wrong.
    /// </remarks>
    public bool Fire(int3 position, Stalkers stalkers, double seconds)
    {
        var middle = new double3(position.X + 0.5, position.Y + 0.6, position.Z + 0.5);
        var nearest = stalkers.NearestTo(middle, ReachMeters);

        _hasTarget = nearest is not null;
        JustFired = false;

        if (nearest is not { } target)
            return false;

        var toIt = target - middle;
        Yaw = (float)double.Atan2(toIt.X, toIt.Z);

        // Taken and cleared, like every other powered machine: a share is good for one step.
        var share = _powerShare;
        _powerShare = 0.0;

        _reloadLeftSeconds -= seconds * share;
        if (_reloadLeftSeconds > 0.0 || share <= 0.0)
            return false;

        _reloadLeftSeconds = EverySeconds;
        JustFired = true;
        Shots++;

        var away = double.Sqrt(double3.Dot(toIt, toIt));
        return stalkers.Strike(middle, toIt / double.Max(away, 1e-9), ReachMeters + 1.0, Damage);
    }

    /// <inheritdoc />
    public double DemandWatts => _hasTarget ? DrawWatts : 0.0;

    /// <inheritdoc />
    public void Supply(double share) => _powerShare = double.Clamp(share, 0.0, 1.0);

    /// <inheritdoc />
    public bool Offer(BlockId block) => false;

    /// <inheritdoc />
    public BlockId? TakeOut() => null;

    /// <inheritdoc />
    public IEnumerable<Stack> Contents() => [];
}
