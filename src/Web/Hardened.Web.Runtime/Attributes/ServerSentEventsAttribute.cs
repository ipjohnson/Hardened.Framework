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
/// <b>The reconnect contract.</b> The client reconnects on its own after the stream ends or the
/// connection drops, sending <c>Last-Event-ID</c>, and stops on a 204 or on any status other than
/// 200. A handler that sets ids binds the header with
/// <c>[FromHeader(KnownHeaders.LastEventId)] string? lastEventId</c>, replays from the event after
/// it, and - when the subscription is over for good - takes <c>IExecutionContext</c>, sets
/// <c>context.Response.Status = 204</c> and yields nothing. The pipeline writes no framing and no
/// body for that 204. Decide it before the first item: a 204 after an event or a heartbeat has
/// gone out is a 204 with a body, which the host refuses.
/// </para>
/// <example>
/// <code>
/// [Get("/orders/live")]
/// [ServerSentEvents]
/// public async IAsyncEnumerable&lt;SseItem&lt;OrderEvent&gt;&gt; Live(
///     [FromHeader(KnownHeaders.LastEventId)] string? lastEventId,
///     IExecutionContext context) { … }
/// </code>
/// </example>
/// <para>
/// <b>Refusals, failures and retries.</b> A refusal on the route - authorization, binding, a throw
/// before the first event - leaves as its own status with a JSON body, and the client stops. A
/// failure after the first event ends the stream, and the client comes back with
/// <c>Last-Event-ID</c>. <c>[Retry]</c> retries the call that produces the sequence and never the
/// enumeration, so an event is never duplicated by a retry.
/// </para>
/// <para>
/// <b>Quiet streams are kept open.</b> A stream that is silent for longer than the heartbeat
/// interval, fifteen seconds by default, carries a comment line the client discards, so an
/// intermediary that cuts idle responses sees bytes. Every event stream also carries
/// <c>Cache-Control: no-cache</c> and <c>X-Accel-Buffering: no</c> unless the handler set them.
/// <c>services.ConfigureStreaming</c> changes the interval; zero turns the heartbeat off.
/// </para>
/// <para>
/// <b>It needs a host that can flush.</b> Kestrel, ASP.NET Core and the Lambda streaming runtime
/// all can. The buffered API Gateway runtime cannot - it accumulates the whole body before
/// returning, so events would arrive together at the end or not at all - and the generator reports
/// that at build time rather than leaving it to be found in an environment.
/// </para>
/// <para>
/// On a handler that does not return <c>IAsyncEnumerable&lt;T&gt;</c> this is a build error,
/// <c>HRDW004</c>: there is no stream to frame, and silently ignoring it would leave an author
/// believing a buffered response was an event stream.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public class ServerSentEventsAttribute : Attribute;
