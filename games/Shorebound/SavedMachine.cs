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
}
