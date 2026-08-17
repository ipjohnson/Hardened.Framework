namespace Hardened.Web.Runtime.Attributes;

/// <summary>
/// Answers this handler's <c>IAsyncEnumerable&lt;T&gt;</c> as <c>text/event-stream</c> rather than
/// newline-delimited JSON.
/// </summary>
/// <remarks>
/// <para>
/// Framing only. The handler does not change: it still returns
/// <c>IAsyncEnumerable&lt;T&gt;</c>, still gets cancellation when the client disconnects, and is
/// still subject to the same filters, validation and serializers as any other route. What changes
/// is what goes around each item on the wire.
/// </para>
/// <example>
/// <code>
/// [Get("/orders/live")]
/// [ServerSentEvents]
/// public async IAsyncEnumerable&lt;OrderEvent&gt; Live() { … }
/// </code>
/// </example>
/// <para>
/// Yield <c>SseItem&lt;T&gt;</c> instead of a bare <c>T</c> to set an event's <c>id</c>,
/// <c>event</c> or <c>retry</c>. The id is the one that carries weight: a browser
/// <c>EventSource</c> reconnects on its own and sends the last id back as <c>Last-Event-ID</c>, so
/// a stream that sets ids can resume where it left off.
/// </para>
/// <para>
/// <b>It needs a host that can flush.</b> Kestrel, ASP.NET Core and the Lambda streaming runtime
/// all can. The buffered API Gateway runtime cannot - it accumulates the whole body before
/// returning, so events would arrive together at the end or not at all - and the generator reports
/// that at build time rather than leaving it to be found in an environment.
/// </para>
/// <para>
/// On a handler that does not return <c>IAsyncEnumerable&lt;T&gt;</c> this is a build error: there
/// is no stream to frame, and silently ignoring it would leave an author believing a buffered
/// response was an event stream.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public class ServerSentEventsAttribute : Attribute;
