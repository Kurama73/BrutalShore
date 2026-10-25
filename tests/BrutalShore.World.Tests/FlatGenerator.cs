namespace BrutalShore.World.Tests;

/// <summary>
/// A world that is stone up to a given altitude and air above it: the second implementation of
/// <see cref="IChunkGenerator"/>, and the one that makes residency and editing testable without a
/// test depending on what terrain happens to have put at a coordinate.
/// </summary>
internal sealed class FlatGenerator(BlockId ground, int surfaceHeight) : IChunkGenerator
{
    public void Generate(int3 chunkCoordinate, Chunk into)
    {
        var originY = chunkCoordinate.Y * Chunk.Size;
        if (originY > surfaceHeight)
            return;

        var blocks = into.BeginBulkWrite();
        for (var y = 0; y < Chunk.Size; y++)
        {
            if (originY + y > surfaceHeight)
                break;

            for (var z = 0; z < Chunk.Size; z++)
                for (var x = 0; x < Chunk.Size; x++)
                    blocks[Chunk.IndexOf(x, y, z)] = ground;
        }

        into.EndBulkWrite();
    }
}
