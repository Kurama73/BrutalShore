using System.Text;
using BrutalShore.Voxel;
using BrutalShore.World;

namespace Shorebound;

/// <summary>Everything about a Shorebound world that is not derivable from its seed.</summary>
/// <remarks>
/// <para>
/// Which is to say: the seed, where the player is standing, what time it is, and every block they
/// have changed. The terrain itself is never written — it is generated from the seed, so storing
/// it would be storing a function's output beside its input.
/// </para>
/// <para>
/// A save is therefore the size of what was done to the world rather than the size of what was
/// walked through. An untouched world of any size saves in a few dozen bytes.
/// </para>
/// </remarks>
public sealed record SaveFile
{
    /// <summary>What the first bytes of the file are, so a wrong one is caught at once.</summary>
    private static ReadOnlySpan<byte> Magic => "SHOREBOUND"u8;

    /// <summary>The format's version, bumped whenever the layout below changes.</summary>
    /// <remarks>
    /// Version 2 added what the player is carrying and version 3 added the machines standing in
    /// the world. Older files still read, with an empty row and no machines: the blocks in
    /// someone's hand are worth less than their world, and refusing to open a world because a
    /// list that did not exist is missing from it would be the wrong trade. A smelter that comes
    /// back empty is the worst that can happen, and only to a world saved before smelters.
    /// </remarks>
    private const byte Version = 3;

    /// <summary>The world's seed, which is the world.</summary>
    public required int Seed { get; init; }

    /// <summary>Where the middle of the player's feet is, in world metres.</summary>
    public required double3 FeetMeters { get; init; }

    /// <summary>Which way the player is facing, in radians.</summary>
    public required float Yaw { get; init; }

    /// <summary>How far up or down they are looking, in radians.</summary>
    public required float Pitch { get; init; }

    /// <summary>Whether they were flying.</summary>
    public required bool IsFlying { get; init; }

    /// <summary>How many days have passed, the fraction being the time of day.</summary>
    public required double DayFraction { get; init; }

    /// <summary>Which slot of the row was in hand.</summary>
    public required int RowIndex { get; init; }

    /// <summary>What was in each slot: a block's name and how many of it, in slot order.</summary>
    /// <remarks>
    /// By name rather than by id, exactly as the edits are and for the same reason: an id is a
    /// position in a registry, and a world saved against one registry has to open against a later
    /// one. An empty slot is an empty name.
    /// </remarks>
    public required IReadOnlyList<(string Block, int Count)> Carried { get; init; }

    /// <summary>Every machine standing in the world, and what was inside it.</summary>
    /// <remarks>
    /// A machine is not derivable from the seed and is not a block edit either — the block it
    /// stands in is one, and what is inside it is this. By name, like everything else written
    /// here, and with its clocks, so that a smelter left running is still running when the world
    /// is opened again rather than quietly starting over.
    /// </remarks>
    public required IReadOnlyList<SavedMachine> Machines { get; init; }

    /// <summary>Writes a world to a file, replacing whatever was there.</summary>
    /// <param name="path">Where to write.</param>
    /// <param name="edits">The blocks the player has changed.</param>
    /// <param name="registry">The registry that gives each block its name.</param>
    public void Write(string path, BlockEdits edits, BlockRegistry registry)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Written beside the real file and moved into place, so that a crash halfway through
        // leaves the previous save intact rather than a half-written one.
        var temporary = path + ".writing";

        using (var stream = File.Create(temporary))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(Seed);
            writer.Write(FeetMeters.X);
            writer.Write(FeetMeters.Y);
            writer.Write(FeetMeters.Z);
            writer.Write(Yaw);
            writer.Write(Pitch);
            writer.Write(IsFlying);
            writer.Write(DayFraction);
            writer.Write(RowIndex);

            writer.Write(Carried.Count);
            foreach (var (block, count) in Carried)
            {
                writer.Write(block);
                writer.Write(count);
            }

