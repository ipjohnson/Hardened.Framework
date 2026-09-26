using System.Text;

namespace Hardened.Requests.Runtime.Tests.Support;

/// <summary>
/// A response body that keeps each write it is handed as a write of its own, so a test can count
/// the writes a transport saw rather than only what they added up to.
/// </summary>
/// <remarks>
/// Kestrel frames every write to a chunked response as a chunk of its own, so the number of writes
/// is what the wire carries. A <c>MemoryStream</c> joins them and cannot show it.
/// </remarks>
public sealed class RecordingTransport : Stream
{
    private readonly MemoryStream _written = new();
    private readonly List<string> _writes = [];

    /// <summary>Called as each write arrives, before anything is recorded.</summary>
    public Action? OnWrite { get; init; }

    /// <summary>The write, counting from one, that fails the way a reset connection does.</summary>
    public int FailOnWrite { get; init; }

    /// <summary>When set, each asynchronous write finishes only once this has.</summary>
    public Task? Gate { get; init; }

    public IReadOnlyList<string> Writes => _writes;

    public int Flushes { get; private set; }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _written.Length;

    public override long Position
    {
        get => _written.Position;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        Record(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer) => Record(buffer);

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    ) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        Record(buffer.Span);

        if (Gate != null)
        {
            await Gate;
        }
    }

    public override void Flush() => Flushes++;

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        Flushes++;

        return Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    private void Record(ReadOnlySpan<byte> bytes)
    {
        OnWrite?.Invoke();

        if (_writes.Count + 1 == FailOnWrite)
        {
            throw new IOException("The connection was reset.");
        }

        _written.Write(bytes);
        _writes.Add(Encoding.UTF8.GetString(bytes));
    }
}
