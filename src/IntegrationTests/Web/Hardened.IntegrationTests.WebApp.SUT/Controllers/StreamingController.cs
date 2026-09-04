using System.Runtime.CompilerServices;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.Filters;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// Handlers returning <c>IAsyncEnumerable&lt;T&gt;</c>, which the pipeline answers as
/// newline-delimited JSON.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Models"/> is the handler that matters, and it is the one that did not exist.</b>
/// Streaming was covered only by <c>AsyncEnumerableIoFilterTests</c>, which constructs the filter
/// directly with a stand-in serializer and does so as <c>AsyncEnumerableIoFilter&lt;string&gt;</c>.
/// A string is the one item type <c>RawResponseSerializer</c> already answered for, so the suite
/// was green while every handler streaming a model threw at the serializer lookup. Covering a
/// model here, through the real pipeline, is what stops that returning.
/// </para>
/// <para>
/// <see cref="Strings"/> is kept alongside it because the two resolve through different serializers
/// - a string still goes to <c>RawResponseSerializer</c> - and only exercising the new one would
/// leave the path that used to work uncovered.
/// </para>
/// <para>
/// <see cref="Cancellable"/> covers the signature every C# author writes on an async iterator. A
/// handler does not need it - the filter already passes <c>context.CancellationToken</c> to
/// <c>WithCancellation</c> at the enumeration site, so a handler is cancellable without asking -
/// but it is what people write, and until <c>CancellationToken</c> bound by type it answered 500.
/// </para>
/// </remarks>
[BasePath("/streaming")]
public class StreamingController {

    public record Measurement(string Sensor, int Reading, bool Settled);

    /// <summary>A model per line, which is the case that used to throw.</summary>
    [Get("/models")]
    public async IAsyncEnumerable<Measurement> Models() {
        yield return new Measurement("north", 12, false);

        await Task.Yield();

        yield return new Measurement("south", 41, true);

        await Task.Yield();

        yield return new Measurement("east", -3, false);
    }

    /// <summary>The shape that already worked, so it keeps being checked.</summary>
    [Get("/strings")]
    public async IAsyncEnumerable<string> Strings() {
        yield return "alpha";

        await Task.Yield();

        yield return "beta";
    }

    /// <summary>
    /// The signature the C# compiler expects on a cancellable async iterator.
    /// </summary>
    /// <remarks>
    /// Two things had to be true for this to bind, and neither was: <c>CancellationToken</c> is a
    /// struct, so it fell past every branch to <c>Body</c> and the pipeline tried to deserialize a
    /// request body into it; and <c>[EnumeratorCancellation]</c> was unrecognised, so it was emitted
    /// as a custom binder and threw "does not implement ICustomBindingAttribute" before that.
    /// </remarks>
    [Get("/cancellable")]
    public async IAsyncEnumerable<Measurement> Cancellable(
        [EnumeratorCancellation] CancellationToken cancellationToken) {
        yield return new Measurement("west", 7, true);

        await Task.Yield();

        yield return new Measurement("centre", 19, false);
    }

    /// <summary>Server-sent events, framed rather than newline-delimited.</summary>
    [Get("/events")]
    [ServerSentEvents]
    public async IAsyncEnumerable<Measurement> Events() {
        yield return new Measurement("north", 12, false);

        await Task.Yield();

        yield return new Measurement("south", 41, true);
    }

    /// <summary>
    /// Events carrying the fields the protocol lets you send beside the payload.
    /// </summary>
    /// <remarks>
    /// The <c>id</c> is the one that matters: a browser <c>EventSource</c> reconnects on its own
    /// and sends the last one back as <c>Last-Event-ID</c>, so a stream that sets ids can resume.
    /// </remarks>
    [Get("/events-with-ids")]
    [ServerSentEvents]
    public async IAsyncEnumerable<SseItem<Measurement>> EventsWithIds() {
        yield return new SseItem<Measurement>(
            new Measurement("north", 12, false), Id: "1", Event: "reading", Retry: 5000);

        await Task.Yield();

        yield return new SseItem<Measurement>(new Measurement("south", 41, true), Id: "2");
    }

    /// <summary>An event stream that produces nothing.</summary>
    [Get("/events-empty")]
    [ServerSentEvents]
    public async IAsyncEnumerable<Measurement> EventsEmpty() {
        await Task.CompletedTask;

        yield break;
    }

    /// <summary>
    /// Produces nothing, so the trailing write is the entire body.
    /// </summary>
    /// <remarks>
    /// The filter writes a newline after the loop whether or not anything was produced, because
    /// Lambda Function URLs do not close a zero-byte body promptly and a reader waiting on one
    /// hangs. That behaviour has never had a test through the real pipeline.
    /// </remarks>
    [Get("/empty")]
    public async IAsyncEnumerable<Measurement> Empty() {
        await Task.CompletedTask;

        yield break;
    }

    #region reconnect, refusal, failure, retry and heartbeat

    private static readonly Measurement[] Readings = [
        new("north", 12, false),
        new("south", 41, true),
        new("east", -3, false),
        new("west", 7, true)
    ];

