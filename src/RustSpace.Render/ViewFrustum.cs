namespace RustSpace.Render;

/// <summary>The six planes bounding what a view-projection matrix can see.</summary>
/// <remarks>
/// <para>
/// Extracted from the matrix rather than built from the camera's angles, so it is the same frustum
/// the GPU will clip against by construction — including the Vulkan y flip and whatever near and
/// far planes the camera happens to be using. A frustum assembled separately from the same inputs
/// drifts out of agreement with the matrix the moment one of them changes.
/// </para>
/// <para>
/// Each plane is stored as <c>(a, b, c, d)</c> with the normal pointing <em>into</em> the frustum,
/// so a point is inside when <c>a*x + b*y + c*z + d</c> is non-negative for all six.
/// </para>
/// </remarks>
public readonly struct ViewFrustum
{
    private readonly float4 _left;
    private readonly float4 _right;
    private readonly float4 _bottom;
    private readonly float4 _top;
    private readonly float4 _near;
    private readonly float4 _far;

    private ViewFrustum(float4 left, float4 right, float4 bottom, float4 top, float4 near, float4 far)
    {
        _left = Normalize(left);
        _right = Normalize(right);
        _bottom = Normalize(bottom);
        _top = Normalize(top);
        _near = Normalize(near);
        _far = Normalize(far);
    }

    /// <summary>Extracts the frustum from a view-projection matrix.</summary>
    /// <remarks>
    /// <para>
    /// The matrix is row-vector — a point is transformed as <c>v * M</c> — so the clip coordinates
    /// are dot products of the point with the matrix's <em>columns</em>. Each clip-space inequality
    /// then becomes one plane: <c>-w &lt;= x</c> gives <c>column0 + column3</c>, <c>x &lt;= w</c>
    /// gives <c>column3 - column0</c>, and so on. Depth runs from 0 to 1 rather than -1 to 1, which
    /// is why the near plane is <c>column2</c> alone instead of <c>column2 + column3</c>.
    /// </para>
    /// </remarks>
    public static ViewFrustum FromViewProjection(in float4x4 matrix)
    {
        var column0 = new float4(matrix.M11, matrix.M21, matrix.M31, matrix.M41);
        var column1 = new float4(matrix.M12, matrix.M22, matrix.M32, matrix.M42);
        var column2 = new float4(matrix.M13, matrix.M23, matrix.M33, matrix.M43);
        var column3 = new float4(matrix.M14, matrix.M24, matrix.M34, matrix.M44);

        return new ViewFrustum(
            column3 + column0,
            column3 - column0,
            column3 + column1,
            column3 - column1,
            column2,
            column3 - column2
        );
    }

    /// <summary>
    /// Whether an axis-aligned box is at least partly inside the frustum.
    /// </summary>
    /// <param name="minimum">The box's lowest corner.</param>
    /// <param name="maximum">The box's highest corner.</param>
    /// <remarks>
    /// Conservative: a box can be reported as visible while being outside all six planes taken
    /// together, which happens near the corners of the frustum. The error only ever draws
    /// something that is not seen, never hides something that is, and the cheap test is worth far
    /// more than the handful of extra draws.
    /// </remarks>
    public bool Intersects(float3 minimum, float3 maximum) =>
        IsInside(_left, minimum, maximum)
        && IsInside(_right, minimum, maximum)
        && IsInside(_bottom, minimum, maximum)
        && IsInside(_top, minimum, maximum)
        && IsInside(_near, minimum, maximum)
        && IsInside(_far, minimum, maximum);

    /// <summary>
    /// Whether the box has any point on the inward side of one plane.
    /// </summary>
    /// <remarks>
    /// Only the box corner furthest along the plane's normal has to be tested: if even that one is
    /// behind the plane, every other corner is too.
    /// </remarks>
    private static bool IsInside(in float4 plane, float3 minimum, float3 maximum)
    {
        var furthest = new float3(
            plane.X >= 0f ? maximum.X : minimum.X,
            plane.Y >= 0f ? maximum.Y : minimum.Y,
            plane.Z >= 0f ? maximum.Z : minimum.Z
        );

        return (plane.X * furthest.X) + (plane.Y * furthest.Y) + (plane.Z * furthest.Z) + plane.W >= 0f;
    }

    /// <summary>
    /// Scales a plane so its normal has unit length.
    /// </summary>
    /// <remarks>
    /// Not needed for the inside test, which only looks at the sign, but it makes the stored
    /// planes' <c>d</c> an actual distance in metres — which is what any later use, such as a
    /// sphere test or a distance-based fade, will want.
    /// </remarks>
    private static float4 Normalize(float4 plane)
    {
        var length = float.Sqrt((plane.X * plane.X) + (plane.Y * plane.Y) + (plane.Z * plane.Z));
        return length > 0f ? plane / length : plane;
    }
}
