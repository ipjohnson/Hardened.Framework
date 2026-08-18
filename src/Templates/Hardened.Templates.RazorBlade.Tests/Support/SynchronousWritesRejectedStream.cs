namespace Hardened.Templates.RazorBlade.Tests.Support;

/// <summary>
/// A response body that refuses synchronous writes, the way a real server's does.
/// </summary>
/// <remarks>
/// <para>
/// Kestrel's <c>HttpResponseStream</c> throws <c>InvalidOperationException: Synchronous
/// operations are disallowed</c> unless <c>AllowSynchronousIO</c> is turned on, and it is off by
/// default on both the Kestrel and the ASP.NET host. A <c>MemoryStream</c> accepts synchronous
/// writes happily, so a test that renders into one cannot tell the difference between an output
/// that is safe on a server and one that is not.
/// </para>
/// <para>
/// That gap is the whole reason the buffering defect shipped: the render was verified end to end
/// against a <c>MemoryStream</c>, so a view over <c>StreamWriter</c>'s 1 KiB buffer looked fine
/// here and returned an empty 200 in production.
/// </para>
/// </remarks>
public sealed class SynchronousWritesRejectedStream : Stream {
    private readonly MemoryStream _written = new();

    public byte[] ToArray() => _written.ToArray();

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _written.Length;

    public override long Position {
        get => _written.Position;
        set => throw new NotSupportedException();
    }

    private static Exception Rejected() =>
        new InvalidOperationException(
            "Synchronous operations are disallowed. Call WriteAsync or set AllowSynchronousIO " +
            "to true instead.");

    public override void Write(byte[] buffer, int offset, int count) => throw Rejected();

    public override void Write(ReadOnlySpan<byte> buffer) => throw Rejected();

    public override void WriteByte(byte value) => throw Rejected();

    public override void Flush() {
        // Kestrel allows a synchronous Flush that writes nothing; only writes are rejected.
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
        _written.WriteAsync(buffer, offset, count, token);

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer, CancellationToken token = default) =>
        _written.WriteAsync(buffer, token);

    public override Task FlushAsync(CancellationToken token) => _written.FlushAsync(token);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
