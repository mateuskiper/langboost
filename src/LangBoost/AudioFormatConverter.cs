using System.IO;
using NAudio.Utils;
using NAudio.Wave;

namespace LangBoost;

/// <summary>
/// Converts the raw captured audio (usually IEEE float 48 kHz stereo) to
/// WAV PCM 16-bit mono 16 kHz — a small format compatible with speech APIs.
/// </summary>
public static class AudioFormatConverter
{
    private static readonly WaveFormat TargetFormat = new(16000, 16, 1);

    public static byte[] ToWav16kMono(byte[] raw, WaveFormat sourceFormat)
    {
        using var rawStream = new RawSourceWaveStream(new MemoryStream(raw), sourceFormat);
        using var resampler = new MediaFoundationResampler(rawStream, TargetFormat)
        {
            ResamplerQuality = 60
        };

        using var outStream = new MemoryStream();
        // IgnoreDisposeStream keeps the WaveFileWriter from closing the MemoryStream before ToArray.
        using (var writer = new WaveFileWriter(new IgnoreDisposeStream(outStream), resampler.WaveFormat))
        {
            var buffer = new byte[resampler.WaveFormat.AverageBytesPerSecond];
            int read;
            while ((read = resampler.Read(buffer, 0, buffer.Length)) > 0)
            {
                writer.Write(buffer, 0, read);
            }
        }

        return outStream.ToArray();
    }

    /// <summary>
    /// Trims a PCM WAV keeping only the [from, to] range. Used to send to transcription
    /// only the segment the user selected in the player.
    /// </summary>
    public static byte[] TrimWav(byte[] wav, TimeSpan from, TimeSpan to)
    {
        using var reader = new WaveFileReader(new MemoryStream(wav));
        var format = reader.WaveFormat;

        long startByte = AlignDown((long)(from.TotalSeconds * format.AverageBytesPerSecond), format.BlockAlign);
        long endByte = AlignDown((long)(to.TotalSeconds * format.AverageBytesPerSecond), format.BlockAlign);

        startByte = Math.Clamp(startByte, 0, reader.Length);
        endByte = Math.Clamp(endByte, startByte, reader.Length);

        reader.Position = startByte;
        var buffer = new byte[endByte - startByte];
        int read = reader.Read(buffer, 0, buffer.Length);

        using var outStream = new MemoryStream();
        using (var writer = new WaveFileWriter(new IgnoreDisposeStream(outStream), format))
            writer.Write(buffer, 0, read);

        return outStream.ToArray();
    }

    private static long AlignDown(long value, int blockAlign) => value - (value % blockAlign);
}
