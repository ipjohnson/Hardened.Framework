using System.Globalization;
using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// Server-sent events: <c>data:</c> and a blank line, with the optional fields ahead of it.
/// </summary>
/// <remarks>
/// <para>
/// The wire format is line-oriented text, and the only structure is that a blank line dispatches
/// the event. So each item is written as its optional fields, then <c>data: </c>, then whatever the
/// serializer produced, then <c>\n\n</c>.
/// </para>
/// <para>
/// <b>The payload is serialized straight into the body rather than buffered and escaped.</b> That
/// is safe for JSON and only for JSON: a <c>data:</c> value may not contain a newline, and JSON
/// escapes every newline inside strings, so a serialized JSON document is always one line. A
/// framing that wanted to send something else would have to fold the payload across
/// <c>data:</c> lines, which is why <see cref="ContentType"/> resolves to a JSON serializer and
/// nothing else claims it.
/// </para>
/// <para>
/// <b>Every write is asynchronous.</b> Kestrel's response body refuses a synchronous
/// <c>Write</c> unless the host turns <c>AllowSynchronousIO</c> on, and neither host does - so a
/// framing that wrote its prefix synchronously answered 500 on a real socket while passing every
/// test over a <c>MemoryStream</c>, which accepts either.
/// </para>
/// </remarks>
public class SseFraming : IStreamFraming {
    /// <summary>The one instance, because it holds nothing.</summary>
    public static readonly SseFraming Instance = new();

    private static readonly byte[] DataPrefix = "data: "u8.ToArray();
    private static readonly byte[] EventTerminator = "\n\n"u8.ToArray();

    /// <summary>
    /// A comment line, which a client is required to ignore.
    /// </summary>
    /// <remarks>
    /// What an empty stream ends with. The protocol has no way to say "nothing happened", and a
    /// zero-byte body is the case Lambda Function URLs do not close promptly - so an empty stream
    /// sends one line every client discards rather than nothing at all. A heartbeat counts: once
    /// any byte is on the wire the hang cannot occur, so a stream that heartbeated and then ended
    /// writes no second comment.
    /// </remarks>
    private static readonly byte[] EmptyStreamComment = ":\n\n"u8.ToArray();

    /// <summary>
    /// The heartbeat, which is also a comment line and also discarded.
    /// </summary>
    /// <remarks>
    /// Not the bare <c>:</c> that ends an empty stream. A test asserting that a stream carries no
    /// trailing comment looks for that exact sequence, and a heartbeat landing in a slow run would
    /// fail it for the wrong reason. The text is for whoever reads the raw stream.
    /// </remarks>
    private static readonly byte[] Heartbeat = ": keep-alive\n\n"u8.ToArray();

    public string ContentType => KnownContentType.EventStream;

    public async ValueTask WriteItem(
        IExecutionContext context, Func<IExecutionContext, Task> serialize) {
        var body = context.Response.Body;
        var cancellationToken = context.CancellationToken;

        if (context.Response.ResponseValue is ISseEvent metadata) {
            await WriteField(body, "id", metadata.Id, cancellationToken);
            await WriteField(body, "event", metadata.Event, cancellationToken);
            await WriteField(body, "retry",
                metadata.Retry?.ToString(CultureInfo.InvariantCulture), cancellationToken);

            // The payload is what the handler yielded, not the wrapper around it - serializing the
            // wrapper would put the id and the event name inside data as well as beside it.
            context.Response.ResponseValue = metadata.Data;
        }

        await body.WriteAsync(DataPrefix, 0, DataPrefix.Length, cancellationToken);

        await serialize(context);

        await body.WriteAsync(EventTerminator, 0, EventTerminator.Length, cancellationToken);
    }

    public async ValueTask WriteCompletion(IExecutionContext context) {
        // Only when nothing was written, a heartbeat included. Every event already ends with a
        // blank line, so a stream that produced anything is complete, and adding to it would
        // dispatch an empty event.
        if (NothingWritten(context)) {
            await context.Response.Body.WriteAsync(
                EmptyStreamComment, 0, EmptyStreamComment.Length, context.CancellationToken);
        }
    }

    /// <summary>
    /// Whether the stream has put nothing on the wire yet.
    /// </summary>
    /// <remarks>
    /// Asked of the body's position where the body can answer - a buffer the cache or a test
    /// holds - and of the response otherwise. Kestrel's response body throws
    /// <c>NotSupportedException</c> from <c>Position</c>, so reading it unconditionally ended
    /// every event stream on a real socket with a logged fault after the last event had already
    /// gone out. On a transport the filter flushes after every item and every heartbeat, so a
    /// response that has started is one that carried something.
    /// </remarks>
    private static bool NothingWritten(IExecutionContext context) {
        var body = context.Response.Body;

        return body.CanSeek ? body.Position == 0 : !context.Response.ResponseStarted;
    }

    public async ValueTask<bool> WriteHeartbeat(IExecutionContext context) {
        await context.Response.Body.WriteAsync(
            Heartbeat, 0, Heartbeat.Length, context.CancellationToken);

        return true;
    }

    /// <summary>
    /// One <c>name: value</c> line, or nothing when the value is absent.
    /// </summary>
    /// <remarks>
    /// A newline in one of these would end the field and start another, so a value carrying one is
    /// dropped rather than written. Only <c>id</c> and <c>event</c> can carry arbitrary text, both
    /// come from the application, and neither has a legitimate reason to span lines - the
    /// specification says an id containing U+0000 must be ignored and says nothing useful about
    /// newlines, which is exactly the gap somebody eventually puts a header value into.
    /// </remarks>
    private static async ValueTask WriteField(
        Stream body, string name, string? value, CancellationToken cancellationToken) {
        if (string.IsNullOrEmpty(value) ||
            value!.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0) {
            return;
        }

        var line = Encoding.UTF8.GetBytes(name + ": " + value + "\n");

        await body.WriteAsync(line, 0, line.Length, cancellationToken);
    }
}
