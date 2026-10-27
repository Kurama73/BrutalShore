using System.Runtime.InteropServices;

namespace BrutalShore.Render;

/// <summary>Where one loose box is, how large it is, what colour it is and how far it has turned.</summary>
/// <remarks>
/// Everything that differs between two of them, in a hundred and twenty-eight bytes — exactly
/// what every Vulkan implementation is required to allow, and the reason a skin is four
/// rectangles rather than six. Push constants rather than
/// an instance buffer because the counts are small — the blocks a player has knocked loose and
/// not yet walked over, the limbs of the few animals in sight — and a buffer rewritten every
/// frame for thirty objects costs more in bookkeeping than thirty draw calls cost in time.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct CubePushConstants
{
    /// <summary>xyz: the box's middle relative to the camera, in metres. w: how far it is turned.</summary>
    public readonly float4 Middle;

    /// <summary>xyz: half its width, height and depth, in metres. w: how far it is nosed up.</summary>
    public readonly float4 HalfSize;

    /// <summary>rgb: its colour in sRGB. w: texture-array layer, or zero to use the colour.</summary>
    public readonly float4 Color;

    /// <summary>rgb: the sRGB tint multiplied into the sampled texture.</summary>
    public readonly float4 TextureTint;

    /// <summary>Where on the layer the two faces across the box's width are cut from.</summary>
    /// <remarks>xy is the corner and zw the size, both as a fraction of the layer.</remarks>
    public readonly float4 SideRect;

    /// <summary>Where its top and bottom are cut from.</summary>
    public readonly float4 TopRect;

    /// <summary>Where the face pointing the way it is turned is cut from.</summary>
    public readonly float4 FrontRect;

    /// <summary>Where the face pointing away is cut from.</summary>
    public readonly float4 BackRect;

    /// <summary>Packs one box for the shader.</summary>
    /// <param name="middle">The box's middle relative to the camera, in metres.</param>
    /// <param name="sizeMeters">Its width, height and depth, in metres.</param>
    /// <param name="color">Its colour, in sRGB.</param>
    /// <param name="turnRadians">How far it is turned about its own upright axis.</param>
    /// <param name="textureLayer">Texture-array layer, or zero to use the colour.</param>
    /// <param name="textureTint">The sRGB colour multiplied into the sampled texture, if any.</param>
    /// <param name="skin">Which part of the layer each face is cut from.</param>
    /// <param name="tiltRadians">How far it is nosed up about its own crosswise axis.</param>
    public CubePushConstants(
        float3 middle,
        float3 sizeMeters,
        float3 color,
        float turnRadians,
        byte textureLayer,
        float3? textureTint = null,
        CubeSkin? skin = null,
        float tiltRadians = 0f
    )
    {
        Middle = new float4(middle, turnRadians);
        HalfSize = new float4(sizeMeters * 0.5f, tiltRadians);
        Color = new float4(color, textureLayer);
        TextureTint = new float4(textureTint ?? new float3(1f, 1f, 1f), 0f);

        var cut = skin ?? CubeSkin.Whole;
        SideRect = cut.Side;
        TopRect = cut.Top;
        FrontRect = cut.Front;
        BackRect = cut.Back;
    }
}
