namespace BrutalShore.World;

/// <summary>Bends a plane, so that a field sampled through it stops looking like a field.</summary>
/// <remarks>
/// <para>
/// Gradient noise is built on a square lattice and never quite stops admitting it. Its features
/// line up with the axes, its contours close into rings around each extremum, and summing octaves
/// does not help because every octave is built on the same grid. Terrain made straight out of it
/// reads as a contour map: concentric bands, all of them smooth, none of them going anywhere.
/// </para>
/// <para>
/// Domain warping fixes it at the input rather than the output. Before a field is sampled, the
/// point itself is moved by a second, much coarser field — so a straight line through the world
/// becomes a meandering one through the noise, and the rings are drawn out into something that
/// looks eroded. It costs two extra samples and it is the single largest difference between
/// terrain that looks generated and terrain that does not.
/// </para>
/// <para>
/// The two offsets come from two seeds rather than from one field sampled at two places. Sampling
/// one field twice gives offsets that are correlated, and a correlated warp slides the plane along
/// a diagonal instead of stirring it.
/// </para>
/// </remarks>
public sealed class Warp
{
    private readonly GradientNoise _along;
    private readonly GradientNoise _across;
    private readonly float _scale;

    /// <summary>Creates a warp.</summary>
    /// <param name="seed">The seed; two fields are derived from it.</param>
    /// <param name="scaleBlocks">How far apart the bends are, in blocks.</param>
    /// <param name="strengthBlocks">How far a point can be moved, in blocks.</param>
    /// <exception cref="ArgumentOutOfRangeException">The scale is not positive.</exception>
    /// <remarks>
    /// The two want to be of the same order. A warp much weaker than its scale is invisible; one
    /// much stronger folds the plane over itself, and a field sampled through a fold has two
    /// different values at one place, which shows up as terrain that tears.
    /// </remarks>
    public Warp(int seed, float scaleBlocks, float strengthBlocks)
    {
        if (scaleBlocks <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(scaleBlocks), scaleBlocks, "A warp needs a scale.");

        _along = new GradientNoise(seed ^ 0x2545F491);
        _across = new GradientNoise(seed ^ 0x6C8E9CF5);
        _scale = scaleBlocks;
        StrengthBlocks = strengthBlocks;
    }

    /// <summary>How far a point can be moved, in blocks.</summary>
    public float StrengthBlocks { get; }

    /// <summary>Where a point really is, once the plane has been bent.</summary>
    /// <param name="x">The point's first coordinate, in blocks.</param>
    /// <param name="y">The point's second coordinate, in blocks.</param>
    /// <returns>Where to sample instead, in blocks.</returns>
    public (float X, float Y) Of(float x, float y) =>
        (
            x + (_along.Sample(x / _scale, y / _scale) * StrengthBlocks),
            y + (_across.Sample(x / _scale, y / _scale) * StrengthBlocks)
        );
}
