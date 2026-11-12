using System.Runtime.InteropServices;
using Sillfall.Core.Audio;
using Sillfall.Core.Visitors;
using Vortice.Multimedia;
using Vortice.XAudio2;

namespace Sillfall.Audio;

/// <summary>
/// Plays the visitors' short sounds (<see cref="VisitorSynth"/>) through XAudio2. Each sound is rendered once and kept
/// pinned; two voices take turns, so an arrival and a collect can overlap.
/// </summary>
public sealed class VisitorSoundPlayer : IDisposable
{
    private const int VoiceCount = 2;

    private readonly IXAudio2 _engine;
    private readonly IXAudio2MasteringVoice _master;
    private readonly IXAudio2SourceVoice[] _voices = new IXAudio2SourceVoice[VoiceCount];
    private readonly Dictionary<(VisitorSound, VisitorKind), (float[] Samples, GCHandle Pin)> _sounds = [];
    private int _next;

    private VisitorSoundPlayer(IXAudio2 engine, IXAudio2MasteringVoice master)
    {
        _engine = engine;
        _master = master;
        for (int i = 0; i < VoiceCount; i++)
        {
            _voices[i] = engine.CreateSourceVoice(WaveFormat.CreateIeeeFloatWaveFormat(VisitorSynth.DefaultSampleRate, 1));
        }
    }

    /// <summary>Creates the player, or <see langword="null"/> with a reason when there is no audio output.</summary>
    public static VisitorSoundPlayer? TryCreate(out string? error)
    {
        IXAudio2? engine = null;
        try
        {
            engine = XAudio2.XAudio2Create();
            IXAudio2MasteringVoice master = engine.CreateMasteringVoice();
            error = null;
            return new VisitorSoundPlayer(engine, master);
        }
#pragma warning disable CA1031 // No audio is a normal situation (servers, remote sessions). It is reported, not thrown.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            engine?.Dispose();
            error = $"No audio output: {exception.Message}";
            return null;
        }
    }

    /// <summary>Plays <paramref name="sound"/> of <paramref name="kind"/> at <paramref name="volume"/> (0 to 1).</summary>
    public void Play(VisitorSound sound, VisitorKind kind, float volume)
    {
        if (volume <= 0f)
        {
            return;
        }

        if (!_sounds.TryGetValue((sound, kind), out (float[] Samples, GCHandle Pin) entry))
        {
            float[] samples = VisitorSynth.Render(sound, kind);

            // XAudio2 reads the samples while they are queued: they must not move.
            entry = (samples, GCHandle.Alloc(samples, GCHandleType.Pinned));
            _sounds[(sound, kind)] = entry;
        }

        IXAudio2SourceVoice voice = _voices[_next];
        _next = (_next + 1) % VoiceCount;
        voice.Stop();
        voice.FlushSourceBuffers();
        voice.SetVolume(Math.Clamp(volume, 0f, 1f));
        using var buffer = new AudioBuffer(entry.Pin.AddrOfPinnedObject(), (uint)(entry.Samples.Length * sizeof(float)), BufferFlags.EndOfStream);
        voice.SubmitSourceBuffer(buffer);
        voice.Start();
    }

    public void Dispose()
    {
        foreach (IXAudio2SourceVoice voice in _voices)
        {
            voice.Stop();
            voice.FlushSourceBuffers();
            voice.DestroyVoice();
            voice.Dispose();
        }

        _master.DestroyVoice();
        _master.Dispose();
        _engine.Dispose();
        foreach ((float[] _, GCHandle pin) in _sounds.Values)
        {
            pin.Free();
        }
    }
}
