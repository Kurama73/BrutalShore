using Silk.NET.OpenAL;

namespace BrutalShore.Audio;

/// <summary>The sound card, if there is one, and the handful of voices playing through it.</summary>
/// <remarks>
/// <para>
/// Everything about this type is arranged around one fact: a great many machines that can run
/// this game have no working audio device. A container, a machine over a remote session, a
/// desktop with the sound server not started. So opening one is allowed to fail, failing is not
/// an error, and every method on it does nothing quietly when it did. A game that will not start
/// because nothing can play a sound is worse than a game that is silent.
/// </para>
/// <para>
/// It owns a fixed pool of sources rather than making one per sound. OpenAL has a hard and
/// implementation-defined limit on how many there can be, reached long before a player notices
/// the limit on how many they can hear; a pool of thirty-two, with the oldest one stolen when
/// they are all busy, never hits it and never runs out.
/// </para>
/// <para>
/// The listener is the origin. Positions handed to <see cref="Play" /> are already relative to
/// whoever is listening, worked out in f64 and narrowed by the caller, because a world coordinate
/// a kilometre out has no business being put into a float.
/// </para>
/// </remarks>
public sealed class AudioDevice : IDisposable
{
    /// <summary>How many sounds can be playing at once.</summary>
    private const int Voices = 32;

    /// <summary>How far away a sound has fallen to half its loudness, in metres.</summary>
    private const float HalfLoudnessMeters = 8f;

    private readonly AL? _al;
    private readonly ALContext? _alc;
    private readonly unsafe Device* _device;
    private readonly unsafe Context* _context;
    private readonly uint[] _sources = [];

    private int _nextVoice;

    /// <summary>Opens the default device, or gives back something silent.</summary>
    /// <remarks>
    /// The whole constructor is inside a catch. The ways this fails are a missing native library,
    /// a device that will not open, and a context that will not go current, and the response to
    /// all three is the same, so distinguishing them would only be a longer way to be silent.
    /// </remarks>
    public unsafe AudioDevice()
    {
        try
        {
            _alc = ALContext.GetApi(soft: true);
            _al = AL.GetApi(soft: true);

            _device = _alc.OpenDevice(string.Empty);
            if (_device is null)
            {
                Quieten();
                return;
            }

            _context = _alc.CreateContext(_device, null);
            if (_context is null || !_alc.MakeContextCurrent(_context))
            {
                Quieten();
                return;
            }

            _sources = new uint[Voices];
            for (var voice = 0; voice < Voices; voice++)
            {
                _sources[voice] = _al.GenSource();
                _al.SetSourceProperty(_sources[voice], SourceFloat.ReferenceDistance, HalfLoudnessMeters);
                _al.SetSourceProperty(_sources[voice], SourceFloat.RolloffFactor, 1f);
            }

            _al.SetListenerProperty(ListenerVector3.Position, 0f, 0f, 0f);
            IsOpen = true;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            Quieten();
        }
    }

    /// <summary>Whether there is anything to play through.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>How loud everything is, from 0 for silence to 1.</summary>
    public float Volume { get; set; } = 1f;

    /// <summary>Uploads a waveform so it can be played, or gives back nothing when silent.</summary>
    /// <param name="waveform">The sound to upload.</param>
    /// <returns>A handle to play, which is 0 when there is no device.</returns>
    public unsafe uint Upload(Waveform waveform)
    {
        ArgumentNullException.ThrowIfNull(waveform);

        if (!IsOpen || _al is null)
            return 0;

        var pcm = waveform.ToPcm16();
        var buffer = _al.GenBuffer();

        fixed (short* samples = pcm)
            _al.BufferData(buffer, BufferFormat.Mono16, samples, pcm.Length * sizeof(short), Waveform.SampleRate);

        return buffer;
    }

    /// <summary>Plays an uploaded sound at a point relative to the listener.</summary>
    /// <param name="buffer">What to play, as <see cref="Upload" /> gave it.</param>
    /// <param name="relativeMeters">Where it is, with the listener at the origin.</param>
    /// <param name="gain">How loud, from 0 to 1, before the master volume.</param>
    /// <param name="pitch">How fast to play it; 1 is as recorded, 2 is an octave up.</param>
    /// <remarks>
    /// A little randomness in pitch at the call site is what stops a repeated sound — a footstep,
    /// a pick on stone — turning into a machine gun. That belongs to whoever knows the sound is
    /// repeated, which is not this type.
    /// </remarks>
    public void Play(uint buffer, float3 relativeMeters, float gain = 1f, float pitch = 1f)
    {
        if (!IsOpen || _al is null || buffer == 0 || Volume <= 0f)
            return;

        // Round robin, stealing the oldest. A sound cut off because thirty-two others are playing
        // is a sound nobody could hear anyway.
        var source = _sources[_nextVoice];
        _nextVoice = (_nextVoice + 1) % Voices;

        _al.SourceStop(source);
        _al.SetSourceProperty(source, SourceInteger.Buffer, (int)buffer);
        _al.SetSourceProperty(source, SourceVector3.Position, relativeMeters.X, relativeMeters.Y, relativeMeters.Z);
        _al.SetSourceProperty(source, SourceFloat.Gain, float.Clamp(gain, 0f, 1f) * Volume);
        _al.SetSourceProperty(source, SourceFloat.Pitch, float.Clamp(pitch, 0.25f, 4f));
        _al.SourcePlay(source);
    }

    /// <summary>Plays an uploaded sound in the player's own head, with no position.</summary>
    /// <param name="buffer">What to play.</param>
    /// <param name="gain">How loud, from 0 to 1.</param>
    /// <param name="pitch">How fast to play it.</param>
    public void PlayHere(uint buffer, float gain = 1f, float pitch = 1f) =>
        Play(buffer, float3.Zero, gain, pitch);

    /// <summary>Which way the listener is facing, so that sounds come from the right side.</summary>
    /// <param name="forward">Where they are looking.</param>
    /// <param name="up">Which way is up for them.</param>
    public unsafe void PointListener(float3 forward, float3 up)
    {
        if (!IsOpen || _al is null)
            return;

        // Six floats in one array, which is how OpenAL has always taken an orientation.
        Span<float> orientation = [forward.X, forward.Y, forward.Z, up.X, up.Y, up.Z];
        fixed (float* values = orientation)
            _al.SetListenerProperty(ListenerFloatArray.Orientation, values);
    }

    /// <inheritdoc />
    public unsafe void Dispose()
    {
        if (_al is not null)
        {
            foreach (var source in _sources)
                _al.DeleteSource(source);
        }

        if (_alc is not null && _context is not null)
        {
            _alc.MakeContextCurrent(null);
            _alc.DestroyContext(_context);
        }

        if (_alc is not null && _device is not null)
            _alc.CloseDevice(_device);

        _al?.Dispose();
        _alc?.Dispose();
        IsOpen = false;
    }

    private void Quieten() => IsOpen = false;
}
