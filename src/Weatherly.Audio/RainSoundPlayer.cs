using System.Runtime.InteropServices;
using Vortice.Multimedia;
using Vortice.XAudio2;
using Weatherly.Core.Audio;

namespace Weatherly.Audio;

/// <summary>
/// Plays the procedural rain sound through XAudio2 (PLAN.md, section 8). A small thread keeps a few short
/// buffers queued and refills them from <see cref="RainSynth"/>, so parameter changes are heard within
/// about 100 ms. Sound is off by default and the voice is paused while the rain is silent.
/// </summary>
public sealed class RainSoundPlayer : IDisposable
{
    private const int BufferCount = 3;
    private const int BufferMilliseconds = 40;

    private readonly RainSynth _synth = new();
    private readonly IXAudio2 _engine;
    private readonly IXAudio2MasteringVoice _master;
    private readonly IXAudio2SourceVoice _voice;
    private readonly float[][] _buffers = new float[BufferCount][];
    private readonly GCHandle[] _pins = new GCHandle[BufferCount];
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _stop = new();
    private readonly Lock _gate = new();
    private int _next;

    private RainSoundPlayer(IXAudio2 engine, IXAudio2MasteringVoice master)
    {
        _engine = engine;
        _master = master;
        _voice = engine.CreateSourceVoice(WaveFormat.CreateIeeeFloatWaveFormat(_synth.SampleRate, 1));

        int samples = _synth.SampleRate * BufferMilliseconds / 1000;
        for (int i = 0; i < BufferCount; i++)
        {
            _buffers[i] = new float[samples];

            // XAudio2 reads the samples while they are queued: they must not move.
            _pins[i] = GCHandle.Alloc(_buffers[i], GCHandleType.Pinned);
        }

        _voice.Start();
        _thread = new Thread(Pump) { Name = "Weatherly rain sound", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>
    /// Creates the player, or returns <see langword="null"/> with a reason when no audio output is available
    /// (no sound device, audio service stopped, remote session without audio).
    /// </summary>
    public static RainSoundPlayer? TryCreate(out string? error)
    {
        IXAudio2? engine = null;
        try
        {
            engine = XAudio2.XAudio2Create();
            IXAudio2MasteringVoice master = engine.CreateMasteringVoice();
            error = null;
            return new RainSoundPlayer(engine, master);
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

    /// <summary>Updates what the rain sounds like. Thread-safe.</summary>
    public void Update(float intensity, float hitsPerSecond, float volume)
    {
        lock (_gate)
        {
            _synth.Intensity = intensity;
            _synth.HitsPerSecond = hitsPerSecond;
            _synth.Volume = volume;
        }
    }

    public void Dispose()
    {
        _stop.Set();
        _thread.Join();
        _voice.Stop();
        _voice.FlushSourceBuffers();
        _voice.DestroyVoice();
        _voice.Dispose();
        _master.DestroyVoice();
        _master.Dispose();
        _engine.Dispose();
        foreach (GCHandle pin in _pins)
        {
            pin.Free();
        }

        _stop.Dispose();
    }

    private void Pump()
    {
        while (!_stop.IsSet)
        {
            while (_voice.State.BuffersQueued < BufferCount)
            {
                float[] buffer = _buffers[_next];
                lock (_gate)
                {
                    _synth.Render(buffer);
                }

                using var audio = new AudioBuffer(_pins[_next].AddrOfPinnedObject(), (uint)(buffer.Length * sizeof(float)), BufferFlags.None);
                _voice.SubmitSourceBuffer(audio);
                _next = (_next + 1) % BufferCount;
            }

            _stop.Wait(BufferMilliseconds / 4);
        }
    }
}
