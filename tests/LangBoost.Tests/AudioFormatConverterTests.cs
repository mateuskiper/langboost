using System.IO;
using LangBoost;
using NAudio.MediaFoundation;
using NAudio.Utils;
using NAudio.Wave;
using Xunit;

namespace LangBoost.Tests;

/// <summary>
/// Starts/stops Media Foundation once for the whole test class. MediaFoundationResampler
/// (used by AudioFormatConverter.ToWav16kMono) requires MediaFoundationApi.Startup().
/// </summary>
public sealed class MediaFoundationFixture : IDisposable
{
    public MediaFoundationFixture() => MediaFoundationApi.Startup();
    public void Dispose() => MediaFoundationApi.Shutdown();
}

/// <summary>
/// Regression tests for the audio conversion/trim pipeline. Inputs are generated in memory,
/// so no hardware or capture is involved.
/// </summary>
public class AudioFormatConverterTests : IClassFixture<MediaFoundationFixture>
{
    // Builds a valid PCM16 mono WAV of the given duration (silence) at the given sample rate.
    private static byte[] MakePcm16MonoWav(int sampleRate, double seconds)
    {
        var format = new WaveFormat(sampleRate, 16, 1);
        using var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(new IgnoreDisposeStream(ms), format))
        {
            int samples = (int)(sampleRate * seconds);
            var buffer = new byte[samples * 2]; // 16-bit mono => 2 bytes/sample (silence = zeros)
            writer.Write(buffer, 0, buffer.Length);
        }
        return ms.ToArray();
    }

    // Builds a raw IEEE-float 48 kHz stereo buffer (no WAV header) — the typical capture format.
    private static byte[] MakeRawFloat48kStereo(double seconds)
    {
        int sampleRate = 48000, channels = 2;
        int frames = (int)(sampleRate * seconds);
        return new byte[frames * channels * sizeof(float)]; // silence
    }

    [Fact]
    public void TrimWav_SelectsRequestedRange_ProducesValidWav()
    {
        var wav = MakePcm16MonoWav(16000, 2.0);

        var trimmed = AudioFormatConverter.TrimWav(wav, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.5));

        using var reader = new WaveFileReader(new MemoryStream(trimmed));
        Assert.Equal(16000, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);
        // ~1s expected; allow a small tolerance for block alignment.
        Assert.InRange(reader.TotalTime.TotalSeconds, 0.98, 1.02);
    }

    [Fact]
    public void TrimWav_EndBeyondLength_ClampsToEnd()
    {
        var wav = MakePcm16MonoWav(16000, 1.0);

        var trimmed = AudioFormatConverter.TrimWav(wav, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(99));

        using var reader = new WaveFileReader(new MemoryStream(trimmed));
        Assert.InRange(reader.TotalTime.TotalSeconds, 0.48, 0.52);
    }

    [Fact]
    public void TrimWav_FromAfterTo_ProducesEmptyButValidWav()
    {
        var wav = MakePcm16MonoWav(16000, 1.0);

        var trimmed = AudioFormatConverter.TrimWav(wav, TimeSpan.FromSeconds(0.8), TimeSpan.FromSeconds(0.2));

        using var reader = new WaveFileReader(new MemoryStream(trimmed));
        Assert.Equal(0, reader.Length); // endByte clamped to >= startByte
    }

    [Fact]
    public void ToWav16kMono_ResamplesToTargetFormat()
    {
        var raw = MakeRawFloat48kStereo(1.0);
        var sourceFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        var wav = AudioFormatConverter.ToWav16kMono(raw, sourceFormat);

        using var reader = new WaveFileReader(new MemoryStream(wav));
        Assert.Equal(16000, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        // Roughly 1 second of audio after resampling.
        Assert.InRange(reader.TotalTime.TotalSeconds, 0.9, 1.1);
    }
}
