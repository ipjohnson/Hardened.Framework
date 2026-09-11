using Hardened.IntegrationTests.WebApp.SUT.Controllers;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Validation;
using Hardened.Requests.Serializers.MessagePack;
using Hardened.Web.Runtime.Responses;
using MessagePack;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// MessagePack through the real pipeline: the client's <c>Accept</c> deciding between two
/// representations, the operation that offers one, and a MessagePack request body.
/// </summary>
/// <remarks>
/// This fixture imports <c>[MessagePackSerializerLibrary]</c>, so the serializer is registered the
/// way an application registers it rather than constructed by the test.
/// </remarks>
public class MessagePackTests {

    private static MessagePackController.Reading Read(TestWebResponse response) {
        response.Body.Position = 0;

        return MessagePackSerializer.Deserialize<MessagePackController.Reading>(
            response.Body, ContractlessOrGenerated);
    }

    /// <summary>
    /// The options the package installs, reached from outside it the way any client would: the
    /// generated formatter for <c>Reading</c> is in the SUT's compilation, and
    /// <c>SourceGeneratedFormatterResolver</c> finds it.
    /// </summary>
    private static readonly MessagePackSerializerOptions ContractlessOrGenerated =
        MessagePackSerializerOptions.Standard;

    private static string ContentType(TestWebResponse response) =>
        response.Headers[KnownHeaders.ContentType].ToString();

    // ---------------------------------------------------------------- negotiated

    /// <summary>
    /// The operation declares JSON first, so a client expressing no preference is answered with it.
    /// </summary>
    [HardenedTest]
    public async Task AClientWithNoPreferenceGetsTheTypeTheOperationLeadsWith(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/msgpack/negotiated/3");

        response.Assert.Ok();

        Assert.StartsWith(KnownContentType.Json, ContentType(response));
    }

    [HardenedTest]
    public async Task AClientAskingForMessagePackGetsMessagePack(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(
            "/msgpack/negotiated/3",
            request => request.Headers[KnownHeaders.Accept] = MessagePackContentType.Value);

        response.Assert.Ok();

        Assert.StartsWith(MessagePackContentType.Value, ContentType(response));
        Assert.Equal(new MessagePackController.Reading("sensor-3", 9), Read(response));
    }

    /// <summary>
    /// The same operation, the same handler, a different representation. Which is the whole point:
    /// nothing about the handler says which one it answered with.
    /// </summary>
    [HardenedTest]
    public async Task TheSameOperationAnswersJsonForAClientThatAsksForIt(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(
            "/msgpack/negotiated/3",
            request => request.Headers[KnownHeaders.Accept] = KnownContentType.Json);

        response.Assert.Ok();

        Assert.StartsWith(KnownContentType.Json, ContentType(response));
        Assert.Contains("sensor-3", await response.ReadTextAsync());
    }

    // ---------------------------------------------------------------- one declared type

    /// <summary>
    /// One declared media type binds its serializer when the pipeline is composed rather than
    /// negotiating per request, and the response still carries the content type - which nothing on
    /// that path assigns but the serializer itself.
    /// </summary>
    [HardenedTest]
    public async Task AnOperationDeclaringMessagePackAloneAnswersIt(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/msgpack/packed/4");

        response.Assert.Ok();

        Assert.StartsWith(MessagePackContentType.Value, ContentType(response));
        Assert.Equal(new MessagePackController.Reading("sensor-4", 12), Read(response));
    }

    // ---------------------------------------------------------------- the request side

    /// <summary>
    /// No attribute declares this. A deserializer is chosen by the inbound <c>Content-Type</c>, so
    /// an operation reads MessagePack because the client sent it.
    /// </summary>
    [HardenedTest]
    public async Task AMessagePackBodyIsRead(ITestWebApp testWebApp) {
        var body = MessagePackSerializer.Serialize(
            new MessagePackController.Reading("sensor-9", 41), ContractlessOrGenerated);

        var response = await testWebApp.Request(
            "POST", null, "/msgpack/round",
            request => {
                request.Headers[KnownHeaders.ContentType] = MessagePackContentType.Value;
                request.Headers[KnownHeaders.Accept] = MessagePackContentType.Value;
                request.Body = body;
            });

        response.Assert.Ok();

        Assert.Equal(new MessagePackController.Reading("sensor-9", 42), Read(response));
    }

