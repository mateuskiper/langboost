namespace LangBoost;

/// <summary>
/// Buffer circular thread-safe que mantém apenas os bytes de áudio mais recentes
/// (os últimos N segundos). A thread de captura escreve continuamente; quando o
/// buffer enche, os bytes mais antigos são sobrescritos.
/// </summary>
public sealed class AudioRingBuffer
{
    private readonly byte[] _buffer;
    private readonly object _lock = new();
    private int _writePos;
    private bool _filled;

    public AudioRingBuffer(int capacityBytes)
    {
        _buffer = new byte[capacityBytes];
    }

    public void Write(byte[] data, int offset, int count)
    {
        lock (_lock)
        {
            // Se o bloco for maior que o buffer, só os últimos bytes interessam.
            if (count >= _buffer.Length)
            {
                Array.Copy(data, offset + count - _buffer.Length, _buffer, 0, _buffer.Length);
                _writePos = 0;
                _filled = true;
                return;
            }

            int firstChunk = Math.Min(count, _buffer.Length - _writePos);
            Array.Copy(data, offset, _buffer, _writePos, firstChunk);
            int remaining = count - firstChunk;

            if (remaining > 0)
            {
                Array.Copy(data, offset + firstChunk, _buffer, 0, remaining);
                _writePos = remaining;
                _filled = true;
            }
            else
            {
                _writePos += firstChunk;
                if (_writePos == _buffer.Length)
                {
                    _writePos = 0;
                    _filled = true;
                }
            }
        }
    }

    /// <summary>Copia o conteúdo atual em ordem cronológica (mais antigo → mais recente).</summary>
    public byte[] Snapshot()
    {
        lock (_lock)
        {
            if (!_filled)
            {
                var partial = new byte[_writePos];
                Array.Copy(_buffer, 0, partial, 0, _writePos);
                return partial;
            }

            var result = new byte[_buffer.Length];
            int tail = _buffer.Length - _writePos; // parte mais antiga
            Array.Copy(_buffer, _writePos, result, 0, tail);
            Array.Copy(_buffer, 0, result, tail, _writePos);
            return result;
        }
    }
}
