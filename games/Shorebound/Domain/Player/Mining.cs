using BrutalShore.World;

namespace Shorebound.Domain.Player;

/// <summary>How far through breaking a block the player is.</summary>
/// <remarks>
/// <para>
/// Breaking a block is the one thing a player does over and over, so it is the one thing that has
/// to feel like something. Instant is not a feeling: a click that empties a cube with no time in
/// between reads as a cheat code rather than as work, and it leaves nothing to tell a player that
/// stone is harder than earth.
/// </para>
/// <para>
/// All of that is one number and one rule: progress builds while the button is held and the aim
/// has not moved, and it is thrown away the moment either stops being true. Looking away is
/// giving up, and giving up starts again — which is also what stops a player chipping at six
/// blocks at once and finishing them all together.
/// </para>
/// </remarks>
public sealed class Mining
{
    private int3 _target;
    private bool _hasTarget;

    /// <summary>The block being broken, or nothing when the player is not breaking one.</summary>
    public int3? Target => _hasTarget ? _target : null;

    /// <summary>How far through, from 0 to just under 1.</summary>
    public float Progress { get; private set; }

    /// <summary>Advances the break, and says whether the block came apart on this step.</summary>
    /// <param name="world">The world the block is in, for how long it takes to break.</param>
    /// <param name="aimed">What the crosshair is on, or nothing.</param>
    /// <param name="isBreaking">Whether the break button is held.</param>
    /// <param name="seconds">How long this step is.</param>
    /// <returns>The block that broke, or nothing.</returns>
    public int3? Step(VoxelWorld world, VoxelHit? aimed, bool isBreaking, double seconds)
    {
        if (!isBreaking || aimed is not { } hit)
        {
            Give();
            return null;
        }

        if (!_hasTarget || _target != hit.Block)
        {
            _target = hit.Block;
            _hasTarget = true;
            Progress = 0f;
        }

        var type = world.Registry[world.BlockAt(hit.Block.X, hit.Block.Y, hit.Block.Z)];
        if (!type.IsBreakable)
        {
            Progress = 0f;
            return null;
        }

        // A block that takes no time is gone on the frame it was touched, which is what the zero
        // is for; dividing by it would be the other way of saying the same thing and would also
        // produce an infinity.
        if (type.BreakSeconds <= 0f)
        {
            Give();
            return hit.Block;
        }

        Progress += (float)(seconds / type.BreakSeconds);

        if (Progress < 1f)
            return null;

        Give();
        return hit.Block;
    }

    /// <summary>Forgets whatever was being broken, as letting go of the button does.</summary>
    public void Give()
    {
        _hasTarget = false;
        Progress = 0f;
    }
}
