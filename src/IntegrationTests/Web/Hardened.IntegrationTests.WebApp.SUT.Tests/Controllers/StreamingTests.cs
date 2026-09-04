using Hardened.IntegrationTests.WebApp.SUT.Controllers;
using Hardened.IntegrationTests.WebApp.SUT.Tests.Support;
using Hardened.Requests.Abstract.Headers;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// Streamed responses driven through the real pipeline.
///
/// <para>
/// <b><see cref="AStreamOfModelsIsOneJsonDocumentPerLine"/> is the test this file exists for.</b>
/// <c>AsyncEnumerableIoFilterTests</c> covers the framing well, but it builds the filter directly
/// with a stand-in serializer and does so as <c>AsyncEnumerableIoFilter&lt;string&gt;</c>. Neither
/// choice is wrong for what that file asserts, and together they meant the serializer lookup was
/// never exercised: a string was already answerable by <c>RawResponseSerializer</c>, so the one
/// item type under test was the one that happened to work, and a handler streaming a model threw
/// with "Response committed to content type 'application/x-ndjson' but no registered serializer can
/// produce it".
/// </para>
///
/// <para>
/// So what these assert is deliberately the wiring rather than the framing: that a real handler
/// returning a real model reaches a serializer at all, that the content type on the wire is the
/// stream's rather than the per-item one, and that the empty case still terminates.
/// </para>
/// </summary>
public class StreamingTests {

    [HardenedTest]
    public async Task AStreamOfModelsIsOneJsonDocumentPerLine(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/models");

        response.Assert.Ok();

        var measurements = new List<StreamingController.Measurement>();

        await foreach (var measurement in
                       response.DeserializeAsyncEnumerable<StreamingController.Measurement>()) {
            measurements.Add(measurement);
        }

        Assert.Equal(3, measurements.Count);
        Assert.Equal(new StreamingController.Measurement("north", 12, false), measurements[0]);
        Assert.Equal(new StreamingController.Measurement("south", 41, true), measurements[1]);
        Assert.Equal(new StreamingController.Measurement("east", -3, false), measurements[2]);
    }

    /// <summary>
    /// The stream's content type, not the item's.
    /// </summary>
    /// <remarks>
    /// Every other JSON serializer assigns <c>application/json</c> on entry, so a stream routed
    /// through one would have each item overwrite what the filter committed. This is what pins the
    /// streaming serializer leaving the content type alone - and it is the property server-sent
    /// events will depend on outright, since an <c>EventSource</c> refuses anything that is not
    /// <c>text/event-stream</c>.
    /// </remarks>
    [HardenedTest]
    public async Task AStreamKeepsItsOwnContentType(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/models");

        response.Assert.Ok();

        Assert.Equal(KnownContentType.NdJson, response.Headers[KnownHeaders.ContentType].ToString());
    }

    /// <summary>
    /// Every line is a JSON document, including when the item is a string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a deliberate behaviour change and the assertion is the point of recording it. A
    /// stream of strings used to resolve to <c>RawResponseSerializer</c>, which writes a string's
    /// characters - so the body read <c>alpha\nbeta</c>, and neither line was a JSON document, in a
    /// format whose whole contract is one JSON document per line. No NDJSON reader could parse it.
    /// </para>
    /// <para>
    /// <c>StreamingJsonResponseSerializer</c> sits at <c>Specialized</c>, ahead of it, so the same
    /// handler now emits quoted strings and every line parses whatever the item type is. Nothing
    /// depended on the old shape - it was unreachable for any item type but string, and untested
    /// through a real response for that one.
    /// </para>
    /// </remarks>
    [HardenedTest]
    public async Task EvenAStreamOfStringsIsValidJsonPerLine(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/strings");

        response.Assert.Ok();

        var values = new List<string>();

        await foreach (var value in response.DeserializeAsyncEnumerable<string>()) {
            values.Add(value);
        }

        Assert.Equal(["alpha", "beta"], values);
    }

