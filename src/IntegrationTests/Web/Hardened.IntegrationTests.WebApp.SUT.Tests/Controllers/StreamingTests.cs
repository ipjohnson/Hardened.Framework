using Hardened.IntegrationTests.WebApp.SUT.Controllers;
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

        response.Body.Position = 0;

        using var reader = new StreamReader(response.Body, leaveOpen: true);

        Assert.Equal("\n", await reader.ReadToEndAsync());
    }
}