    /// <summary>
    /// The reconnect contract in one handler: resume after the event the client last saw, and end
    /// the subscription with a 204 once there is nothing more to say.
    /// </summary>
    /// <remarks>
    /// An <c>EventSource</c> sends <c>Last-Event-ID</c> on its own. This reads it, replays from the
    /// event after it, and answers the reconnect that arrives holding the last id with a 204 - the
    /// one status that makes the client stop. The 204 is decided inside the iterator, which is the
    /// natural place to write it and the case the filter has to get right: the status is set after
    /// the handler call returned, at the first <c>MoveNextAsync</c>.
    /// </remarks>
    [Get("/events-resume")]
    [ServerSentEvents]
    public async IAsyncEnumerable<SseItem<Measurement>> EventsResume(
        [FromHeader(KnownHeaders.LastEventId)] string? lastEventId,
        IExecutionContext context) {
        var after = int.TryParse(lastEventId, out var id) ? id : 0;

        if (after >= Readings.Length) {
            context.Response.Status = 204;

            yield break;
        }

        for (var i = after; i < Readings.Length; i++) {
            yield return new SseItem<Measurement>(Readings[i], Id: (i + 1).ToString());

            await Task.Yield();
        }
    }

    /// <summary>A guarded event stream, for what a refusal on one looks like on the wire.</summary>
    [Get("/events-guarded")]
    [ServerSentEvents]
    [AuthorizeGrants("events:read")]
    public async IAsyncEnumerable<Measurement> EventsGuarded() {
        yield return Readings[0];

        await Task.Yield();
    }

    /// <summary>The newline-delimited twin of <see cref="EventsGuarded"/>.</summary>
    [Get("/models-guarded")]
    [AuthorizeGrants("events:read")]
    public async IAsyncEnumerable<Measurement> ModelsGuarded() {
        yield return Readings[0];

        await Task.Yield();
    }

    /// <summary>
    /// Fails before its first event. The iterator has begun and nothing has reached the wire, so
    /// the failure is an error document rather than an empty stream.
    /// </summary>
    [Get("/events-fail-before-first")]
    [ServerSentEvents]
    public async IAsyncEnumerable<Measurement> EventsFailBeforeFirst() {
        await Task.Yield();

        if (ThrowNothingToStream()) {
            yield return Readings[0];
        }
    }

    /// <summary>Fails after its first event, which is already with the client.</summary>
    [Get("/events-fail-after-first")]
    [ServerSentEvents]
    public async IAsyncEnumerable<Measurement> EventsFailAfterFirst() {
        yield return Readings[0];

        await Task.Yield();

        if (ThrowNothingToStream()) {
            yield return Readings[1];
        }
    }

    /// <summary>
    /// How many times <see cref="EventsRetryAfterFirst"/> has been enumerated, across the process.
    /// </summary>
    /// <remarks>
    /// Static because the test cannot read the body: the failure escapes the harness the way an
    /// aborted connection escapes a host. What it can read is whether the enumeration ran once or
    /// was run again, which is the whole question.
    /// </remarks>
    public static int RetryAfterFirstEnumerations;

    /// <summary>
    /// Under <c>[Retry]</c>, yields one event and fails. The retry filter wraps the call that
    /// produced the sequence, not the enumeration, so the failure is not retried and the event is
    /// not duplicated.
    /// </summary>
    [Get("/events-retry-after-first")]
    [ServerSentEvents]
    [Retry(Attempts = 3, SleepTime = 0)]
    public async IAsyncEnumerable<Measurement> EventsRetryAfterFirst() {
        Interlocked.Increment(ref RetryAfterFirstEnumerations);

        yield return Readings[0];

        await Task.Yield();

        if (ThrowNothingToStream()) {
            yield return Readings[1];
        }
    }

    private int _retryCalls;

    /// <summary>
    /// Under <c>[Retry]</c>, throws on the first call and returns the sequence on the second. The
    /// call is what a retry covers, so the events arrive.
    /// </summary>
    /// <remarks>
    /// Not an iterator, on purpose: an iterator's body runs at enumeration, outside the retry
    /// fork. The controller is transient, so the count is per request, and both attempts share
    /// the instance - which is the documented cost of where <c>FilterOrder.Retry</c> sits.
    /// </remarks>
    [Get("/events-retry-call")]
    [ServerSentEvents]
    [Retry(Attempts = 3, SleepTime = 0)]
    public IAsyncEnumerable<Measurement> EventsRetryCall() {
        if (++_retryCalls == 1) {
            throw new InvalidOperationException("first call fails");
        }

        return Events();
    }

    /// <summary>
    /// Two events with a silence between them longer than the heartbeat interval a test sets.
    /// </summary>
    [Get("/events-slow")]
    [ServerSentEvents]
    public async IAsyncEnumerable<Measurement> EventsSlow(
        [EnumeratorCancellation] CancellationToken cancellationToken) {
        yield return Readings[0];

        await Task.Delay(100, cancellationToken);

        yield return Readings[1];
    }

    private static bool ThrowNothingToStream() =>
        throw new InvalidOperationException("nothing to stream");

    #endregion
}