    /// <summary>
    /// The signature every C# author writes on a cancellable async iterator binds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It did not. <c>[EnumeratorCancellation] CancellationToken</c> answered 500 -
    /// "does not implement ICustomBindingAttribute" out of <c>ExecutionHelper</c> - because an
    /// unrecognised parameter attribute is emitted as a custom binder, and because
    /// <c>CancellationToken</c> is a struct that otherwise falls all the way through to being
    /// deserialized from the request body.
    /// </para>
    /// <para>
    /// A handler does not need the parameter to be cancellable; the filter passes
    /// <c>context.CancellationToken</c> at the enumeration site either way. This is here because
    /// the signature is the idiomatic one, and a streaming handler is exactly where somebody
    /// reaches for it.
    /// </para>
    /// </remarks>
    [HardenedTest]
    public async Task ACancellableIteratorSignatureBinds(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/cancellable");

        response.Assert.Ok();

        var measurements = new List<StreamingController.Measurement>();

        await foreach (var measurement in
                       response.DeserializeAsyncEnumerable<StreamingController.Measurement>()) {
            measurements.Add(measurement);
        }

        Assert.Equal(2, measurements.Count);
        Assert.Equal(new StreamingController.Measurement("west", 7, true), measurements[0]);
    }

    #region server-sent events

