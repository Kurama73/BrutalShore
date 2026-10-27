namespace Shorebound.Presentation.Camera;

/// <summary>A camera placement, as a command line can state it.</summary>
/// <param name="Position">Where the camera is, in world metres.</param>
/// <param name="Yaw">Which way it faces, in radians; zero looks along positive x.</param>
/// <param name="Pitch">How far above the horizon it looks, in radians.</param>
public sealed record Viewpoint(double3 Position, float Yaw, float Pitch);