            writer.Write(Machines.Count);
            foreach (var machine in Machines)
            {
                writer.Write(machine.Position.X);
                writer.Write(machine.Position.Y);
                writer.Write(machine.Position.Z);
                writer.Write(machine.BurnLeftSeconds);
                writer.Write(machine.BurnTotalSeconds);
                writer.Write(machine.WorkedSeconds);

                foreach (var (block, count) in (ReadOnlySpan<(string Block, int Count)>)
                    [machine.Input, machine.Fuel, machine.Output])
                {
                    writer.Write(block);
                    writer.Write(count);
                }
            }

            writer.Flush();

            BlockEditFile.Write(stream, edits, registry);
        }

        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Reads a world back from a file.</summary>
    /// <param name="path">Where to read from.</param>
    /// <param name="into">The edit log to fill.</param>
    /// <param name="registry">The registry that resolves each block name.</param>
    /// <returns>The saved world, and what became of its edits.</returns>
    /// <exception cref="InvalidDataException">The file does not hold a world this build can read.</exception>
    public static (SaveFile Save, BlockEditReport Edits) Read(string path, BlockEdits into, BlockRegistry registry)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        Span<byte> magic = stackalloc byte[Magic.Length];
        if (reader.Read(magic) != magic.Length || !magic.SequenceEqual(Magic))
            throw new InvalidDataException($"'{path}' is not a Shorebound world.");

        var version = reader.ReadByte();
        if (version is 0 or > Version)
            throw new InvalidDataException($"World version {version} cannot be read; this build writes {Version}.");

        var save = new SaveFile
        {
            Seed = reader.ReadInt32(),
            FeetMeters = new double3(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble()),
            Yaw = reader.ReadSingle(),
            Pitch = reader.ReadSingle(),
            IsFlying = reader.ReadBoolean(),
            DayFraction = reader.ReadDouble(),
            RowIndex = reader.ReadInt32(),
            Carried = version >= 2 ? ReadCarried(reader) : [],
            Machines = version >= 3 ? ReadMachines(reader) : [],
        };

        return (save, BlockEditFile.Read(stream, into, registry));
    }

    private static List<SavedMachine> ReadMachines(BinaryReader reader)
    {
        var count = reader.ReadInt32();

        // The same guard as the carried slots, for the same reason: a count read from a file
        // decides how many times this loop runs. The bound is generous — a player who has built
        // a hundred thousand smelters has earned the right to load them — and still finite.
        if (count is < 0 or > 100_000)
            throw new InvalidDataException($"A world claiming {count} machines is not one.");

        var machines = new List<SavedMachine>(count);
        for (var index = 0; index < count; index++)
        {
            var position = new int3(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            var burnLeft = reader.ReadSingle();
            var burnTotal = reader.ReadSingle();
            var worked = reader.ReadSingle();

            machines.Add(
                new SavedMachine
                {
                    Position = position,
                    BurnLeftSeconds = burnLeft,
                    BurnTotalSeconds = burnTotal,
                    WorkedSeconds = worked,
                    Input = (reader.ReadString(), reader.ReadInt32()),
                    Fuel = (reader.ReadString(), reader.ReadInt32()),
                    Output = (reader.ReadString(), reader.ReadInt32()),
                }
            );
        }

        return machines;
    }

    private static List<(string Block, int Count)> ReadCarried(BinaryReader reader)
    {
        var slots = reader.ReadInt32();

        // A count read from a file decides how many times this loop runs, so a corrupt one is a
        // hang or an allocation of gigabytes. Bounding it is cheaper than trusting the file.
        if (slots is < 0 or > 256)
            throw new InvalidDataException($"A world claiming {slots} carried slots is not one.");

        var carried = new List<(string, int)>(slots);
        for (var slot = 0; slot < slots; slot++)
            carried.Add((reader.ReadString(), reader.ReadInt32()));

        return carried;
    }
}
