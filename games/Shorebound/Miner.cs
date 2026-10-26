using BrutalShore.Voxel;

namespace Shorebound;

/// <summary>A drill that stands on ore and keeps taking it out, as long as it is fed fuel.</summary>
/// <remarks>
/// <para>
/// The other end of the line from the smelter, and the piece that makes the whole thing worth
/// building. Up to here a factory still had a player at the front of it swinging at rock; a miner
/// means a seam found once goes on producing while they are somewhere else entirely, which is the
/// difference between a machine that saves effort and one that multiplies it.
/// </para>
/// <para>
/// It does not break the block it stands on. A drill that consumed its seam would turn a found
/// vein into a countdown, and the interesting decision — is this seam worth building a line out
/// to — only exists if the answer is about distance rather than about how many are left. The ore
/// a seam yields is therefore unbounded and the cost is the fuel and the walk.
/// </para>
/// <para>
/// It reads what it stands on every cycle rather than once. A player who mines out the block
/// underneath a drill has turned it off, which is a thing they can see and undo, and the
/// alternative is a drill that remembers a seam that is no longer there.
/// </para>
/// </remarks>
public sealed class Miner : IMachine, IPowered
{
    /// <summary>How long one block of ore takes to bring up, in seconds.</summary>
    /// <remarks>
    /// Four, against the nine a smelter takes over iron. A drill that outran what could be done
    /// with its output would make every line end in a pile, so it is deliberately slower than one
    /// smelter and faster than a player with a pick.
    /// </remarks>
    private const float CycleSeconds = 4f;

    private Smelting? _smelting;
    private double _powerShare;
    private bool _hasWork;
    private float _workedSeconds;
    private float _burnLeftSeconds;
    private float _burnTotalSeconds;

    /// <summary>What it burns.</summary>
    public Slots Fuel { get; } = new(1);

    /// <summary>What it has brought up and not had taken away.</summary>
    public Slots Output { get; } = new(1);

    /// <summary>Whether it is working.</summary>
    public bool IsLit => _burnLeftSeconds > 0f || _powerShare > 0.0;

    /// <summary>How much it draws, in watts, when it has a seam to work.</summary>
    public const double DrawWatts = 90.0;

    /// <inheritdoc />
    public double DemandWatts => _hasWork ? DrawWatts : 0.0;

    /// <inheritdoc />
    public void Supply(double share) => _powerShare = double.Clamp(share, 0.0, 1.0);

    /// <summary>How far through the current block it is, from 0 to 1.</summary>
    public float Progress => float.Clamp(_workedSeconds / CycleSeconds, 0f, 1f);

    /// <inheritdoc />
    public void Step(in MachineStep step)
    {
        _smelting = step.Smelting;

        // Taken and cleared in one go. A share is good for the step it was given for and no
        // longer: a machine whose cable is broken is never told anything again, so a share that
        // persisted would leave it running for ever on electricity that stopped arriving.
        var share = _powerShare;
        _powerShare = 0.0;

        // What is directly underneath. A drill is placed on top of a seam, which is how a player
        // can see what it is working without opening anything.
        var under = new int3(step.Position.X, step.Position.Y - 1, step.Position.Z);
        var ore = step.World.BlockAt(under.X, under.Y, under.Z);

        _hasWork = Yields(ore, step.World.Registry) && HasRoomFor(ore);

        if (!_hasWork)
        {
            _workedSeconds = 0f;
            _burnLeftSeconds = float.Max(0f, _burnLeftSeconds - (float)step.Seconds);
            return;
        }

        // Electricity first, fuel for the rest, exactly as a smelter does.
        var fromPower = step.Seconds * share;
        var wanted = step.Seconds - fromPower;
        var spent = (float)fromPower;

        if (wanted > 0.0)
        {
            if (_burnLeftSeconds <= 0f && !Light() && spent <= 0f)
                return;

            var burnt = float.Min(_burnLeftSeconds, (float)wanted);
            _burnLeftSeconds -= burnt;
            spent += burnt;
        }

        if (spent <= 0f)
            return;

        _workedSeconds += spent;

        if (_workedSeconds < CycleSeconds)
            return;

        Output.Add(ore, 1);
        _workedSeconds = 0f;
    }

    /// <inheritdoc />
    /// <remarks>
    /// It takes fuel and nothing else. A belt running past a row of drills carrying both coal and
    /// ore therefore fills them without any of them taking back what the one before produced.
    /// </remarks>
    public bool Offer(BlockId block) =>
        !block.IsAir && _smelting is not null && _smelting.BurnSeconds(block) > 0f && Fuel.Add(block, 1) > 0;

    /// <inheritdoc />
    public BlockId? TakeOut()
    {
        var brought = Output.At(0);
        if (brought.IsNothing)
            return null;

        Output.TakeOne(0);
        return brought.Block;
    }

    /// <inheritdoc />
    public IEnumerable<Stack> Contents()
    {
        foreach (var slots in (Slots[])[Fuel, Output])
        {
            var stack = slots.At(0);
            if (!stack.IsNothing)
                yield return stack;
        }
    }

    /// <summary>Sets the clocks back to where a save file said they were.</summary>
    /// <param name="burnLeftSeconds">How much fuel was still alight.</param>
    /// <param name="burnTotalSeconds">How long that lump lasts in total.</param>
    /// <param name="workedSeconds">How far into the current block it was.</param>
    public void Restore(float burnLeftSeconds, float burnTotalSeconds, float workedSeconds)
    {
        _burnLeftSeconds = float.Max(0f, burnLeftSeconds);
        _burnTotalSeconds = float.Max(0f, burnTotalSeconds);
        _workedSeconds = float.Max(0f, workedSeconds);
    }

    /// <summary>How much fuel is left, as a fraction, for a gauge.</summary>
    public float Burning =>
        _burnTotalSeconds > 0f ? float.Clamp(_burnLeftSeconds / _burnTotalSeconds, 0f, 1f) : 0f;

    /// <summary>How much fuel is still alight, in seconds, for a save file.</summary>
    public float BurnLeftSeconds => _burnLeftSeconds;

    /// <summary>How long the lump currently burning lasts in total, for a save file.</summary>
    public float BurnTotalSeconds => _burnTotalSeconds;

    /// <summary>How far into the current block it is, in seconds, for a save file.</summary>
    public float WorkedSeconds => _workedSeconds;

    /// <summary>Whether a block is something a drill brings up.</summary>
    /// <remarks>
    /// Anything that gives off no light, is solid, and takes longer than a second to break by
    /// hand — which is ore and stone and nothing else a player would stand a drill on. Stated as
    /// a property of the block rather than as a list, so a seam added tomorrow is drillable
    /// without this type being told.
    /// </remarks>
    private static bool Yields(BlockId block, BlockRegistry registry)
    {
        if (block.IsAir)
            return false;

        var type = registry[block];
        return type.Collides && type.BreakSeconds >= 1f && !float.IsPositiveInfinity(type.BreakSeconds);
    }

    private bool HasRoomFor(BlockId ore)
    {
        var made = Output.At(0);
        return made.IsNothing || (made.Block == ore && made.Count < Slots.StackSize);
    }

    private bool Light()
    {
        if (_smelting is null)
            return false;

        var fuel = Fuel.At(0);
        if (fuel.IsNothing)
            return false;

        var burns = _smelting.BurnSeconds(fuel.Block);
        if (burns <= 0f)
            return false;

        Fuel.TakeOne(0);
        _burnLeftSeconds = burns;
        _burnTotalSeconds = burns;
        return true;
    }
}
