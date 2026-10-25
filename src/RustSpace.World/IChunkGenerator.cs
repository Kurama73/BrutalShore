namespace RustSpace.World;

/// <summary>
/// Fills a chunk with whatever is there, given only where it is.
/// </summary>
/// <remarks>
/// <para>
/// The signature is the design. A generator is handed a coordinate and a chunk to fill, and may
/// read nothing else — no neighbouring chunk, no shared state, no previous call. That is what makes
/// the world regenerable rather than stored: a chunk nobody has edited need not be saved, because
/// asking for it again gives the same answer, in any order, from any thread, in a later session.
/// </para>
/// <para>
/// A feature that spans a chunk boundary — a tree whose leaves cross into the next chunk, a cave
/// that runs for a hundred blocks — is still possible under this rule, and the way it is done is
/// worth knowing before it is needed: the generator computes the feature from a coordinate it can
/// derive on both sides, and writes only the part that falls inside the chunk it was given. Both
/// chunks agree because both did the same arithmetic, not because either consulted the other.
/// </para>
/// </remarks>
public interface IChunkGenerator
{
    /// <summary>
    /// Fills <paramref name="into"/> with the chunk at <paramref name="chunkCoordinate"/>, in chunk
    /// units. The chunk arrives all air.
    /// </summary>
    void Generate(int3 chunkCoordinate, Chunk into);
}