    private static async Task<string> BodyOf(TestWebResponse response) {
        response.Body.Position = 0;

        using var reader = new StreamReader(response.Body, leaveOpen: true);

        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// Each event is <c>data:</c> and a blank line, under <c>text/event-stream</c>.
    /// </summary>
    /// <remarks>
    /// The content type is half the assertion. A browser <c>EventSource</c> refuses a response
    /// whose content type is anything else, so a stream that framed correctly and answered
    /// <c>application/json</c> would be rejected before a single event was read.
    /// </remarks>
    [HardenedTest]
    public async Task EventsAreDataLinesSeparatedByBlankLines(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/events");

        response.Assert.Ok();

        Assert.Equal(
            KnownContentType.EventStream, response.Headers[KnownHeaders.ContentType].ToString());

        Assert.Equal(
            """
            data: {"sensor":"north","reading":12,"settled":false}

            data: {"sensor":"south","reading":41,"settled":true}


            """.ReplaceLineEndings("\n"),
            await BodyOf(response));
    }

    /// <summary>
    /// <c>id</c>, <c>event</c> and <c>retry</c> are written ahead of the payload, and the payload
    /// is what the handler wrapped rather than the wrapper.
    /// </summary>
    /// <remarks>
    /// Serializing the wrapper would put the id and the event name inside <c>data:</c> as well as
    /// beside it, which reads as working right up until a client dispatches on the event name and
    /// finds it in two places.
    /// </remarks>
    [HardenedTest]
    public async Task EventFieldsAreWrittenBesideThePayloadNotInsideIt(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/events-with-ids");

        response.Assert.Ok();

        Assert.Equal(
            """
            id: 1
            event: reading
            retry: 5000
            data: {"sensor":"north","reading":12,"settled":false}

            id: 2
            data: {"sensor":"south","reading":41,"settled":true}


            """.ReplaceLineEndings("\n"),
            await BodyOf(response));
    }

    /// <summary>
    /// An empty event stream is a comment line, not an empty body.
    /// </summary>
    /// <remarks>
    /// The protocol has no way to say "nothing happened", and a zero-byte body is the case Lambda
    /// Function URLs do not close promptly - a reader waiting on one hangs. A comment is the one
    /// thing every client is required to discard, so it costs three bytes and nothing else.
    /// </remarks>
    [HardenedTest]
    public async Task AnEmptyEventStreamSendsAComment(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/events-empty");

        response.Assert.Ok();

        Assert.Equal(":\n\n", await BodyOf(response));
    }

    /// <summary>
    /// A stream that produced events does not get a trailing anything.
    /// </summary>
    /// <remarks>
    /// Every event already ends with a blank line, so the stream is complete when the last one is
    /// written. Appending the empty-stream comment unconditionally would dispatch a spurious event
    /// at the end of every stream.
    /// </remarks>
    [HardenedTest]
    public async Task ANonEmptyEventStreamHasNoTrailingComment(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/events");

        Assert.DoesNotContain(":\n\n", await BodyOf(response));
    }

    #endregion

    #region reconnecting

    private static Action<TestWebRequest> WithHeader(string name, string value) =>
        request => request.Headers[name] = value;

    /// <summary>
    /// The client comes back with the last id it saw, and the handler resumes after it.
    /// </summary>
    [HardenedTest]
    public async Task AReconnectWithLastEventIdResumesAfterThatEvent(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(
            "/streaming/events-resume", WithHeader(KnownHeaders.LastEventId, "2"));

        response.Assert.Ok();

        Assert.Equal(
            """
            id: 3
            data: {"sensor":"east","reading":-3,"settled":false}

            id: 4
            data: {"sensor":"west","reading":7,"settled":true}


            """.ReplaceLineEndings("\n"),
            await BodyOf(response));
    }

    [HardenedTest]
    public async Task AReconnectWithNoLastEventIdStartsFromTheBeginning(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/events-resume");

        response.Assert.Ok();

        var body = await BodyOf(response);

        Assert.StartsWith("id: 1\n", body);
        Assert.Contains("id: 4\n", body);
    }

    /// <summary>
    /// A 204 is the one answer that makes an <c>EventSource</c> stop reconnecting, and it has to
    /// be a real 204: no content type, no completion comment. Kestrel aborts a 204 that carries a
    /// body, and the client reads an abort as a network error and reconnects from it.
    /// </summary>
    [HardenedTest]
    public async Task AReconnectPastTheLastEventIsA204WithNoBody(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(
            "/streaming/events-resume", WithHeader(KnownHeaders.LastEventId, "4"));

        Assert.Equal(204, response.StatusCode);
        Assert.False(response.Headers.ContainsKey(KnownHeaders.ContentType));
        Assert.Equal("", await BodyOf(response));
    }

    /// <summary>
    /// API Gateway payload 2.0 and a function URL deliver header names in lower case.
    /// </summary>
    [HardenedTest]
    public async Task TheLastEventIdHeaderIsReadCaseInsensitively(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(
            "/streaming/events-resume", WithHeader("last-event-id", "3"));

        response.Assert.Ok();

        Assert.Equal(
            """
            id: 4
            data: {"sensor":"west","reading":7,"settled":true}


            """.ReplaceLineEndings("\n"),
            await BodyOf(response));
    }

    #endregion

    #region refusals and failures

    private static Action<TestWebRequest> AsAnEventSource() =>
        request => request.Headers[KnownHeaders.Accept] = KnownContentType.EventStream;

    /// <summary>
    /// A refusal on an event-stream route leaves as the refusal's status with a JSON body, which
    /// is what makes an <c>EventSource</c> stop: any status other than 200, or any content type
    /// other than <c>text/event-stream</c>, fails the connection for good. <c>text/event-stream</c>
    /// around a JSON error would be parsed as garbage and reconnected to forever.
    /// </summary>
    /// <remarks>
    /// The request carries <c>Accept: text/event-stream</c> exactly as a browser sends it, because
    /// that is the header the negotiation has to look past. Reading the code said the default
    /// serializer catches this; this is the run.
    /// </remarks>
    [HardenedTest]
    public async Task ARefusalOnAnSseRouteIsJsonNotAHalfOpenEventStream(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/events-guarded", AsAnEventSource());

        response.Assert.Unauthorized();

        Assert.StartsWith(KnownContentType.Json, response.Headers[KnownHeaders.ContentType].ToString());

        var body = await response.ReadTextAsync();

        Assert.StartsWith("{", body);
        Assert.DoesNotContain("data:", body);
        Assert.DoesNotContain(":\n\n", body);
    }

    /// <summary>The newline-delimited twin: no framing, no trailing newline, just the error.</summary>
    [HardenedTest]
    public async Task ARefusalOnAStreamedRouteDoesNotEmitTheFramingPrologue(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/models-guarded");

        response.Assert.Unauthorized();

        Assert.StartsWith(KnownContentType.Json, response.Headers[KnownHeaders.ContentType].ToString());

        var body = await response.ReadTextAsync();

        Assert.StartsWith("{", body);
        Assert.EndsWith("}", body);
    }

    /// <summary>
    /// A throw before the first event has nothing on the wire yet, so there is still a whole
    /// response to answer with, and it is the same error document a buffered handler's throw gets.
    /// </summary>
    [HardenedTest]
    public async Task AFailureBeforeTheFirstEventIsAnErrorDocument(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/events-fail-before-first", AsAnEventSource());

        Assert.Equal(500, response.StatusCode);
        Assert.StartsWith(KnownContentType.Json, response.Headers[KnownHeaders.ContentType].ToString());

        var body = await response.ReadTextAsync();

        Assert.StartsWith("{", body);
        Assert.DoesNotContain("data:", body);
    }

    /// <summary>
    /// A throw after the first event ends the stream. The bytes are with the client, so the only
    /// honest answer is to stop; on the in-memory harness the failure reaches the caller the way an
    /// aborted connection reaches a host, and a client sees the connection end and comes back with
    /// <c>Last-Event-ID</c>.
    /// </summary>
    [HardenedTest]
    public async Task AFailureAfterTheFirstEventEndsTheStream(ITestWebApp testWebApp) {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            testWebApp.Get("/streaming/events-fail-after-first", AsAnEventSource()));

        Assert.Equal("nothing to stream", failure.Message);
    }

