using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Tests.Support;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// <c>[ErrorBodies(ErrorBodyFormat.Json)]</c>: a failed request answers JSON whatever it negotiated.
/// </summary>
/// <remarks>
/// <para>
/// For a service whose success bodies are binary and whose error bodies have to stay readable. The
/// case that forces it is Refit, whose <c>ApiException</c> carries content as a <c>string</c> - so
/// a client of a MessagePack service can read every success body and no error body, and the only
/// thing the service can do about it is send text.
/// </para>
/// <para>
/// Off by default, so a service that says nothing is unchanged: an operation declaring
/// <c>application/x-msgpack</c> still answers its refusals as MessagePack.
/// </para>
/// </remarks>
public class ErrorBodyPolicyTests {

    private static IResponseSerializer Serializer(string produces, bool isDefault = false) {
        var serializer = Substitute.For<IResponseSerializer>();

        serializer.ContentType.Returns(produces);
        serializer.CanProduce(Arg.Any<string>(), Arg.Any<IExecutionContext>())
            .Returns(call => MediaType.Matches((string)call[0], produces));
        serializer.IsDefaultSerializer.Returns(isDefault);

        return serializer;
    }

    private static readonly IResponseSerializer Json =
        Serializer(KnownContentType.Json, isDefault: true);

    private static readonly IResponseSerializer MessagePack = Serializer("application/x-msgpack");

    private static SerializationLocatorService Locator(ErrorBodyFormat? format) =>
        new(Array.Empty<IRequestDeserializer>(),
            new[] { Json, MessagePack },
            errorBodyPolicy: format is { } value ? new ErrorBodyPolicy(value) : null);

    private static IExecutionContext Failing(int status, string accept = "application/x-msgpack") {
        var context = Pipeline.Context(accept: accept);

        context.Response.Status = status;

        return context;
    }

    [Fact]
    public void AFailureAnswersJsonUnderTheJsonFormat() =>
        Assert.Same(Json, Locator(ErrorBodyFormat.Json).FindResponseSerializer(Failing(404)));

    [Fact]
    public void AServerFailureAnswersJsonToo() =>
        Assert.Same(Json, Locator(ErrorBodyFormat.Json).FindResponseSerializer(Failing(500)));

    /// <summary>
    /// A success is untouched: this decides what a <em>failure</em> is written as, and a service
    /// whose whole point is a binary success body would be a strange thing to break.
    /// </summary>
    [Fact]
    public void ASuccessStillNegotiates() =>
        Assert.Same(MessagePack, Locator(ErrorBodyFormat.Json).FindResponseSerializer(Failing(200)));

    /// <summary>
    /// The boundary. 399 is not a failure and 400 is.
    /// </summary>
    [Theory]
    [InlineData(399, false)]
    [InlineData(400, true)]
    public void FourHundredIsWhereItStarts(int status, bool json) =>
        Assert.Same(
            json ? Json : MessagePack,
            Locator(ErrorBodyFormat.Json).FindResponseSerializer(Failing(status)));

    /// <summary>
    /// The default, which is what every service that says nothing gets: an operation declaring
    /// MessagePack answers its refusals as MessagePack, exactly as it did.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(ErrorBodyFormat.Negotiated)]
    public void AFailureNegotiatesByDefault(ErrorBodyFormat? format) =>
        Assert.Same(MessagePack, Locator(format).FindResponseSerializer(Failing(404)));

    /// <summary>
    /// Ahead of a committed content type, and deliberately. A raw handler that committed
    /// <c>image/png</c> and then failed has no PNG to send, and its error model is not one either -
    /// so the commitment is the thing that has to give.
    /// </summary>
    [Fact]
    public void ACommittedContentTypeDoesNotSurviveAFailure() {
        var context = Failing(500);

        context.Response.ContentType = "application/x-msgpack";

        Assert.Same(Json, Locator(ErrorBodyFormat.Json).FindResponseSerializer(context));
    }

    /// <summary>
    /// Falls through where nothing writes JSON rather than refusing. An application that replaced
    /// the JSON serializer with one under another tag has said something about what it writes, and
    /// this setting is not the place to overrule it.
    /// </summary>
    [Fact]
    public void ItFallsThroughWhenNothingWritesJson() {
        var locator = new SerializationLocatorService(
            Array.Empty<IRequestDeserializer>(),
            new[] { MessagePack },
            errorBodyPolicy: new ErrorBodyPolicy(ErrorBodyFormat.Json));

        Assert.Same(MessagePack, locator.FindResponseSerializer(Failing(404)));
    }
}
