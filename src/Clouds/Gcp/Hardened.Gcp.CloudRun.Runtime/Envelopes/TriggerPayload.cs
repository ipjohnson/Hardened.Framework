using System.Text.Json;

namespace Hardened.Gcp.CloudRun.Runtime.Envelopes;

/// <summary>
/// One delivery's body, and the parsed document if an envelope asks for it.
/// </summary>
/// <remarks>
/// <para>
/// Two views of the same bytes, for two different envelopes. <see cref="Raw"/> is what a binary
/// CloudEvent hands on as its data and what a request nothing unwrapped gets back as its body.
/// <see cref="Json"/> exists for the envelopes that are JSON - a push, a structured CloudEvent - so
/// that two of them asked about one body parse it once.
/// </para>
/// <para>
/// The parse is lazy and its failure is an answer rather than an error: a body that is not JSON is
/// not a JSON envelope, which is something an envelope wants to know rather than catch. The
/// document rents pooled memory, so an envelope copies what it keeps and the front door disposes
/// this as soon as the envelopes have answered.
/// </para>
/// </remarks>
public sealed class TriggerPayload : IDisposable {
    private readonly ReadOnlyMemory<byte> _raw;
    private JsonDocument? _document;
    private bool _parsed;

    public TriggerPayload(ReadOnlyMemory<byte> raw) {
        _raw = raw;
    }

    /// <summary>The body exactly as it arrived. No parse, no copy.</summary>
    public ReadOnlyMemory<byte> Raw => _raw;

    /// <summary>
    /// The body as a JSON document, parsed on first use, or null when it is not one.
    /// </summary>
    public JsonElement? Json {
        get {
            if (!_parsed) {
                _parsed = true;

                try {
                    _document = JsonDocument.Parse(_raw);
                }
                catch (JsonException) {
                    _document = null;
                }
            }

            return _document?.RootElement;
        }
    }

    /// <summary>
    /// The bytes as a stream, for a request whose body is the payload itself. Seekable, read-only,
    /// and over the same memory rather than a copy.
    /// </summary>
    public Stream AsStream() => new ReadOnlyMemoryStream(_raw);

    public void Dispose() {
        _document?.Dispose();
        _document = null;
    }

    /// <summary>
    /// A read-only <see cref="Stream"/> over <see cref="ReadOnlyMemory{T}"/>.
    /// </summary>
    /// <remarks>
    /// <c>MemoryStream</c> cannot wrap <c>ReadOnlyMemory</c> without copying, and a push body can
    /// be thirteen megabytes. Reading is all the pipeline does with a body.
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
