using BrutalShore.Audio;
using BrutalShore.Voxel;

namespace Shorebound.Infrastructure.Audio;

/// <summary>Every sound this game makes, built out of arithmetic when the world opens.</summary>
/// <remarks>
/// <para>
/// A block is a colour and a pattern; a block being broken is a burst of noise with a filter on
/// it. Which filter is decided by what kind of block it is, through the same
/// <see cref="BlockPattern" /> the shader uses to draw its surface — so stone sounds like the
/// thing stone looks like, and a block added tomorrow sounds right without anyone recording
/// anything.
/// </para>
/// <para>
/// Built once, at load, into a handful of buffers. Nothing here allocates or synthesises while
/// the game is running: a sound that cost a millisecond to make would be a sound that stuttered
/// the frame it played on.
/// </para>
/// </remarks>
public sealed class GameSounds
{
    private readonly AudioDevice _device;
    private readonly BlockRegistry _registry;
    private readonly Dictionary<BlockPattern, uint> _breaking = [];
    private readonly Dictionary<BlockPattern, uint> _placing = [];
    private readonly Dictionary<BlockPattern, uint> _steps = [];

    private readonly uint _craft;
    private readonly uint _milestone;
    private readonly uint _hurt;
    private readonly uint _pickUp;
    private readonly uint _smelterLit;
    private readonly uint _swing;
    private readonly uint _stalkerNear;

    private readonly Random _variation = new(0x50F7);

    /// <summary>Makes every sound and uploads it.</summary>
    /// <param name="device">Where to upload to; a silent one gives back handles of zero.</param>
    /// <param name="registry">Where a block id is resolved to the pattern it sounds like.</param>
    public GameSounds(AudioDevice device, BlockRegistry registry)
    {
        _device = device;
        _registry = registry;

        foreach (var pattern in Enum.GetValues<BlockPattern>())
        {
            _breaking[pattern] = device.Upload(Breaking(pattern));
            _placing[pattern] = device.Upload(Placing(pattern));
            _steps[pattern] = device.Upload(Step(pattern));
        }

        _craft = device.Upload(Craft());
        _milestone = device.Upload(Milestone());
        _hurt = device.Upload(Hurt());
        _pickUp = device.Upload(PickUp());
        _smelterLit = device.Upload(SmelterLit());
        _swing = device.Upload(Swing());
        _stalkerNear = device.Upload(StalkerNear());
    }

    /// <summary>A block coming apart, at a place in the world.</summary>
    /// <param name="block">What broke.</param>
    /// <param name="relativeMeters">Where it was, relative to the listener.</param>
    public void Broke(BlockId block, float3 relativeMeters) =>
        _device.Play(_breaking[PatternOf(block)], relativeMeters, 0.75f, Wobble(0.12f));

    /// <summary>A block going down.</summary>
    /// <param name="block">What was placed.</param>
    /// <param name="relativeMeters">Where it went, relative to the listener.</param>
    public void Placed(BlockId block, float3 relativeMeters) =>
        _device.Play(_placing[PatternOf(block)], relativeMeters, 0.6f, Wobble(0.1f));

    /// <summary>A footfall on whatever the player is standing on.</summary>
    /// <param name="block">What is underfoot.</param>
    public void Stepped(BlockId block) => _device.PlayHere(_steps[PatternOf(block)], 0.3f, Wobble(0.18f));

    /// <summary>Something made on the grid.</summary>
    public void Crafted() => _device.PlayHere(_craft, 0.55f);

    /// <summary>A milestone paid for.</summary>
    public void Advanced() => _device.PlayHere(_milestone, 0.7f);

    /// <summary>Something landing on the player.</summary>
    public void Bitten() => _device.PlayHere(_hurt, 0.8f);

    /// <summary>A swing connecting with something.</summary>
    public void Struck() => _device.PlayHere(_swing, 0.6f, Wobble(0.14f));

    /// <summary>A block walked over and picked up.</summary>
    public void PickedUp() => _device.PlayHere(_pickUp, 0.35f, Wobble(0.2f));

    /// <summary>A smelter catching.</summary>
    /// <param name="relativeMeters">Where it is, relative to the listener.</param>
    public void SmelterCaught(float3 relativeMeters) => _device.Play(_smelterLit, relativeMeters, 0.5f);

    /// <summary>Something out in the dark, close enough to hear.</summary>
    /// <param name="relativeMeters">Where it is, relative to the listener.</param>
    public void StalkerHeard(float3 relativeMeters) =>
        _device.Play(_stalkerNear, relativeMeters, 0.45f, Wobble(0.25f));

