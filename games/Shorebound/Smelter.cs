using BrutalShore.Voxel;

namespace Shorebound;

/// <summary>One smelter standing in the world: what is in it, and how far along it is.</summary>
/// <remarks>
/// <para>
/// Three slots and two clocks. The slots are what goes in, what it burns and what comes out; the
/// clocks are how much fuel is still alight and how far through the current block it is. Keeping
/// the two apart is what lets a smelter carry on burning between jobs rather than throwing away
/// the rest of a lump of coal every time its input runs out — which is the behaviour a player
/// notices immediately and has no way to explain.
/// </para>
/// <para>
/// It does not know where it is. Position belongs to <see cref="Machines" />, so that a smelter
/// can be tested by stepping it rather than by building a world around it.
/// </para>
/// </remarks>
public sealed class Smelter
{
    /// <summary>What is waiting to be melted.</summary>
    public Slots Input { get; } = new(1);

    /// <summary>What is waiting to be burnt.</summary>
    public Slots Fuel { get; } = new(1);

    /// <summary>What has been made and not yet taken out.</summary>
    public Slots Output { get; } = new(1);

    /// <summary>How many seconds of fuel are still alight.</summary>
    public float BurnLeftSeconds { get; private set; }

    /// <summary>How long the lump currently burning lasts in total, for drawing a gauge.</summary>
    public float BurnTotalSeconds { get; private set; }

    /// <summary>How many seconds of work have gone into the block being melted.</summary>
    public float WorkedSeconds { get; private set; }

    /// <summary>Whether something is alight.</summary>
    public bool IsLit => BurnLeftSeconds > 0f;

    /// <summary>How far through the current block it is, from 0 to 1.</summary>
    /// <param name="smelting">The tables, which say how long the block in the input takes.</param>
    /// <returns>The fraction done, or zero when there is nothing to do.</returns>
    public float Progress(Smelting smelting)
    {
        var job = Job(smelting);
        return job is null || job.Value.Seconds <= 0f ? 0f : float.Clamp(WorkedSeconds / job.Value.Seconds, 0f, 1f);
    }

    /// <summary>Runs the smelter for a slice of time.</summary>
    /// <param name="smelting">The tables that say what melts and what burns.</param>
    /// <param name="seconds">How long has passed.</param>
    /// <remarks>
    /// <para>
    /// Fuel is only lit when there is something to melt <em>and</em> somewhere to put the result.
    /// Both halves matter: a smelter that burned coal with an empty input is a smelter that eats
    /// a player's fuel while they are away, and one that burned with a full output throws the
    /// work away at the end of it.
    /// </para>
    /// <para>
    /// Progress is lost when the input changes but not when the fire goes out, so a smelter that
    /// runs out of coal halfway through picks up where it left off once it is fed. The opposite
    /// would make an unattended smelter a trap.
    /// </para>
    /// </remarks>
    public void Step(Smelting smelting, double seconds)
    {
        var job = Job(smelting);

        if (job is null)
        {
            // Nothing to do. Whatever is alight stays alight — it is already burning — but no new
            // fuel is lit, and the part-finished work is thrown away because the block it belonged
            // to has gone.
            WorkedSeconds = 0f;
            BurnLeftSeconds = float.Max(0f, BurnLeftSeconds - (float)seconds);
            return;
        }

        if (BurnLeftSeconds <= 0f && !Light(smelting))
            return;

        var spent = float.Min(BurnLeftSeconds, (float)seconds);
        BurnLeftSeconds -= spent;
        WorkedSeconds += spent;

        if (WorkedSeconds < job.Value.Seconds)
            return;

        Input.TakeOne(0);
        Output.Add(job.Value.Result, 1);
        WorkedSeconds = 0f;
    }

    /// <summary>Everything in it, for when the block is broken and has to give it all back.</summary>
    /// <returns>Each non-empty slot's stack.</returns>
    public IEnumerable<Stack> Contents()
    {
        foreach (var slots in (Slots[])[Input, Fuel, Output])
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
        BurnLeftSeconds = float.Max(0f, burnLeftSeconds);
        BurnTotalSeconds = float.Max(0f, burnTotalSeconds);
        WorkedSeconds = float.Max(0f, workedSeconds);
    }

    /// <summary>The job the input slot is asking for, if there is room for its result.</summary>
    private (BlockId Result, float Seconds)? Job(Smelting smelting)
    {
        var waiting = Input.At(0);
        if (waiting.IsNothing || smelting.Melts(waiting.Block) is not { } melt)
            return null;

        var made = Output.At(0);
        var roomForIt =
            made.IsNothing || (made.Block == melt.Result && made.Count < Slots.StackSize);

        return roomForIt ? melt : null;
    }

    /// <summary>Burns one of whatever is in the fuel slot.</summary>
    private bool Light(Smelting smelting)
    {
        var fuel = Fuel.At(0);
        if (fuel.IsNothing)
            return false;

        var burns = smelting.BurnSeconds(fuel.Block);
        if (burns <= 0f)
            return false;

        Fuel.TakeOne(0);
        BurnLeftSeconds = burns;
        BurnTotalSeconds = burns;
        return true;
    }
}
