namespace Shorebound.Domain.Player;

/// <summary>What the player is asking for this step, before the world gets a say.</summary>
/// <remarks>
/// Sits between reading the keyboard and moving a body, so that the movement can be reasoned
/// about — and changed — without a key name anywhere near it.
/// </remarks>
public readonly record struct PlayerIntent
{
    /// <summary>Forwards and backwards, from -1 to 1.</summary>
    public required double Forward { get; init; }

    /// <summary>Right and left, from -1 to 1.</summary>
    public required double Right { get; init; }

    /// <summary>Whether the jump key is held, which also means rise while flying.</summary>
    public required bool Jump { get; init; }

    /// <summary>Whether the sink key is held, which only means anything while flying.</summary>
    public required bool Sink { get; init; }

    /// <summary>Whether to move at the sprinting speed.</summary>
    public required bool Sprint { get; init; }

    /// <summary>Whether to crouch: slower, lower, and unable to walk off an edge.</summary>
    /// <remarks>
    /// The same key as sinking while flying, because the two never apply at once and a player
    /// holding "down" means one thing: go lower, or at least do not go higher.
    /// </remarks>
    public required bool Crouch { get; init; }

    /// <summary>Nothing held.</summary>
    public static PlayerIntent Still =>
        new()
        {
            Forward = 0.0,
            Right = 0.0,
            Jump = false,
            Sink = false,
            Sprint = false,
            Crouch = false,
        };
}
