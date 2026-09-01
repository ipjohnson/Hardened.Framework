using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Tests.Support;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// Negotiation against what the operation declares, rather than against every registered serializer.
/// </summary>
/// <remarks>
/// <c>SerializationLocatorServiceTests</c> covers the path an operation that declares nothing takes,
/// which is unchanged. This covers the tier above it.
/// </remarks>
public class DeclaredContentTypeNegotiationTests {

    private static IResponseSerializer Serializer(string produces, bool isDefault = false) {
        var serializer = Substitute.For<IResponseSerializer>();

        serializer.CanProduce(Arg.Any<string>(), Arg.Any<IExecutionContext>())
            .Returns(call => MediaType.Matches((string)call[0], produces));
        serializer.IsDefaultSerializer.Returns(isDefault);

        return serializer;
    }

    /// <summary>A context whose handler declares <paramref name="declared"/>.</summary>
    private static IExecutionContext Context(string? accept, params string[] declared) {
        var context = Pipeline.Context(accept: accept);
        var handlerInfo = Substitute.For<IExecutionRequestHandlerInfo>();

        handlerInfo.ProducedContentTypes.Returns(declared);
        context.HandlerInfo = handlerInfo;

        return context;
    }

    private static SerializationLocatorService Locator(
        ContentNegotiationMode mode, params IResponseSerializer[] serializers) =>
        new(Array.Empty<IRequestDeserializer>(), serializers, new ContentNegotiationPolicy(mode));

    [Fact]
    public void AnAbsentAcceptTakesTheFirstDeclaredType() {
        var json = Serializer("application/json", isDefault: true);
        var text = Serializer("text/plain");

        var chosen = Locator(ContentNegotiationMode.Strict, json, text)
            .FindResponseSerializer(Context(accept: null, "text/plain"));

        Assert.Same(text, chosen);
    }

    /// <summary>
    /// The defect in one assertion: <c>*/*</c> used to reach whichever serializer answered first,
    /// which is JSON, for an operation that declares plain text and nothing else.
    /// </summary>
    [Fact]
    public void AnyMediaTypeTakesTheFirstDeclaredType() {
        var json = Serializer("application/json", isDefault: true);
        var text = Serializer("text/plain");

        var chosen = Locator(ContentNegotiationMode.Strict, json, text)
            .FindResponseSerializer(Context("*/*", "text/plain"));

        Assert.Same(text, chosen);
    }

    /// <summary>Document order is the server's preference, and decides what <c>*/*</c> gets.</summary>
    [Fact]
    public void TheFirstDeclaredTypeIsTheDocumentsOrderNotTheSerializers() {
        var json = Serializer("application/json", isDefault: true);
        var text = Serializer("text/plain");

        var chosen = Locator(ContentNegotiationMode.Strict, json, text)
            .FindResponseSerializer(Context("*/*", "text/plain", "application/json"));

        Assert.Same(text, chosen);
    }

    [Fact]
    public void AnExplicitAcceptWithinTheSetIsHonoured() {
        var json = Serializer("application/json", isDefault: true);
        var text = Serializer("text/plain");

        var chosen = Locator(ContentNegotiationMode.Strict, json, text)
            .FindResponseSerializer(Context("application/json", "text/plain", "application/json"));

        Assert.Same(json, chosen);
    }

    /// <summary>
    /// A client listing several gets the overlap, not a refusal - the case that keeps strict mode
    /// from refusing clients that said outright they could read the answer.
    /// </summary>
    [Fact]
    public void AClientListingSeveralGetsTheOverlap() {
        var json = Serializer("application/json", isDefault: true);
        var text = Serializer("text/plain");

        var chosen = Locator(ContentNegotiationMode.Strict, json, text)
            .FindResponseSerializer(Context("application/json, text/plain", "text/plain"));

        Assert.Same(text, chosen);
    }

    [Fact]
    public void NoOverlapIsNotAcceptableUnderStrict() {
        var json = Serializer("application/json", isDefault: true);
        var text = Serializer("text/plain");

        var locator = Locator(ContentNegotiationMode.Strict, json, text);

        Assert.Throws<NotAcceptableException>(
            () => locator.FindResponseSerializer(Context("application/json", "text/plain")));
    }

    /// <summary>And the refusal names what the operation can produce.</summary>
    [Fact]
    public void TheRefusalNamesWhatIsOnOffer() {
        var locator = Locator(
            ContentNegotiationMode.Strict,
            Serializer("application/json", isDefault: true),
            Serializer("text/plain"));

        var refusal = Assert.Throws<NotAcceptableException>(
            () => locator.FindResponseSerializer(Context("application/json", "text/plain")));

        Assert.Equal(406, refusal.StatusCode);
        Assert.Contains("text/plain", refusal.Message);
    }

    /// <summary>The escape hatch, for a service whose clients ask badly and must be served anyway.</summary>
    [Fact]
    public void NoOverlapFallsBackToTheDefaultUnderLenient() {
        var json = Serializer("application/json", isDefault: true);
        var text = Serializer("text/plain");

        var chosen = Locator(ContentNegotiationMode.Lenient, json, text)
            .FindResponseSerializer(Context("application/json", "text/plain"));

        Assert.Same(json, chosen);
    }

    /// <summary>
    /// A declared type nothing can write is a configuration fault, not a client one.
    /// </summary>
    /// <remarks>
    /// A document declaring <c>application/pdf</c> with no PDF serializer registered would otherwise
    /// answer 406 to every request and make a misconfigured service look like a misbehaving client.
    /// The committed-content-type tier throws for exactly this, and so does this one.
    /// </remarks>
    [Fact]
    public void ADeclaredTypeNothingCanWriteIsAConfigurationFault() {
        var locator = Locator(
            ContentNegotiationMode.Strict, Serializer("application/json", isDefault: true));

        var failure = Assert.Throws<ContentTypeNotProducibleException>(
            () => locator.FindResponseSerializer(Context("text/html", "application/pdf")));

        Assert.Contains("application/pdf", failure.Message);
    }

    /// <summary>
    /// An operation declaring nothing negotiates exactly as it did before any of this existed.
    /// </summary>
    [Fact]
    public void AnOperationDeclaringNothingIsUnaffected() {
        var json = Serializer("application/json", isDefault: true);

        var chosen = Locator(ContentNegotiationMode.Strict, json)
            .FindResponseSerializer(Context("*/*"));

        Assert.Same(json, chosen);
    }

    /// <summary>
    /// The default is strict, so a service that registers no policy still refuses cleanly.
    /// </summary>
    [Fact]
    public void TheDefaultPolicyIsStrict() {
        var locator = new SerializationLocatorService(
            Array.Empty<IRequestDeserializer>(),
            new[] { Serializer("application/json", isDefault: true), Serializer("text/plain") });

        Assert.Throws<NotAcceptableException>(
            () => locator.FindResponseSerializer(Context("application/json", "text/plain")));
    }
}