    /// <summary>Every sound this game has, with a name, and without a device.</summary>
    /// <returns>Each sound's name and the waveform it is made of.</returns>
    /// <remarks>
    /// Here so that the sounds can be looked at rather than only listened to. A test can assert
    /// that none of them clips, that none is silent and that none goes on for ten seconds — all
    /// of which have been true of a sound at some point in every game ever made, and none of
    /// which anyone notices until they are wearing headphones.
    /// </remarks>
    public static IEnumerable<(string Name, Waveform Sound)> Catalogue()
    {
        foreach (var pattern in Enum.GetValues<BlockPattern>())
        {
            yield return ($"breaking {pattern}".ToLowerInvariant(), Breaking(pattern));
            yield return ($"placing {pattern}".ToLowerInvariant(), Placing(pattern));
            yield return ($"step {pattern}".ToLowerInvariant(), Step(pattern));
        }

        yield return ("craft", Craft());
        yield return ("milestone", Milestone());
        yield return ("hurt", Hurt());
        yield return ("pick up", PickUp());
        yield return ("smelter lit", SmelterLit());
        yield return ("swing", Swing());
        yield return ("stalker near", StalkerNear());
    }

    /// <summary>The pattern a block's sound is taken from.</summary>
    private BlockPattern PatternOf(BlockId block) =>
        block.IsAir ? BlockPattern.Smooth : _registry[block].Pattern;

    /// <summary>A little randomness in pitch, so a repeated sound is not a machine gun.</summary>
    private float Wobble(float by) => 1f + (float)((_variation.NextDouble() - 0.5) * 2.0 * by);

    /// <summary>
    /// How dull a material sounds and how low it is, from the pattern it is drawn with.
    /// </summary>
    /// <remarks>
    /// One table rather than a sound per block. The patterns already group the blocks by what
    /// they are made of — crumbly, grainy, bladed, grained — which is exactly the grouping a
    /// listener hears, so the one that was written for the renderer does for the speakers too.
    /// </remarks>
    private static (float Smoothing, double Hertz) Material(BlockPattern pattern) =>
        pattern switch
        {
            BlockPattern.Crumbly => (0.82f, 110.0),
            BlockPattern.Grainy => (0.62f, 150.0),
            BlockPattern.Scattered => (0.5f, 240.0),
            BlockPattern.Bladed => (0.45f, 260.0),
            BlockPattern.Grained => (0.7f, 170.0),
            BlockPattern.Glinting => (0.3f, 420.0),
            BlockPattern.Rippled => (0.88f, 90.0),
            BlockPattern.Radiant => (0.35f, 520.0),
            _ => (0.74f, 130.0),
        };

    /// <summary>A block of some material coming apart.</summary>
    /// <param name="pattern">What the block is drawn with, which is also what it is made of.</param>
    /// <returns>The sound, normalised so it cannot clip.</returns>
    public static Waveform Breaking(BlockPattern pattern)
    {
        var (smoothing, hertz) = Material(pattern);
        var waveform = new Waveform(0.26);

        Synth.Noise(waveform, seed: 1 + (int)pattern, amplitude: 0.9f, Envelope.Hit, smoothing);

        // A short fall in pitch under the noise, which is what makes it a thing coming apart
        // rather than a hiss.
        Synth.Tone(waveform, hertz, hertz * 0.55, 0.5f, Envelope.Hit);

        return waveform.NormalizeTo(0.85f);
    }

    /// <summary>A block of some material going down.</summary>
    /// <param name="pattern">What the block is drawn with.</param>
    /// <returns>The sound, normalised so it cannot clip.</returns>
    public static Waveform Placing(BlockPattern pattern)
    {
        var (smoothing, hertz) = Material(pattern);
        var waveform = new Waveform(0.13);

        // Shorter and higher than breaking, because putting something down is one impact and
        // breaking it is several.
        Synth.Noise(waveform, seed: 50 + (int)pattern, amplitude: 0.7f, Envelope.Hit, smoothing * 0.9f);
        Synth.Tone(waveform, hertz * 1.4, hertz, 0.55f, Envelope.Hit);

        return waveform.NormalizeTo(0.8f);
    }

