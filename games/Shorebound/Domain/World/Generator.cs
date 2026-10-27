using BrutalShore.Voxel;
using Shorebound.Domain.Player;
using Shorebound.Domain.Production;

namespace Shorebound.Domain.World;

/// <summary>A burner that turns fuel into electricity for everything wired to it.</summary>
/// <remarks>
/// <para>
/// The one machine in the game that still has to be fed by hand or by belt once there is a grid,
/// which is the whole point: it concentrates the hauling. Twelve smelters each wanting coal is
/// twelve places to walk to; twelve smelters on a wire and one generator is one.
/// </para>
/// <para>
/// It burns only what was actually drawn. A generator running flat out into a grid with nothing
/// switched on would eat a player's coal while they slept, and they would have no way to see it
/// happening — which is the worst kind of cost a game can charge.
/// </para>
/// </remarks>
public sealed class Generator : IMachine
{
    /// <summary>The most it can put out, in watts.</summary>
    /// <remarks>
    /// Enough for four machines at full draw. Small on purpose: a factory's second generator
    /// should arrive early, so that the shape of the thing — more production needs more power —
    /// is learned while it is still cheap to learn.
    /// </remarks>
    public const double CapacityWatts = 360.0;

    private Smelting? _smelting;
    private float _burnLeftSeconds;
    private float _burnTotalSeconds;

    /// <summary>What it burns.</summary>
    public Slots Fuel { get; } = new(1);

    /// <summary>Whether it is alight.</summary>
    public bool IsLit => _burnLeftSeconds > 0f;

    /// <summary>How much fuel is left, as a fraction, for a gauge.</summary>
    public float Burning =>
        _burnTotalSeconds > 0f ? float.Clamp(_burnLeftSeconds / _burnTotalSeconds, 0f, 1f) : 0f;

    /// <summary>How much fuel is still alight, in seconds, for a save file.</summary>
    public float BurnLeftSeconds => _burnLeftSeconds;

    /// <summary>How long the lump currently burning lasts in total, for a save file.</summary>
    public float BurnTotalSeconds => _burnTotalSeconds;

    /// <summary>How much it could put out right now, in watts.</summary>
    /// <remarks>
    /// Its full capacity if it is alight or could be lit, and nothing at all otherwise. Whether
    /// it <em>will</em> is the grid's decision, because only the grid knows what is being asked
    /// for.
    /// </remarks>
    public double AvailableWatts => IsLit || CanLight ? CapacityWatts : 0.0;

    /// <inheritdoc />
    public void Step(in MachineStep step) => _smelting = step.Smelting;

    /// <summary>Takes a share of its capacity out of it, burning fuel for exactly that much.</summary>
    /// <param name="share">How much of its capacity was drawn, from 0 to 1.</param>
    /// <param name="seconds">How long for.</param>
    /// <remarks>
    /// Fuel is spent in proportion to the share drawn. A lump of coal that lasts thirty-two
    /// seconds at full load therefore lasts a minute at half, which is the behaviour anyone
    /// expects of a fire that is barely working.
    /// </remarks>
    public void Draw(double share, double seconds)
    {
        if (share <= 0.0)
            return;

        var wanted = (float)(share * seconds);

        while (wanted > 0f)
        {
            if (_burnLeftSeconds <= 0f && !Light())
                return;

            var spent = float.Min(_burnLeftSeconds, wanted);
            _burnLeftSeconds -= spent;
            wanted -= spent;
        }
    }

    /// <inheritdoc />
    public bool Offer(BlockId block) =>
        !block.IsAir && _smelting is not null && _smelting.BurnSeconds(block) > 0f && Fuel.Add(block, 1) > 0;

    /// <inheritdoc />
    public BlockId? TakeOut() => null;

    /// <inheritdoc />
    public IEnumerable<Stack> Contents()
    {
        var stack = Fuel.At(0);
        if (!stack.IsNothing)
            yield return stack;
    }

    /// <summary>Sets the clocks back to where a save file said they were.</summary>
    /// <param name="burnLeftSeconds">How much fuel was still alight.</param>
    /// <param name="burnTotalSeconds">How long that lump lasts in total.</param>
    public void Restore(float burnLeftSeconds, float burnTotalSeconds)
    {
        _burnLeftSeconds = float.Max(0f, burnLeftSeconds);
        _burnTotalSeconds = float.Max(0f, burnTotalSeconds);
    }

    private bool CanLight =>
        _smelting is not null && !Fuel.At(0).IsNothing && _smelting.BurnSeconds(Fuel.At(0).Block) > 0f;

    private bool Light()
    {
        if (!CanLight || _smelting is null)
            return false;

        var burns = _smelting.BurnSeconds(Fuel.At(0).Block);
        Fuel.TakeOne(0);
        _burnLeftSeconds = burns;
        _burnTotalSeconds = burns;
        return true;
    }
}
