using System.IO;

namespace FFPorter.Desktop.Services;

internal sealed class PartsStream : Stream
{
    private readonly FileStream?[] _parts;
    private readonly long[] _starts;
    private readonly long _length;
    private long _position;

    public PartsStream(IReadOnlyList<string> files)
    {
        _parts = new FileStream?[files.Count];
        _starts = new long[files.Count];
        try
        {
            for (int index = 0; index < files.Count; index++)
            {
                var part = new FileStream(files[index], FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
                _parts[index] = part;
                _starts[index] = _length;
                _length += part.Length;
            }
        }
        catch
        {
            Dispose(true);
            throw;
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int total = 0;
        while (buffer.Length > 0 && _position < _length)
        {
            int index = Array.BinarySearch(_starts, _position);
            if (index < 0)
                index = ~index - 1;
            while (index < _parts.Length - 1 && _position - _starts[index] >= _parts[index]!.Length)
                index++;
            FileStream part = _parts[index]!;
            long within = _position - _starts[index];
            part.Position = within;
            int read = part.Read(buffer[..(int)Math.Min(buffer.Length, part.Length - within)]);
            if (read <= 0)
                break;
            total += read;
            _position += read;
            buffer = buffer[read..];
        }
        return total;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => _length + offset,
        };
        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (FileStream? part in _parts)
                part?.Dispose();
        }
        base.Dispose(disposing);
    }
}