    /// <summary>
    /// And a JSON body still reads on the same operation, because the reader is chosen per request
    /// rather than declared once.
    /// </summary>
    [HardenedTest]
    public async Task AJsonBodyStillReadsOnTheSameOperation(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            new MessagePackController.Reading("sensor-9", 41), "/msgpack/round");

        response.Assert.Ok();

        Assert.Contains("sensor-9", await response.ReadTextAsync());
    }

    // ---------------------------------------------------------------- refusals

    /// <summary>
    /// A refusal on a MessagePack operation is answered as MessagePack, because the document says
    /// it is.
    /// </summary>
    /// <remarks>
    /// It answered <b>500 with an empty body</b>. <c>ExceptionResponseSerializer</c> negotiates the
    /// error body through the same locator the success goes through, so the MessagePack writer was
    /// chosen, found no formatter for <c>RequestValidationError</c> - a framework type that cannot
    /// carry <c>[MessagePackObject]</c> - and threw inside the handler that exists to answer a
    /// throw. Every bind failure, validation failure and authorization refusal on a MessagePack
    /// operation went out that way. See <c>ErrorEnvelopeFormatters</c>.
    /// </remarks>
    [HardenedTest]
    public async Task ARefusalIsAnsweredAsMessagePack(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(
            "/msgpack/packed/notanumber",
            request => request.Headers[KnownHeaders.Accept] = MessagePackContentType.Value);

        Assert.Equal(400, response.StatusCode);
        Assert.StartsWith(MessagePackContentType.Value, ContentType(response));

        response.Body.Position = 0;

        var error = MessagePackSerializer.Deserialize<RequestValidationError>(
            response.Body,
            MessagePackSerializerOptions.Standard.WithResolver(HardenedFormatterResolver.Instance));

        Assert.NotEmpty(error.Type);
        Assert.NotEmpty(error.Errors);
        Assert.Contains(error.Errors, field => field.Field == "id");
    }

    // ---------------------------------------------------------------- a declared 404

    /// <summary>
    /// A declared status whose body is one of the framework's own response types, answered as
    /// MessagePack.
    /// </summary>
    /// <remarks>
    /// <c>NotFound</c> lives in <c>Hardened.Web.Runtime</c> and cannot carry
    /// <c>[MessagePackObject]</c>, so the writer used to be asked for a formatter it did not have -
    /// which is why the media type could only be declared on an operation with one outcome.
    /// <c>HardenedFormatterResolver</c> answers for it now.
    /// </remarks>
    [HardenedTest]
    public async Task ADeclaredNotFoundIsAnsweredAsMessagePack(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(
            "/msgpack/declared/500",
            request => request.Headers[KnownHeaders.Accept] = MessagePackContentType.Value);

        Assert.Equal(404, response.StatusCode);
        Assert.StartsWith(MessagePackContentType.Value, ContentType(response));

        response.Body.Position = 0;

        var notFound = MessagePackSerializer.Deserialize<NotFound>(
            response.Body,
            MessagePackSerializerOptions.Standard.WithResolver(HardenedFormatterResolver.Instance));

        Assert.Equal("reading", notFound.Resource);
        Assert.Equal(404, notFound.Status);
        Assert.Equal("No reading has id 500.", notFound.Detail);
    }

    /// <summary>And the success case on the same operation still negotiates.</summary>
    [HardenedTest]
    public async Task TheSuccessOnTheSameOperationStillAnswersMessagePack(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(
            "/msgpack/declared/4",
            request => request.Headers[KnownHeaders.Accept] = MessagePackContentType.Value);

        response.Assert.Ok();

        Assert.Equal(new MessagePackController.Reading("sensor-4", 12), Read(response));
    }
}
