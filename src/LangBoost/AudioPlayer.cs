using System.IO;
using NAudio.Wave;

namespace LangBoost;

/// <summary>
/// Reprodutor simples de um clipe WAV em memória (usa WaveOutEvent do NAudio).
/// Toca a partir de uma posição; quem chama decide quando parar (ex.: ao atingir o fim
/// da seleção) consultando <see cref="CurrentTime"/> e <see cref="IsPlaying"/>.
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
            try { _output.Stop(); } catch { /* ignora */ }
            _output.Dispose();
            _output = null;
        }
        _reader?.Dispose();
        _reader = null;
    }

    public void Dispose() => Stop();
}
