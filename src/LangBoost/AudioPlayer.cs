using System.IO;
using NAudio.Wave;

namespace LangBoost;

/// <summary>
/// Simple in-memory WAV clip player (uses NAudio's WaveOutEvent).
/// Plays from a position; the caller decides when to stop (e.g. when reaching the end
/// of the selection) by checking <see cref="CurrentTime"/> and <see cref="IsPlaying"/>.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private readonly byte[] _wav;
    private WaveOutEvent? _output;
    private WaveFileReader? _reader;

    public AudioPlayer(byte[] wav)
    {
        _wav = wav;
        using var probe = new WaveFileReader(new MemoryStream(_wav));
        Duration = probe.TotalTime;
    }

    public TimeSpan Duration { get; }
    public TimeSpan CurrentTime => _reader?.CurrentTime ?? TimeSpan.Zero;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public void Play(TimeSpan from)
    {
        Stop();
        _reader = new WaveFileReader(new MemoryStream(_wav));
        if (from > TimeSpan.Zero && from < Duration)
            _reader.CurrentTime = from;

        _output = new WaveOutEvent();
        _output.Init(_reader);
        _output.Play();
    }

    public void Stop()
    {
        if (_output is not null)
        {
            try { _output.Stop(); } catch { /* ignore */ }
            _output.Dispose();
            _output = null;
        }
        _reader?.Dispose();
        _reader = null;
    }

    public void Dispose() => Stop();
}
