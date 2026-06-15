using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LangBoost;

/// <summary>
/// Captures all audio coming out of the default playback device (WASAPI loopback)
/// and keeps it in a circular buffer of the last N seconds. Playback continues normally;
/// it works even with DRM content (Netflix), since DRM affects the video, not the audio.
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

    /// <summary>Returns the last seconds of raw audio in the capture's native format.</summary>
    public byte[] Snapshot() => _buffer.Snapshot();

    public void Dispose()
    {
        try { _capture.StopRecording(); } catch { /* ignore */ }
        _capture.DataAvailable -= OnDataAvailable;
        _capture.Dispose();
    }
}
