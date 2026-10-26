namespace BrutalShore.Audio.Tests;

/// <summary>
/// The sounds themselves, asserted without a sound card: that they are the length they claim,
/// that they start and end at silence, that nothing clips, and that a slide is smooth.
/// </summary>
/// <remarks>
/// This is the whole reason the synthesiser is managed code with no device in it. A recording
/// cannot be tested and a sound played through a driver cannot be asserted about; an array of
/// numbers can, and every mistake that matters here — a sound that clips, a slide that buzzes, a
/// sound that is not repeatable — is visible in the numbers before anyone hears it.
/// </remarks>
public sealed class SynthTests
{
    [Fact]
    public void a_waveform_is_as_long_as_it_was_asked_for()
    {
        var waveform = new Waveform(0.25);

        // Within one sample, because a quarter of a second is not a whole number of them at
        // every rate and rounding has to go somewhere.
        Assert.Equal(0.25, waveform.Seconds, 1.0 / Waveform.SampleRate);
    }

    [Fact]
    public void a_length_of_nothing_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Waveform(0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Waveform(-1.0));
    }

    [Fact]
    public void a_fresh_waveform_is_silent()
    {
        Assert.Equal(0f, new Waveform(0.1).Peak);
    }

    [Fact]
    public void a_tone_starts_and_ends_at_silence()
    {
        // What makes a sound a sound rather than a click at each end. A waveform that began at
        // full amplitude would be a step from nothing to something, and a step is a bang.
        var waveform = new Waveform(0.2);
        Synth.Tone(waveform, 440.0, 440.0, 1f, Envelope.Hit);

        Assert.Equal(0f, waveform.Samples[0], 1e-3f);
        Assert.Equal(0f, waveform.Samples[^1], 1e-3f);
    }

    [Fact]
    public void a_hit_is_loudest_near_its_start()
    {
        var waveform = new Waveform(0.3);
        Synth.Noise(waveform, seed: 1, amplitude: 1f, Envelope.Hit, smoothing: 0.4f);

        var samples = waveform.Samples;
        var early = Loudest(samples[..(samples.Length / 4)]);
        var late = Loudest(samples[(samples.Length * 3 / 4)..]);

        Assert.True(early > late * 3f, $"early {early}, late {late}");
    }

    [Fact]
    public void a_swell_is_quiet_at_both_ends_and_loud_in_the_middle()
    {
        var waveform = new Waveform(0.5);
        Synth.Tone(waveform, 220.0, 220.0, 1f, Envelope.Swell);

        var samples = waveform.Samples;
        var middle = Loudest(samples[(samples.Length * 2 / 5)..(samples.Length * 3 / 5)]);

        Assert.True(middle > Loudest(samples[..(samples.Length / 10)]));
        Assert.True(middle > Loudest(samples[(samples.Length * 9 / 10)..]));
    }

    [Fact]
    public void a_slide_has_no_jumps_in_it()
    {
        // The bug this is really about: computing a phase as frequency times time, with a
        // frequency that is itself changing, makes the wave jump at every sample. It sounds like
        // a buzz, and it shows up here as a difference between neighbouring samples far larger
        // than a sine wave at that pitch could produce.
        var waveform = new Waveform(0.4);
        Synth.Tone(waveform, 120.0, 900.0, 1f, new Envelope(0.02, 0.5));

        var samples = waveform.Samples;
        var largestStep = 0f;
        for (var index = 1; index < samples.Length; index++)
            largestStep = float.Max(largestStep, float.Abs(samples[index] - samples[index - 1]));

        // A 900 Hz sine at 22 050 samples a second moves at most about a quarter of its
        // amplitude between two samples.
        Assert.True(largestStep < 0.35f, $"largest step {largestStep}");
    }

    [Fact]
    public void the_same_seed_makes_the_same_noise()
    {
        var first = new Waveform(0.1);
        var second = new Waveform(0.1);

        Synth.Noise(first, seed: 42, amplitude: 1f, Envelope.Hit, smoothing: 0.5f);
        Synth.Noise(second, seed: 42, amplitude: 1f, Envelope.Hit, smoothing: 0.5f);

        Assert.Equal(first.Samples.ToArray(), second.Samples.ToArray());
    }

    [Fact]
    public void smoothing_takes_the_hiss_out()
    {
        // The one tone control here, and what makes sand sound different from stone. A smoothed
        // noise moves less between neighbouring samples, which is what dull means.
        var sharp = new Waveform(0.2);
        var dull = new Waveform(0.2);

        Synth.Noise(sharp, seed: 7, amplitude: 1f, new Envelope(0.01, 0.01), smoothing: 0f);
        Synth.Noise(dull, seed: 7, amplitude: 1f, new Envelope(0.01, 0.01), smoothing: 0.9f);

        Assert.True(Roughness(sharp) > Roughness(dull) * 2f);
    }

    [Fact]
    public void normalising_puts_the_peak_where_it_was_asked_for()
    {
        var waveform = new Waveform(0.2);
        Synth.Tone(waveform, 300.0, 300.0, 0.1f, Envelope.Ring);
        Synth.Noise(waveform, seed: 3, amplitude: 0.1f, Envelope.Hit, smoothing: 0.3f);

        waveform.NormalizeTo(0.8f);

        Assert.Equal(0.8f, waveform.Peak, 1e-4f);
    }

    [Fact]
    public void normalising_silence_leaves_it_silent()
    {
        Assert.Equal(0f, new Waveform(0.1).NormalizeTo(0.9f).Peak);
    }

    [Fact]
    public void nothing_ever_clips_once_normalised()
    {
        // Three layers over each other makes a peak nobody can predict. Normalising at the end
        // is what stops a sound being wrong on every machine rather than merely loud.
        var waveform = new Waveform(0.3);
        Synth.Tone(waveform, 90.0, 60.0, 0.9f, Envelope.Hit);
        Synth.Tone(waveform, 400.0, 180.0, 0.9f, Envelope.Ring);
        Synth.Noise(waveform, seed: 11, amplitude: 0.9f, Envelope.Hit, smoothing: 0.6f);

        waveform.NormalizeTo(0.9f);

        foreach (var sample in waveform.Samples)
            Assert.InRange(sample, -1f, 1f);
    }

    [Fact]
    public void a_burst_past_the_end_is_dropped_rather_than_refused()
    {
        var waveform = new Waveform(0.1);

        Synth.Burst(waveform, seed: 1, atSeconds: 5.0, forSeconds: 0.05, amplitude: 1f, smoothing: 0.2f);

        Assert.Equal(0f, waveform.Peak);
    }

    [Fact]
    public void a_burst_lands_where_it_was_put()
    {
        var waveform = new Waveform(0.4);
        Synth.Burst(waveform, seed: 1, atSeconds: 0.2, forSeconds: 0.05, amplitude: 1f, smoothing: 0.2f);

        var samples = waveform.Samples;
        Assert.Equal(0f, Loudest(samples[..(samples.Length / 4)]));
        Assert.True(Loudest(samples[(samples.Length / 2 - 200)..(samples.Length / 2 + 200)]) > 0f);
    }

    [Fact]
    public void sixteen_bit_samples_come_out_in_range_and_in_step()
    {
        var waveform = new Waveform(0.05);
        Synth.Tone(waveform, 500.0, 500.0, 1f, Envelope.Ring);
        waveform.NormalizeTo(1f);

        var pcm = waveform.ToPcm16();

        Assert.Equal(waveform.Length, pcm.Length);
        Assert.Contains(pcm, sample => sample > short.MaxValue / 2);
    }

    [Fact]
    public void an_envelope_is_silent_at_both_ends_and_never_above_one()
    {
        foreach (var shape in (ReadOnlySpan<Envelope>)[Envelope.Hit, Envelope.Ring, Envelope.Swell])
        {
            Assert.Equal(0.0, shape.At(0.0));
            Assert.Equal(0.0, shape.At(1.0));

            for (var along = 0.0; along <= 1.0; along += 0.01)
                Assert.InRange(shape.At(along), 0.0, 1.0);
        }
    }

    private static float Loudest(ReadOnlySpan<float> samples)
    {
        var loudest = 0f;
        foreach (var sample in samples)
            loudest = float.Max(loudest, float.Abs(sample));

        return loudest;
    }

    /// <summary>How much a waveform moves between one sample and the next, on average.</summary>
    private static float Roughness(Waveform waveform)
    {
        var samples = waveform.Samples;
        var total = 0f;
        for (var index = 1; index < samples.Length; index++)
            total += float.Abs(samples[index] - samples[index - 1]);

        return total / samples.Length;
    }
}
