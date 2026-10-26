namespace BrutalShore.Audio;

/// <summary>Makes sounds out of arithmetic, because this repository ships no audio files.</summary>
/// <remarks>
/// <para>
/// The same decision the renderer already made about textures. A block's surface is a pattern
/// computed in the fragment shader rather than an image, and a block breaking is a burst of noise
/// shaped by an envelope rather than a recording — for the same reasons. There is nothing to
/// licence, nothing to keep in step with the code, nothing in the repository that cannot be read,
/// and changing how a sound feels is changing a number rather than opening an editor.
/// </para>
/// <para>
/// What it gives up is realism, and that is the right thing to give up: a game made of coloured
/// cubes with procedural surfaces does not want a recording of a real pickaxe on real stone, it
/// wants something that reads as the same world the pictures came from.
/// </para>
/// <para>
/// Everything here is deterministic given a seed, so a sound can be asserted about. That is what
/// lets the shape of a sound — that it starts loud and ends quiet, that it never clips, that it
/// is as long as it says — be a test rather than a thing someone listened to once.
/// </para>
/// </remarks>
public static class Synth
{
    /// <summary>Adds a sine tone that slides from one pitch to another.</summary>
    /// <param name="into">The waveform to add to.</param>
    /// <param name="fromHertz">The pitch it starts at.</param>
    /// <param name="toHertz">The pitch it ends at.</param>
    /// <param name="amplitude">How loud, from 0 to 1.</param>
    /// <param name="shape">How its loudness changes over its length.</param>
    /// <remarks>
    /// The phase is carried rather than recomputed from the time, which is what makes a slide
    /// smooth. Computing the phase as frequency times time with a frequency that is itself
    /// changing produces a tone that jumps at every sample — audible as a buzz, and the first
    /// thing anyone gets wrong when writing one of these.
    /// </remarks>
    public static void Tone(Waveform into, double fromHertz, double toHertz, float amplitude, Envelope shape)
    {
        var samples = into.Samples;
        var phase = 0.0;

        for (var index = 0; index < samples.Length; index++)
        {
            var along = (double)index / samples.Length;
            var hertz = double.Lerp(fromHertz, toHertz, along);

            phase += double.Tau * hertz / Waveform.SampleRate;
            samples[index] += (float)(double.Sin(phase) * amplitude * shape.At(along));
        }
    }

    /// <summary>Adds filtered noise, which is what everything percussive is made of.</summary>
    /// <param name="into">The waveform to add to.</param>
    /// <param name="seed">What the noise is generated from, so a sound is repeatable.</param>
    /// <param name="amplitude">How loud, from 0 to 1.</param>
    /// <param name="shape">How its loudness changes over its length.</param>
    /// <param name="smoothing">
    /// How much of the previous sample is kept, from 0 for raw hiss to near 1 for a dull thud.
    /// This is a one-pole low-pass filter and it is the only tone control here: it is the
    /// difference between sand and stone.
    /// </param>
    public static void Noise(Waveform into, int seed, float amplitude, Envelope shape, float smoothing)
    {
        var samples = into.Samples;
        var random = new Random(seed);
        var kept = float.Clamp(smoothing, 0f, 0.999f);
        var previous = 0f;

        for (var index = 0; index < samples.Length; index++)
        {
            var along = (double)index / samples.Length;
            var raw = (float)((random.NextDouble() * 2.0) - 1.0);

            previous = (previous * kept) + (raw * (1f - kept));
            samples[index] += previous * amplitude * (float)shape.At(along);
        }
    }

    /// <summary>Adds a short burst of noise at a point, for a click or a footfall.</summary>
    /// <param name="into">The waveform to add to.</param>
    /// <param name="seed">What the noise is generated from.</param>
    /// <param name="atSeconds">When it happens.</param>
    /// <param name="forSeconds">How long it lasts.</param>
    /// <param name="amplitude">How loud, from 0 to 1.</param>
    /// <param name="smoothing">How dull it is, from 0 to near 1.</param>
    /// <remarks>
    /// A burst past the end of the waveform is dropped rather than refused, so that a rhythm of
    /// them can be written without every line checking it still fits.
    /// </remarks>
    public static void Burst(
        Waveform into,
        int seed,
        double atSeconds,
        double forSeconds,
        float amplitude,
        float smoothing
    )
    {
        var samples = into.Samples;
        var start = (int)(atSeconds * Waveform.SampleRate);
        var length = (int)(forSeconds * Waveform.SampleRate);

        if (start >= samples.Length || length <= 0)
            return;

        var random = new Random(seed);
        var kept = float.Clamp(smoothing, 0f, 0.999f);
        var previous = 0f;
        var shape = Envelope.Hit;

        for (var offset = 0; offset < length && start + offset < samples.Length; offset++)
        {
            var raw = (float)((random.NextDouble() * 2.0) - 1.0);
            previous = (previous * kept) + (raw * (1f - kept));
            samples[start + offset] += previous * amplitude * (float)shape.At((double)offset / length);
        }
    }
}
