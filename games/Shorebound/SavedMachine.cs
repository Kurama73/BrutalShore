using BrutalShore.World;

namespace Shorebound;

/// <summary>One machine as a save file holds it: where it stands and what was inside.</summary>
/// <remarks>
/// A flat record rather than the machine itself, for the same reason the edits are written by
/// block name: what goes in a file has to be readable by a build that has changed since, and a
/// type with behaviour in it is a type that will change. This has no behaviour and will not.
/// </remarks>
public readonly record struct SavedMachine
{
    /// <summary>The block coordinate it stands at.</summary>
    public required int3 Position { get; init; }

    /// <summary>Which kind of machine it is.</summary>
    /// <remarks>
    /// Written as a string rather than a number, like everything else in a save file, so that a
    /// kind added in the middle of the list does not renumber the ones after it.
    /// </remarks>
    public required string Kind { get; init; }

    /// <summary>Which way it is turned, for the kinds that have a direction.</summary>
    public Facing Facing { get; init; }

    /// <summary>What a belt is carrying, by block name and how far along it has got.</summary>
    /// <remarks>
    /// Required rather than defaulted, because a struct with a field initialiser needs a
    /// constructor and a record this plain is better off with neither.
    /// </remarks>
    public required IReadOnlyList<(string Block, double Along)> Carrying { get; init; }

    /// <summary>How many seconds of fuel were still alight.</summary>
    public required float BurnLeftSeconds { get; init; }

    /// <summary>How long the lump then burning lasts in total.</summary>
    public required float BurnTotalSeconds { get; init; }

    /// <summary>How far into the block being melted it had got, in seconds.</summary>
    public required float WorkedSeconds { get; init; }

    /// <summary>What was waiting to be melted, by block name.</summary>
    public required (string Block, int Count) Input { get; init; }

    /// <summary>What was waiting to be burnt.</summary>
    public required (string Block, int Count) Fuel { get; init; }

    /// <summary>What had been made and not taken out.</summary>
    public required (string Block, int Count) Output { get; init; }

    /// <summary>The name this file writes for a smelter.</summary>
    public const string Smelter = "smelter";

    /// <summary>The name this file writes for a conveyor.</summary>
    public const string Conveyor = "conveyor";

    /// <summary>The name this file writes for a miner.</summary>
    public const string Miner = "miner";

    /// <summary>The name this file writes for a cable.</summary>
    public const string Cable = "cable";

    /// <summary>The name this file writes for a generator.</summary>
    public const string Generator = "generator";

    /// <summary>The name this file writes for a turret.</summary>
    public const string Turret = "turret";

    /// <summary>The name this file writes for a lift.</summary>
    public const string Lift = "lift";
}
