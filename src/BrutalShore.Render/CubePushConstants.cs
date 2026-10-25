using System.Runtime.InteropServices;

namespace BrutalShore.Render;

/// <summary>Where one loose box is, how large it is, what colour it is and how far it has turned.</summary>
/// <remarks>
/// Everything that differs between two of them, in forty-eight bytes. Push constants rather than
/// an instance buffer because the counts are small — the blocks a player has knocked loose and
/// not yet walked over, the limbs of the few animals in sight — and a buffer rewritten every
/// frame for thirty objects costs more in bookkeeping than thirty draw calls cost in time.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct CubePushConstants
{
    /// <summary>xyz: the box's middle relative to the camera, in metres. w: how far it is turned.</summary>
    public readonly float4 Middle;

    /// <summary>xyz: half its width, height and depth, in metres. w: spare.</summary>
    public readonly float4 HalfSize;

    /// <summary>rgb: its colour in sRGB. w: spare.</summary>
    public readonly float4 Color;

    /// <summary>Packs one box for the shader.</summary>
    /// <param name="middle">The box's middle relative to the camera, in metres.</param>
    /// <param name="sizeMeters">Its width, height and depth, in metres.</param>
    /// <param name="color">Its colour, in sRGB.</param>
    /// <param name="turnRadians">How far it is turned about its own upright axis.</param>
    public CubePushConstants(float3 middle, float3 sizeMeters, float3 color, float turnRadians)
    {
        Middle = new float4(middle, turnRadians);
        HalfSize = new float4(sizeMeters * 0.5f, 0f);
        Color = new float4(color, 0f);
    }
}
