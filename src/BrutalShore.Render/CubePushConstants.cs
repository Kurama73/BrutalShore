using System.Runtime.InteropServices;

namespace BrutalShore.Render;

/// <summary>Where one loose cube is, how large it is, what colour it is and how far it has turned.</summary>
/// <remarks>
/// Everything that differs between two cubes, in thirty-two bytes. Push constants rather than an
/// instance buffer because the counts are small — the dozens of blocks a player has knocked loose
/// and not yet walked over — and a buffer rewritten every frame for thirty objects costs more in
/// bookkeeping than thirty draw calls cost in time.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct CubePushConstants
{
    /// <summary>xyz: the cube's middle relative to the camera, in metres. w: half its side.</summary>
    public readonly float4 Middle;

    /// <summary>rgb: its colour in sRGB. w: how far it is turned about its own upright axis.</summary>
    public readonly float4 Color;

    /// <summary>Packs one cube for the shader.</summary>
    /// <param name="middle">The cube's middle relative to the camera, in metres.</param>
    /// <param name="halfSize">Half the cube's side, in metres.</param>
    /// <param name="color">Its colour, in sRGB.</param>
    /// <param name="turnRadians">How far it is turned about its own upright axis.</param>
    public CubePushConstants(float3 middle, float halfSize, float3 color, float turnRadians)
    {
        Middle = new float4(middle, halfSize);
        Color = new float4(color, turnRadians);
    }
}
