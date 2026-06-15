using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LangBoost;

/// <summary>
/// Captura todo o áudio que sai pelo dispositivo de reprodução padrão (WASAPI loopback)
/// e o mantém num buffer circular dos últimos N segundos. A reprodução continua normal;
/// funciona inclusive com conteúdo DRM (Netflix), pois o DRM afeta o vídeo, não o áudio.
/// </summary>
public sealed class AudioCaptureService : IDisposable
{
    private readonly WasapiLoopbackCapture _capture;
    private readonly AudioRingBuffer _buffer;

    public WaveFormat WaveFormat => _capture.WaveFormat;

    public AudioCaptureService(int seconds)
    {
        _capture = new WasapiLoopbackCapture();
        int capacity = _capture.WaveFormat.AverageBytesPerSecond * seconds;
        _buffer = new AudioRingBuffer(capacity);
        _capture.DataAvailable += OnDataAvailable;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
        => _buffer.Write(e.Buffer, 0, e.BytesRecorded);

    public void Start() => _capture.StartRecording();

    /// <summary>Retorna os últimos segundos de áudio bruto no formato nativo da captura.</summary>
    public byte[] Snapshot() => _buffer.Snapshot();

    public void Dispose()
    {
        try { _capture.StopRecording(); } catch { /* ignora */ }
        _capture.DataAvailable -= OnDataAvailable;
        _capture.Dispose();
    }
}
