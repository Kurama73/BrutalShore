namespace Shorebound.Domain.Player;

/// <summary>How much the player can take, and what happens when they cannot take any more.</summary>
/// <remarks>
/// <para>
/// Kept apart from <see cref="Player" /> because it is about a different thing. The player type
/// is movement — acceleration, collision, what it means to crouch — and none of that has an
/// opinion about being bitten. Separating them means health can be reasoned about, and tested,
/// without a world to stand in.
/// </para>
/// <para>
/// Healing is slow and stops the moment anything lands. That is what makes retreating a real
/// move: a player who can back off into a corridor and be whole again in two seconds has no
/// reason to ever build a wall.
/// </para>
/// </remarks>
public sealed class Vitality
{
    /// <summary>How much health a whole player has.</summary>
    /// <remarks>
    /// Twenty, so that a single bite can be worth a visible fraction without the number needing
    /// decimals, and so that the bar can be read as a count of bites rather than a percentage.
    /// </remarks>
    public const float Whole = 20f;

    /// <summary>How much health comes back a second once nothing has landed for a while.</summary>
    private const float HealPerSecond = 0.9f;

    /// <summary>How long after being hurt healing starts again, in seconds.</summary>
    private const float CalmSeconds = 6f;

    /// <summary>How long after being hurt the same source may hurt again, in seconds.</summary>
    /// <remarks>
    /// What stops a creature standing inside the player and doing its damage once a frame. The
    /// window is shared rather than per creature on purpose: a horde should be dangerous because
    /// it surrounds and cuts off, not because six of them multiply into a frame-rate's worth of
    /// damage.
    /// </remarks>
    public const float GraceSeconds = 0.6f;

    private float _sinceHurtSeconds = CalmSeconds;

    /// <summary>How much health is left, from zero to <see cref="Whole" />.</summary>
    public float Health { get; private set; } = Whole;

    /// <summary>How much of the player is left, from 0 to 1, for a bar.</summary>
    public float Fraction => float.Clamp(Health / Whole, 0f, 1f);

    /// <summary>Whether they are still standing.</summary>
    public bool IsAlive => Health > 0f;

    /// <summary>Whether anything may land a hit right now.</summary>
    public bool CanBeHurt => _sinceHurtSeconds >= GraceSeconds;

    /// <summary>Whether something landed recently enough to still be worth showing.</summary>
    public bool WasHurtRecently => _sinceHurtSeconds < 1.2f;

    /// <summary>Takes health away, if the grace window has passed.</summary>
    /// <param name="amount">How much to take.</param>
    /// <returns>Whether it landed.</returns>
    public bool Hurt(float amount)
    {
        if (amount <= 0f || !CanBeHurt || !IsAlive)
            return false;

        Health = float.Max(0f, Health - amount);
        _sinceHurtSeconds = 0f;
        return true;
    }

    /// <summary>Puts a player back together, as respawning does.</summary>
    public void Restore()
    {
        Health = Whole;
        _sinceHurtSeconds = CalmSeconds;
    }

    /// <summary>Sets health to what a save file recorded.</summary>
    /// <param name="health">How much was left.</param>
    /// <remarks>
    /// Clamped, and never to zero: a world that opened with the player already dead would show
    /// them the death screen before they had touched anything.
    /// </remarks>
    public void Restore(float health)
    {
        Health = float.Clamp(health, 1f, Whole);
        _sinceHurtSeconds = CalmSeconds;
    }

    /// <summary>Lets time pass, healing once nothing has landed for a while.</summary>
    /// <param name="seconds">How long has passed.</param>
    public void Step(double seconds)
    {
        _sinceHurtSeconds += (float)seconds;

        if (!IsAlive || _sinceHurtSeconds < CalmSeconds)
            return;

        Health = float.Min(Whole, Health + (HealPerSecond * (float)seconds));
    }
}
