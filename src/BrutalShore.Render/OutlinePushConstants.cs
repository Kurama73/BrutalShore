using System.Runtime.InteropServices;

namespace BrutalShore.Render;

/// <summary>Where an outlined box is, and what it looks like.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct OutlinePushConstants
{
    /// <summary>The block's lowest corner relative to the camera, in metres. w is spare.</summary>
    public readonly float4 Corner;

    /// <summary>rgb: the line's colour. w: how far the box is grown, in metres.</summary>
    public readonly float4 Color;

    /// <summary>Packs one outlined box for the shader.</summary>
    /// <param name="corner">The block's lowest corner relative to the camera, in metres.</param>
    /// <param name="color">The line's colour.</param>
    /// <param name="growth">How far to grow the box about its middle, in metres.</param>
    public OutlinePushConstants(float3 corner, float3 color, float growth)
    {
        Corner = new float4(corner, 0f);
        Color = new float4(color, growth);
    }
}
