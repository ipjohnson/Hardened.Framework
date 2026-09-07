using System.Text.Json;

namespace Hardened.Aws.Lambda.Runtime.Execution;

/// <summary>
/// One invocation's bytes, and the parsed document if anything asks for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two views of the same bytes, for two different jobs.</b> <see cref="Raw"/> is what an adapter
/// binds its event from and what a direct invocation uses as its request body.
/// <see cref="Json"/> exists only so a function serving several sources can ask each adapter whether
/// it recognises the payload, which is a property lookup rather than a deserialize.
/// </para>
/// <para>
/// <b>The parse is lazy because two of the three families must not pay for it.</b> A function
/// serving one source has nothing to discriminate, so it never touches <see cref="Json"/>: the HTTP
/// family goes straight from <see cref="Raw"/> to its proxy request, and the invoke family never
/// parses at all. Only the event family, which is the one that was going to read the payload
/// anyway, pays for the document.
/// </para>
/// <para>
/// <b>The document outlives nothing.</b> <see cref="JsonDocument"/> rents pooled arrays, so a
/// <see cref="JsonElement"/> is a window into memory that returns to the pool on
/// <see cref="Dispose"/>. Adapters bind from <see cref="Raw"/> precisely so that nothing downstream
/// holds one - the elements never leave <c>Handles</c>, and a request object owns its own strings.
/// </para>
/// </remarks>
public sealed class LambdaPayload : IDisposable {
    private readonly ReadOnlyMemory<byte> _raw;
    private JsonDocument? _document;

    public LambdaPayload(ReadOnlyMemory<byte> raw) {
        _raw = raw;
    }

    /// <summary>The invocation exactly as it arrived. No parse, no copy.</summary>
    public ReadOnlyMemory<byte> Raw => _raw;

    /// <summary>
    /// The payload as a document, parsed on first use, for deciding which adapter owns it.
    /// </summary>
    /// <exception cref="JsonException">
    /// The payload is not JSON. That is a legitimate direct invocation - a caller may send anything
    /// - so the invoke adapter reads <see cref="Raw"/> and never asks for this.
    /// </exception>
    public JsonElement Json => (_document ??= JsonDocument.Parse(_raw)).RootElement;

    /// <summary>
    /// The bytes as a stream, for a request whose body is the payload itself.
    /// </summary>
    /// <remarks>
    /// Non-writable and over the same memory rather than a copy, so a direct invocation costs one
    /// allocation for the wrapper and nothing for the bytes.
    /// </remarks>
    public Stream AsStream() => new ReadOnlyMemoryStream(_raw);

    public void Dispose() {
        _document?.Dispose();
        _document = null;
    }

    /// <summary>The first element of a <c>Records</c> array, or null if there is not one.</summary>
    /// <remarks>
    /// <para>
    /// Here rather than on an adapter because every record-array source needs it and they are in
    /// different packages now - SQS, SNS, DynamoDB Streams, Kinesis and S3 all arrive as a
    /// <c>Records</c> array.
    /// </para>
    /// <para>
    /// <b>Which is exactly why this is not a recognition on its own.</b> An adapter matching on the
    /// array claims all five and whichever is asked first wins, so a caller reads the first record's
    /// event source and compares the <em>value</em>: <c>aws:sqs</c>, <c>aws:dynamodb</c>,
    /// <c>aws:kinesis</c>, <c>aws:s3</c>. SNS spells the property <c>EventSource</c> and the rest
    /// spell it <c>eventSource</c>, which is the trap this comment exists to keep in one place.
    /// </para>
    /// <para>
    /// An empty array returns null rather than throwing. AWS does not invoke with an empty batch, and
    /// a payload no adapter claims is an error the dispatcher raises by name.
    /// </para>
    /// </remarks>
    public static JsonElement? FirstRecord(JsonElement payload) {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("Records", out var records) ||
            records.ValueKind != JsonValueKind.Array ||
            records.GetArrayLength() == 0) {
            return null;
        }

        var first = records[0];

        return first.ValueKind == JsonValueKind.Object ? first : null;
    }

    /// <summary>
    /// A read-only <see cref="Stream"/> over <see cref="ReadOnlyMemory{T}"/>.
    /// </summary>
    /// <remarks>
    /// <c>MemoryStream</c> cannot wrap <c>ReadOnlyMemory</c> without copying, and the payload can be
    /// six megabytes on a synchronous invocation. Reading is all the pipeline does with a body.
    /// </remarks>
    private sealed class ReadOnlyMemoryStream : Stream {
        private readonly ReadOnlyMemory<byte> _memory;
        private int _position;

        public ReadOnlyMemoryStream(ReadOnlyMemory<byte> memory) {
            _memory = memory;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _memory.Length;

        public override long Position {
            get => _position;
            set => _position = checked((int)value);
        }

        public override int Read(Span<byte> buffer) {
            var remaining = _memory.Length - _position;

            if (remaining <= 0) {
                return 0;
            }

            var count = Math.Min(remaining, buffer.Length);

            _memory.Span.Slice(_position, count).CopyTo(buffer);
            _position += count;

            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override long Seek(long offset, SeekOrigin origin) {
            var target = origin switch {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _memory.Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

            if (target < 0 || target > _memory.Length) {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            _position = (int)target;

            return _position;
        }

        public override void Flush() { }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
