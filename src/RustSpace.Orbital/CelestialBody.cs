namespace RustSpace.Orbital;

/// <summary>
/// One body of a system: a mass with a radius, and, unless it is the root, an orbit around another
/// body.
/// </summary>
/// <remarks>
/// A body holds no appearance of any kind. How it is drawn belongs to whatever draws it, which is
/// what keeps this assembly free of any graphics dependency and runnable under the test suite.
/// </remarks>
public sealed record CelestialBody
{
    /// <summary>The body's name, unique within its system.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Standard gravitational parameter <c>G * M</c>, in m^3/s^2.
    /// </summary>
    /// <remarks>
    /// Stored rather than the mass because it is what the equations of motion actually use, and
    /// because it is measured to far more significant figures than either G or the mass alone.
    /// </remarks>
    public required double GravitationalParameter { get; init; }

    /// <summary>Mean equatorial radius, in metres.</summary>
    public required double RadiusMeters { get; init; }

    /// <summary>
    /// The body this one orbits, or <see langword="null"/> for the root of the system.
    /// </summary>
    public CelestialBody? Parent { get; init; }

    /// <summary>
    /// The orbit around <see cref="Parent"/>, or <see langword="null"/> for the root of the system.
    /// </summary>
    public OrbitalElements? Orbit { get; init; }

    /// <summary>
    /// How many parents separate this body from the root: 0 for the root, 1 for a planet, 2 for a
    /// moon.
    /// </summary>
    public int Depth
    {
        get
        {
            var depth = 0;
            for (var body = Parent; body is not null; body = body.Parent)
                depth++;
            return depth;
        }
    }

    /// <summary>
    /// The standard gravitational parameter governing this body's orbit: the sum of its parent's
    /// and its own, in m^3/s^2. Zero for the root.
    /// </summary>
    /// <remarks>
    /// The two-body problem is governed by <c>G * (M_parent + M_body)</c>, not by the parent alone.
    /// For a planet around the Sun the difference is a part in a thousand at most, but the Moon is
    /// 1.2% of Earth's mass and leaving its own mass out puts its period half a day out — which is
    /// exactly the sort of small systematic error that gets blamed on the propagator.
    /// </remarks>
    public double TwoBodyGravitationalParameter =>
        Parent is { } parent ? parent.GravitationalParameter + GravitationalParameter : 0.0;

    /// <summary>The orbital period around <see cref="Parent"/> in seconds, or 0 for the root.</summary>
    public double PeriodSeconds =>
        Orbit is { } orbit ? orbit.PeriodSeconds(TwoBodyGravitationalParameter) : 0.0;

    /// <summary>
    /// Position relative to <see cref="Parent"/> at <paramref name="timeSeconds"/> since epoch, in
    /// metres, or the origin for the root.
    /// </summary>
    public double3 LocalPositionAt(double timeSeconds) =>
        Orbit is { } orbit
            ? TwoBody.StateAt(orbit, TwoBodyGravitationalParameter, timeSeconds).PositionMeters
            : double3.Zero;

    /// <summary>
    /// Position in the root body's frame at <paramref name="timeSeconds"/> since epoch, in metres.
    /// </summary>
    /// <remarks>
    /// Summed up the parent chain in f64, so a moon's absolute position is its parent's position
    /// plus its own orbit and never an offset anyone has to remember to apply. This is the
    /// "position is absolute" rule: the floating origin is applied once, where the frame is turned
    /// into something a GPU can hold, and nowhere else.
    /// </remarks>
    public double3 AbsolutePositionAt(double timeSeconds)
    {
        var position = double3.Zero;
        for (CelestialBody? body = this; body is not null; body = body.Parent)
            position += body.LocalPositionAt(timeSeconds);
        return position;
    }

    /// <summary>
    /// Velocity relative to <see cref="Parent"/> at <paramref name="timeSeconds"/> since epoch, in
    /// metres per second, or zero for the root.
    /// </summary>
    public double3 LocalVelocityAt(double timeSeconds) =>
        Orbit is { } orbit
            ? TwoBody.StateAt(orbit, TwoBodyGravitationalParameter, timeSeconds).VelocityMetersPerSecond
            : double3.Zero;

    /// <inheritdoc />
    public override string ToString() => Name;
}