    /// <summary>A footfall on some material.</summary>
    /// <param name="pattern">What is underfoot.</param>
    /// <returns>The sound, normalised so it cannot clip.</returns>
    public static Waveform Step(BlockPattern pattern)
    {
        var (smoothing, hertz) = Material(pattern);
        var waveform = new Waveform(0.1);

        Synth.Noise(waveform, seed: 100 + (int)pattern, amplitude: 0.8f, Envelope.Hit, smoothing);
        Synth.Tone(waveform, hertz * 0.8, hertz * 0.5, 0.3f, Envelope.Hit);

        return waveform.NormalizeTo(0.55f);
    }

    /// <summary>Something made on the grid.</summary>
    /// <returns>The sound, normalised so it cannot clip.</returns>
    public static Waveform Craft()
    {
        var waveform = new Waveform(0.3);

        // Two notes a fifth apart, which is the shortest thing that reads as "that worked".
        Synth.Tone(waveform, 520.0, 520.0, 0.5f, new Envelope(0.01, 0.5));
        Synth.Tone(waveform, 780.0, 784.0, 0.4f, new Envelope(0.3, 0.6));
        Synth.Burst(waveform, seed: 9, atSeconds: 0.0, forSeconds: 0.04, amplitude: 0.5f, smoothing: 0.55f);

        return waveform.NormalizeTo(0.8f);
    }

    /// <summary>A milestone paid for.</summary>
    /// <returns>The sound, normalised so it cannot clip.</returns>
    public static Waveform Milestone()
    {
        var waveform = new Waveform(1.1);

        // A rising third over a low swell: longer and warmer than crafting, because it happens
        // four times in a game and ought to feel like it.
        Synth.Tone(waveform, 196.0, 196.0, 0.5f, Envelope.Swell);
        Synth.Tone(waveform, 392.0, 392.0, 0.35f, new Envelope(0.05, 0.8, 1.2));
        Synth.Tone(waveform, 494.0, 494.0, 0.3f, new Envelope(0.25, 0.7, 1.2));
        Synth.Tone(waveform, 587.0, 587.0, 0.28f, new Envelope(0.45, 0.55, 1.2));

        return waveform.NormalizeTo(0.85f);
    }

    /// <summary>Something landing on the player.</summary>
    /// <returns>The sound, normalised so it cannot clip.</returns>
    public static Waveform Hurt()
    {
        var waveform = new Waveform(0.22);

        // Falling and rough. Nothing in this game sounds like this except being hurt, which is
        // the point: it has to be unmistakable with the sound turned down.
        Synth.Tone(waveform, 300.0, 90.0, 0.8f, Envelope.Hit);
        Synth.Noise(waveform, seed: 77, amplitude: 0.5f, Envelope.Hit, smoothing: 0.5f);

        return waveform.NormalizeTo(0.9f);
    }

    /// <summary>A block walked over and taken.</summary>
    /// <returns>The sound, normalised so it cannot clip.</returns>
    public static Waveform PickUp()
    {
        var waveform = new Waveform(0.09);
        Synth.Tone(waveform, 700.0, 1100.0, 0.7f, new Envelope(0.05, 0.8));

        return waveform.NormalizeTo(0.7f);
    }

    /// <summary>A fire catching in a smelter.</summary>
    /// <returns>The sound, normalised so it cannot clip.</returns>
    public static Waveform SmelterLit()
    {
        var waveform = new Waveform(0.5);

        // A low roar swelling up, which is a fire catching.
        Synth.Noise(waveform, seed: 31, amplitude: 0.9f, Envelope.Swell, smoothing: 0.93f);
        Synth.Tone(waveform, 70.0, 55.0, 0.4f, Envelope.Swell);

        return waveform.NormalizeTo(0.75f);
    }

    /// <summary>A swing connecting with something.</summary>
    /// <returns>The sound, normalised so it cannot clip.</returns>
    public static Waveform Swing()
    {
        var waveform = new Waveform(0.16);
        Synth.Noise(waveform, seed: 61, amplitude: 0.9f, Envelope.Hit, smoothing: 0.4f);
        Synth.Tone(waveform, 180.0, 70.0, 0.6f, Envelope.Hit);

        return waveform.NormalizeTo(0.85f);
    }

    /// <summary>Something breathing out in the dark.</summary>
    /// <returns>The sound, normalised so it cannot clip.</returns>
    public static Waveform StalkerNear()
    {
        var waveform = new Waveform(0.55);

        // Breathing, more or less: two swells of very dull noise with a low tone under them.
        Synth.Noise(waveform, seed: 97, amplitude: 0.8f, Envelope.Swell, smoothing: 0.96f);
        Synth.Tone(waveform, 58.0, 44.0, 0.45f, Envelope.Swell);

        return waveform.NormalizeTo(0.7f);
    }
}
