using System.Runtime.CompilerServices;
using Hardened.Requests.Abstract.Serializer;
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
}
