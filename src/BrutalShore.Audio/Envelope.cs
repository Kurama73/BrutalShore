namespace BrutalShore.Audio;

/// <summary>How loud a sound is at each point along its length, from 0 to 1.</summary>
/// <remarks>
/// <para>
/// The one thing that decides whether a sound reads as a hit, a hum or a chime, far more than
/// what it is made of. A burst of noise with a sharp start and a quick decay is a footstep; the
/// same noise faded in and out is wind.
/// </para>
/// <para>
/// Expressed as attack and decay fractions of the whole rather than in seconds, so that a shape
/// can be reused at any length — the same <see cref="Hit" /> works for a tap and for a thud.
/// </para>
/// </remarks>
/// <param name="Attack">What fraction of the length is spent getting loud.</param>
/// <param name="Decay">What fraction at the end is spent getting quiet.</param>
/// <param name="Curve">
/// How sharply the decay falls: 1 is a straight line, higher drops away faster at the start and
/// lingers at the end, which is what nearly everything physical does.
/// </param>
public readonly record struct Envelope(double Attack, double Decay, double Curve = 2.0)
{
    /// <summary>Something struck: instant, then gone.</summary>
    public static Envelope Hit => new(0.004, 1.0, 3.0);

    /// <summary>Something struck that rings a little.</summary>
    public static Envelope Ring => new(0.01, 0.9, 1.6);

    /// <summary>Something that comes up and goes away, like a machine passing.</summary>
    public static Envelope Swell => new(0.35, 0.65, 1.0);

    /// <summary>How loud it is at a point along the sound.</summary>
    /// <param name="along">Where, from 0 at the start to 1 at the end.</param>
    /// <returns>The level, from 0 to 1.</returns>
    public double At(double along)
    {
        if (along <= 0.0 || along >= 1.0)
            return 0.0;

        if (Attack > 0.0 && along < Attack)
            return along / Attack;

        var decayStarts = double.Max(Attack, 1.0 - Decay);
        if (along <= decayStarts)
            return 1.0;

        var through = (along - decayStarts) / (1.0 - decayStarts);
        return double.Pow(1.0 - through, Curve);
    }
}
