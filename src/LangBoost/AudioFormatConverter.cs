using System.IO;
using NAudio.Utils;
using NAudio.Wave;

namespace LangBoost;

/// <summary>
/// Converte o áudio bruto capturado (geralmente IEEE float 48 kHz estéreo) para
/// WAV PCM 16-bit mono 16 kHz — formato pequeno e compatível com APIs de fala.
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
        // IgnoreDisposeStream evita que o WaveFileWriter feche o MemoryStream antes do ToArray.
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
    /// Recorta um WAV PCM mantendo apenas o intervalo [from, to]. Usado para enviar à
    /// transcrição só o trecho selecionado pelo usuário no player.
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
