using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Serializers.MessagePack.Tests.Support;
using Xunit;

namespace Hardened.Requests.Serializers.MessagePack.Tests;

/// <summary>
/// What the pair declares about itself, which is how the locator finds them.
/// </summary>
public class RegistrationTests {

    [Fact]
    public void TheSerializerRegistersUnderTheMessagePackMediaType() =>
        Assert.Equal(
            MessagePackContentType.Value,
            Pipeline.ResponseSerializer(Pipeline.Pool()).ContentType);

    /// <summary>
    /// Installing the package does not change what an operation produces. A service that imports it
    /// and declares nothing still answers JSON, because nothing declared is the service default and
    /// this serializer does not claim to be one.
    /// </summary>
    [Fact]
    public void ItIsNotTheDefaultSerializer() =>
        Assert.False(Pipeline.ResponseSerializer(Pipeline.Pool()).IsDefaultSerializer);

    /// <summary>
    /// The response carries the media type it was written as. Nothing else assigns it on the bound
    /// path: <c>ContextSerializationService</c> calls a serializer resolved at composition
    /// directly, and only <c>SerializationLocatorService.FindDeclaredProducer</c> assigns one.
    /// </summary>
    [Fact]
    public async Task SerializingCommitsTheContentType() {
        var context = Pipeline.Context();

        context.Response.ResponseValue = new Pipeline.Payload("first", 2);

        await Pipeline.ResponseSerializer(Pipeline.Pool()).SerializeResponse(context);

        Assert.Equal(MessagePackContentType.Value, context.Response.ContentType);
    }

    /// <summary>An empty response still says what it would have been.</summary>
    [Fact]
    public async Task ANullResponseStillCommitsTheContentType() {
        var context = Pipeline.Context();

        context.Response.ResponseValue = null;

        await Pipeline.ResponseSerializer(Pipeline.Pool()).SerializeResponse(context);

        Assert.Equal(MessagePackContentType.Value, context.Response.ContentType);
    }

    [Fact]
    public void AMessagePackBodyIsClaimed() =>
        Assert.True(
            Pipeline.Deserializer(Pipeline.Pool())
                .CanProcessContext(Pipeline.Context(contentType: MessagePackContentType.Value)));

    /// <summary>
    /// Parameters and all. A client sending <c>application/x-msgpack; charset=binary</c> is sending
    /// MessagePack.
    /// </summary>
    [Fact]
    public void AMediaTypeWithParametersIsClaimed() =>
        Assert.True(
            Pipeline.Deserializer(Pipeline.Pool())
                .CanProcessContext(
                    Pipeline.Context(contentType: MessagePackContentType.Value + "; charset=binary")));

    [Fact]
    public void AJsonBodyIsLeftToTheJsonReader() =>
        Assert.False(
            Pipeline.Deserializer(Pipeline.Pool())
                .CanProcessContext(Pipeline.Context(contentType: KnownContentType.Json)));

    /// <summary>
    /// A body with no content type is not MessagePack. It is a JSON body from a client that did not
    /// say so, which is what the default reader is for - and this reader is not one.
    /// </summary>
    [Fact]
    public void ABodyWithNoContentTypeIsNotClaimed() {
        var deserializer = Pipeline.Deserializer(Pipeline.Pool());

        Assert.False(deserializer.CanProcessContext(Pipeline.Context(contentType: null)));
        Assert.False(deserializer.IsDefaultSerializer);
    }

    /// <summary>
    /// Ahead of the JSON readers, which is where a reader for one specific content type belongs.
    /// </summary>
    [Fact]
    public void ItIsAskedBeforeTheGeneralPurposeReaders() =>
        Assert.Equal(
            (int)RequestDeserializerOrder.Specialized,
            Pipeline.Deserializer(Pipeline.Pool()).Order);
}