    #endregion

    #region retry

    /// <summary>
    /// <c>[Retry]</c> wraps the call that produces the sequence; the enumeration runs outside it,
    /// in the filter that writes the items. So a failure after the first event is not retried, the
    /// event is not written twice, and the failure ends the stream as it would without the
    /// attribute. Row 14 of the test-gap findings suspected a duplicate here; this pins that the
    /// construction rules it out.
    /// </summary>
    [HardenedTest]
    public async Task ARetryAfterTheFirstItemIsWrittenDoesNotDuplicateIt(ITestWebApp testWebApp) {
        var before = StreamingController.RetryAfterFirstEnumerations;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            testWebApp.Get("/streaming/events-retry-after-first"));

        Assert.Equal(1, StreamingController.RetryAfterFirstEnumerations - before);
    }

    /// <summary>
    /// What a retry does cover on a streaming handler: the call. A handler that fails to produce
    /// the sequence is called again, and the events arrive.
    /// </summary>
    [HardenedTest]
    public async Task ARetryOnAStreamingHandlerRetriesTheCall(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/events-retry-call");

        response.Assert.Ok();

        Assert.Equal(
            """
            data: {"sensor":"north","reading":12,"settled":false}

            data: {"sensor":"south","reading":41,"settled":true}


            """.ReplaceLineEndings("\n"),
            await BodyOf(response));
    }

    #endregion

    #region heartbeat and headers

    /// <summary>
    /// A stream quiet for longer than the interval carries a comment line between its events, so
    /// an intermediary that cuts idle responses sees bytes. At least one rather than a count: the
    /// handler waits 100 ms at a 10 ms interval, and how many timers fire in that window is the
    /// scheduler's business.
    /// </summary>
    [HardenedTest]
    [HeartbeatEvery(10)]
    public async Task AHeartbeatArrivesBetweenSlowEvents(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/events-slow");

        response.Assert.Ok();

        var body = await BodyOf(response);

        Assert.StartsWith("data: {\"sensor\":\"north\"", body);
        Assert.Contains(": keep-alive\n\n", body);
        Assert.EndsWith("data: {\"sensor\":\"south\",\"reading\":41,\"settled\":true}\n\n", body);
        Assert.DoesNotContain(":\n\n", body);
    }

    /// <summary>
    /// <c>Cache-Control: no-cache</c> keeps a shared cache out of the path, and
    /// <c>X-Accel-Buffering: no</c> turns off nginx's per-response buffering. Both are inert
    /// where they do not apply.
    /// </summary>
    [HardenedTest]
    public async Task AnEventStreamCarriesNoCacheAndNoAccelBuffering(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/events");

        response.Assert.Ok();

        Assert.Equal("no-cache", response.Headers[KnownHeaders.CacheControl].ToString());
        Assert.Equal("no", response.Headers[KnownHeaders.XAccelBuffering].ToString());
    }

    /// <summary>A newline-delimited response is an ordinary representation, and says nothing.</summary>
    [HardenedTest]
    public async Task ANewlineDelimitedStreamCarriesNeitherHeader(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/models");

        response.Assert.Ok();

        Assert.False(response.Headers.ContainsKey(KnownHeaders.CacheControl));
        Assert.False(response.Headers.ContainsKey(KnownHeaders.XAccelBuffering));
    }

    #endregion

    /// <summary>
    /// A stream that produces nothing is a newline, not an empty body.
    /// </summary>
    /// <remarks>
    /// The trailing write exists because Lambda Function URLs do not close a zero-byte body
    /// promptly and a reader waiting on one hangs. It has been in the filter from the start and has
    /// never been asserted through a real response until now.
    /// </remarks>
    [HardenedTest]
    public async Task AnEmptyStreamStillTerminates(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/empty");

        response.Assert.Ok();

        // Through the decoded accessor: the application compresses NDJSON, and a terminator is a
        // body like any other.
        Assert.Equal("\n", await response.ReadTextAsync());
    }
}
