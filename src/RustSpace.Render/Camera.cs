namespace RustSpace.Render;

/// <summary>A free-flying first-person camera, positioned in world metres.</summary>
/// <remarks>
/// <para>
/// The position is <see cref="double3" /> and the matrices are <c>float</c>, because the camera is
/// exactly where the two meet. It is also the render-space origin: the view matrix always looks
/// from zero, and every chunk is translated by its offset <em>from the camera</em> before being
/// narrowed to f32. That is the whole floating-origin scheme, and it costs one subtraction per
/// chunk per frame — far away geometry keeps the same precision as geometry underfoot, so there is
/// no distance at which vertices start to shimmer.
/// </para>
/// <para>
/// Yaw and pitch rather than a quaternion: a first-person camera has no roll, and storing the two
/// angles makes the pitch clamp that keeps it that way trivial instead of a renormalisation
/// problem.
/// </para>
/// </remarks>
public sealed class Camera
{
    /// <summary>How close to straight up or down the pitch may get, in radians.</summary>
    /// <remarks>
    /// Looking exactly along the up axis leaves the view matrix's cross product with no direction
    /// to pick, and the view rolls as it crosses. A hundredth of a radian is far below what anyone
    /// notices and keeps the basis well conditioned.
    /// </remarks>
    private const float PitchLimit = float.Pi / 2f - 0.01f;

    /// <summary>Where the camera is, in world metres.</summary>
    public double3 Position { get; set; }

    /// <summary>Rotation around the vertical axis, in radians; zero looks along positive x.</summary>
    public float Yaw { get; private set; }

    /// <summary>Rotation above the horizon, in radians, clamped short of the poles.</summary>
    public float Pitch { get; private set; }

    /// <summary>Vertical field of view, in radians.</summary>
    public float FieldOfView { get; set; } = 1.3089969f;

    /// <summary>The near plane, in metres.</summary>
    public float NearPlane { get; set; } = 0.05f;

    /// <summary>The far plane, in metres.</summary>
    public float FarPlane { get; set; } = 1024f;

    /// <summary>The unit vector the camera is looking along.</summary>
    public float3 Forward
    {
        get
        {
            var (sinPitch, cosPitch) = float.SinCos(Pitch);
            var (sinYaw, cosYaw) = float.SinCos(Yaw);
            return new float3(cosPitch * cosYaw, sinPitch, cosPitch * sinYaw);
        }
    }

    /// <summary>The unit vector pointing to the camera's right, level with the horizon.</summary>
    public float3 Right
    {
        get
        {
            var (sinYaw, cosYaw) = float.SinCos(Yaw);
            return new float3(-sinYaw, 0f, cosYaw);
        }
    }

    /// <summary>Turns the camera by a mouse delta in pixels.</summary>
    /// <param name="deltaX">Horizontal movement; positive turns right.</param>
    /// <param name="deltaY">Vertical movement; positive, as the mouse reports it, looks down.</param>
    /// <param name="radiansPerPixel">Sensitivity.</param>
    public void Look(double deltaX, double deltaY, float radiansPerPixel)
    {
        Yaw += (float)deltaX * radiansPerPixel;
        Pitch = float.Clamp(Pitch - (float)deltaY * radiansPerPixel, -PitchLimit, PitchLimit);

        // Wrapping keeps the angle small no matter how long the session runs; a yaw of a few
        // hundred thousand radians would start losing precision in the sine.
        Yaw = float.Ieee754Remainder(Yaw, float.Tau);
    }

    /// <summary>
    /// Moves the camera along its own axes.
    /// </summary>
    /// <param name="forward">Metres along <see cref="Forward" />, flattened to the horizon.</param>
    /// <param name="right">Metres along <see cref="Right" />.</param>
    /// <param name="up">Metres straight up.</param>
    /// <remarks>
    /// Forward is flattened because a walk-style control that pitched into the ground when the
    /// player looks down is unusable; climbing is what the <paramref name="up" /> axis is for.
    /// </remarks>
    public void Move(double forward, double right, double up)
    {
        var (sinYaw, cosYaw) = float.SinCos(Yaw);
        Position = new double3(
            Position.X + (forward * cosYaw) + (right * -sinYaw),
            Position.Y + up,
            Position.Z + (forward * sinYaw) + (right * cosYaw)
        );
    }

    /// <summary>The combined view and projection matrix, with the camera at the origin.</summary>
    /// <param name="aspectRatio">Framebuffer width divided by height.</param>
    /// <remarks>
    /// The y scale of -1 is the Vulkan correction: clip space has y pointing down, while every
    /// perspective matrix in <see cref="float4x4" /> is built for an API where it points up.
    /// Folding it in here means no shader has to know. It does interact with
    /// <see cref="VoxelPipeline" />'s front face, since negating y reverses the apparent winding
    /// of every triangle — change one and the other has to be checked against a picture.
    /// </remarks>
    public float4x4 ViewProjection(float aspectRatio)
    {
        var view = float4x4.CreateLookTo(float3.Zero, Forward, new float3(0f, 1f, 0f));
        var projection = float4x4.CreatePerspectiveFieldOfView(FieldOfView, aspectRatio, NearPlane, FarPlane);
        return view * projection * float4x4.CreateScale(1f, -1f, 1f);
    }
}
