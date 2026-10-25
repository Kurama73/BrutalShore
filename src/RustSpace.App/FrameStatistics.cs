namespace RustSpace.App;

/// <summary>What the overlay reports about one frame.</summary>
/// <remarks>
/// A snapshot passed by value rather than the live objects, so the overlay cannot reach into the
/// world or the renderer and change anything while it draws. It also keeps the overlay's
/// dependencies to one type instead of five.
/// </remarks>
/// <param name="DeviceName">The GPU in use.</param>
/// <param name="FramesPerSecond">The smoothed frame rate.</param>
/// <param name="FrameMilliseconds">How long the last frame took.</param>
/// <param name="Position">Where the camera is, in world metres.</param>
/// <param name="Chunk">Which chunk that is.</param>
/// <param name="Seed">The world seed.</param>
/// <param name="ViewRadiusChunks">How far chunks are kept loaded.</param>
/// <param name="LoadedChunks">How many chunks are resident.</param>
/// <param name="MeshedChunks">How many of them have geometry on the GPU.</param>
/// <param name="PendingChunks">How many are dirty and waiting to be meshed.</param>
/// <param name="DrawnChunks">How many survived frustum culling.</param>
/// <param name="DrawnTriangles">How many triangles those were.</param>
/// <param name="ResidentBytes">How much vertex and index data is on the GPU.</param>
public readonly record struct FrameStatistics(
    string DeviceName,
    double FramesPerSecond,
    double FrameMilliseconds,
    double3 Position,
    int3 Chunk,
    int Seed,
    int ViewRadiusChunks,
    int LoadedChunks,
    int MeshedChunks,
    int PendingChunks,
    int DrawnChunks,
    int DrawnTriangles,
    long ResidentBytes
);
