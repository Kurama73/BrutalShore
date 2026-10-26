namespace BrutalShore.Audio;

/// <summary>A sound as numbers: mono samples between -1 and 1, at a known rate.</summary>
/// <remarks>
/// <para>
/// The whole of what this engine's audio is, before it reaches a device. A waveform is made, bent
/// about and mixed entirely in managed code with no device open, which is what makes every
/// interesting part of the sound in this game testable under <c>dotnet test</c> — the same
/// bargain the mesher takes with geometry.
/// </para>
/// <para>
/// Mono, deliberately. Where a sound is coming from is decided at the moment it is played, from
/// where the listener is standing, so a stereo waveform would be a sound that had already made up
/// its mind about something it cannot know.
/// </para>
/// </remarks>
public sealed class Waveform
{
    /// <summary>The rate everything here is made at, in samples a second.</summary>
    /// <remarks>
    /// Twenty-two thousand and fifty, which is half of CD rate. Everything this synthesises is
    /// percussive, short, and heard over a world; the top octave it gives up is not in any of it,
    /// and halving the rate halves the memory and the upload.
    /// </remarks>
    public const int SampleRate = 22_050;

    private readonly float[] _samples;

    /// <summary>Builds a waveform of a length in seconds, silent.</summary>
    /// <param name="seconds">How long it is.</param>
    /// <exception cref="ArgumentOutOfRangeException">If it is not a positive length.</exception>
    public Waveform(double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);
        _samples = new float[int.Max(1, (int)(seconds * SampleRate))];
    }

    /// <summary>Wraps samples that have already been made.</summary>
    /// <param name="samples">The samples, between -1 and 1.</param>
    public Waveform(float[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfZero(samples.Length);
        _samples = samples;
    }

    /// <summary>How many samples there are.</summary>
    public int Length => _samples.Length;

    /// <summary>How long it lasts, in seconds.</summary>
    public double Seconds => (double)_samples.Length / SampleRate;

    /// <summary>The samples themselves.</summary>
    public Span<float> Samples => _samples;

    /// <summary>The loudest sample in it, which is what headroom is measured against.</summary>
    public float Peak
    {
        get
        {
            var peak = 0f;
            foreach (var sample in _samples)
                peak = float.Max(peak, float.Abs(sample));

            return peak;
        }
    }

    /// <summary>Scales the whole thing so its loudest sample sits at a level.</summary>
    /// <param name="level">Where the peak should end up, from 0 to 1.</param>
    /// <returns>This waveform, so that building one reads as a sentence.</returns>
    /// <remarks>
    /// Done once at the end rather than by choosing amplitudes carefully along the way. Layering
    /// a tone over noise over another tone makes a peak nobody can predict, and a sound that
    /// clips is a sound that is wrong on every machine rather than merely loud.
    /// </remarks>
    public Waveform NormalizeTo(float level)
    {
        var peak = Peak;
        if (peak <= 1e-6f)
            return this;

        var scale = level / peak;
        for (var index = 0; index < _samples.Length; index++)
            _samples[index] *= scale;

        return this;
    }

    /// <summary>Turns it into the 16-bit samples a device wants.</summary>
    /// <returns>One signed 16-bit sample per sample, little-endian in memory.</returns>
    /// <remarks>
    /// Clamped rather than wrapped. A sample past the end of the range is a mistake somewhere
    /// above this, and the two ways of handling it are a sound that is slightly too loud and a
    /// sound that is replaced by a bang.
    /// </remarks>
    public short[] ToPcm16()
    {
        var pcm = new short[_samples.Length];
        for (var index = 0; index < _samples.Length; index++)
            pcm[index] = (short)(float.Clamp(_samples[index], -1f, 1f) * short.MaxValue);

        return pcm;
    }
}
