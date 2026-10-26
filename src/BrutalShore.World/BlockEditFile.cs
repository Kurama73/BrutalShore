using System.Text;

namespace BrutalShore.World;

/// <summary>What came back out of a file.</summary>
/// <param name="Applied">How many edits were read and recorded.</param>
/// <param name="Dropped">
/// How many named a block the registry no longer has. Reported rather than thrown because losing
/// a block type should cost the blocks of that type and nothing else.
/// </param>
public readonly record struct BlockEditReport(int Applied, int Dropped);

/// <summary>Reads and writes a <see cref="BlockEdits" /> as bytes.</summary>
/// <remarks>
/// <para>
/// Kept apart from <see cref="BlockEdits" /> so that the log knows nothing about files and the
/// format knows nothing about how edits are stored in memory. Either can be rewritten without
/// touching the other, and the format is the one of the two that has to stay readable.
/// </para>
/// <para>
/// Blocks are written by name, not by the <see cref="BlockId" /> they happen to hold. An id is an
/// index into the registry in registration order, so adding one block type to a game would turn
/// every saved stone block into dirt. A name costs a few bytes once, in a table the edits index
/// into, and survives the game growing.
/// </para>
/// </remarks>
public static class BlockEditFile
{
    /// <summary>What the first bytes of the file are, so that a wrong one is caught at once.</summary>
    private static ReadOnlySpan<byte> Magic => "BSEDITS"u8;

    /// <summary>The format's version, bumped whenever the layout below changes.</summary>
    private const byte Version = 1;

    /// <summary>Writes every edit to a stream.</summary>
    /// <param name="stream">Where to write; left open.</param>
    /// <param name="edits">The edits to write.</param>
    /// <param name="registry">The registry that gives each block its name.</param>
    public static void Write(Stream stream, BlockEdits edits, BlockRegistry registry)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(Magic);
        writer.Write(Version);

        // One pass to collect the names used, so the edits can index into a table rather than
        // repeat a string per block.
        var nameOf = new Dictionary<BlockId, int>();
        var names = new List<string>();
        var chunks = edits.TouchedChunks.ToArray();

        foreach (var chunk in chunks)
        {
            foreach (var (_, block) in edits.InChunk(chunk))
            {
                if (nameOf.ContainsKey(block))
                    continue;

                nameOf[block] = names.Count;
                names.Add(block.IsAir ? "air" : registry[block].Name);
            }
        }

        writer.Write(names.Count);
        foreach (var name in names)
            writer.Write(name);

        writer.Write(chunks.Length);
        foreach (var chunk in chunks)
        {
            var inChunk = edits.InChunk(chunk);

            writer.Write(chunk.X);
            writer.Write(chunk.Y);
            writer.Write(chunk.Z);
            writer.Write(inChunk.Count);

            foreach (var (index, block) in inChunk)
            {
                writer.Write(index);
                writer.Write(nameOf[block]);
            }
        }
    }

    /// <summary>Reads edits from a stream into a log.</summary>
    /// <param name="stream">Where to read from; left open.</param>
    /// <param name="into">The log to record into; existing edits are kept.</param>
    /// <param name="registry">The registry that resolves each name back to a block.</param>
    /// <returns>How many edits were taken and how many were dropped.</returns>
    /// <exception cref="InvalidDataException">The stream does not hold edits this version can read.</exception>
    public static BlockEditReport Read(Stream stream, BlockEdits into, BlockRegistry registry)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        Span<byte> magic = stackalloc byte[Magic.Length];
        if (reader.Read(magic) != magic.Length || !magic.SequenceEqual(Magic))
            throw new InvalidDataException("This is not a BrutalShore edit file.");

        var version = reader.ReadByte();
        if (version != Version)
            throw new InvalidDataException($"Edit file version {version} cannot be read; this build writes {Version}.");

        var nameCount = reader.ReadInt32();
        var resolved = new BlockId?[nameCount];
        for (var index = 0; index < nameCount; index++)
        {
            var name = reader.ReadString();
            resolved[index] = name == "air" ? BlockId.Air : registry.Find(name);
        }

        var applied = 0;
        var dropped = 0;

        var chunkCount = reader.ReadInt32();
        for (var chunk = 0; chunk < chunkCount; chunk++)
        {
            var coordinate = new int3(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            var editCount = reader.ReadInt32();

            for (var edit = 0; edit < editCount; edit++)
            {
                var index = reader.ReadInt32();
                var nameIndex = reader.ReadInt32();

                if ((uint)nameIndex >= resolved.Length || resolved[nameIndex] is not { } block)
                {
                    dropped++;
                    continue;
                }

                into.RecordAt(coordinate, index, block);
                applied++;
            }
        }

        return new BlockEditReport(applied, dropped);
    }
}
