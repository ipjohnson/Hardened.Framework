using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Tests.Support;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// Which serializer writes a response: what the response committed to, then what the client asked
/// for, then the default.
/// </summary>
public class SerializationLocatorServiceTests {

    /// <summary>
    /// A serializer that emits <paramref name="produces"/>, or nothing at all when it is null.
    /// </summary>
    private static IResponseSerializer Response(
        bool isDefault, string? produces = null, int order = 0) {
        var serializer = Substitute.For<IResponseSerializer>();

        serializer.CanProduce(Arg.Any<string>(), Arg.Any<IExecutionContext>())
            .Returns(call => produces != null && MediaType.Matches((string)call[0], produces));
        serializer.IsDefaultSerializer.Returns(isDefault);
        serializer.Order.Returns(order);

        return serializer;
    }

    private static IRequestDeserializer Request(bool canProcess, bool isDefault) {
        var deserializer = Substitute.For<IRequestDeserializer>();

        deserializer.CanProcessContext(Arg.Any<IExecutionContext>()).Returns(canProcess);
        deserializer.IsDefaultSerializer.Returns(isDefault);

        return deserializer;
    }

    private static SerializationLocatorService Locator(
        IEnumerable<IRequestDeserializer>? deserializers = null,
        IEnumerable<IResponseSerializer>? serializers = null) =>
        new(deserializers ?? Array.Empty<IRequestDeserializer>(),
            serializers ?? Array.Empty<IResponseSerializer>());

    // ── negotiation ────────────────────────────────────────────────────

    [Fact]
    public void TheSerializerThatProducesTheRequestedTypeIsChosen() {
        var declines = Response(isDefault: false, produces: "text/csv");
        var claims = Response(isDefault: false, produces: "application/json");

        var chosen = Locator(serializers: new[] { declines, claims })
            .FindResponseSerializer(Pipeline.Context(accept: "application/json"));

        Assert.Same(claims, chosen);
    }

    /// <summary>
    /// The client's first preference wins over the server's ordering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The case this whole design exists for, and it is not hypothetical: TechEmpower's json, db,
    /// query and update tests all send
    /// <c>application/json,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7</c>.
    /// That header contains <c>text/html</c>. A template serializer ordered ahead of JSON, asked in
    /// isolation whether it can handle the context, would answer yes and hijack four of the six
    /// benchmark routes.
    /// </para>
    /// <para>
    /// The accept list is the outer loop, so <c>application/json</c> is asked about before
    /// <c>text/html</c> is, and the html serializer's order never comes into it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheClientsFirstPreferenceWinsOverServerOrder() {
        var html = Response(isDefault: false, produces: "text/html", order: -1000);
        var json = Response(isDefault: true, produces: "application/json");

        var chosen = Locator(serializers: new[] { html, json })
            .FindResponseSerializer(Pipeline.Context(
                accept: "application/json,text/html;q=0.9,application/xml;q=0.8,*/*;q=0.7"));

        Assert.Same(json, chosen);
    }

    /// <summary>And the same two serializers resolve the other way for a browser.</summary>
    [Fact]
    public void AClientPreferringHtmlGetsTheHtmlSerializer() {
        var html = Response(isDefault: false, produces: "text/html", order: -1000);
        var json = Response(isDefault: true, produces: "application/json");

        var chosen = Locator(serializers: new[] { html, json })
            .FindResponseSerializer(Pipeline.Context(
                accept: "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));

        Assert.Same(html, chosen);
    }

    /// <summary>
    /// Order decides among serializers that satisfy the same preference - which is the whole of what
    /// it decides. A client sending <c>*/*</c> has expressed no preference, so the server's ranking
    /// is the only thing left to go on.
    /// </summary>
    [Fact]
    public void ServerOrderDecidesWithinOneAcceptPosition() {
        var normal = Response(isDefault: true, produces: "application/json");
        var ahead = Response(isDefault: false, produces: "text/html", order: -1000);

        var chosen = Locator(serializers: new[] { normal, ahead })
            .FindResponseSerializer(Pipeline.Context(accept: "*/*"));

        Assert.Same(ahead, chosen);
    }

    /// <summary>
    /// A request with no Accept header accepts anything, exactly as <c>*/*</c> does. It used to be
    /// declined by every serializer and rescued by the default.
    /// </summary>
    [Fact]
    public void NoAcceptHeaderIsTreatedAsAnything() {
        var json = Response(isDefault: false, produces: "application/json");

        var chosen = Locator(serializers: new[] { json })
            .FindResponseSerializer(Pipeline.Context(accept: null));

        Assert.Same(json, chosen);
    }

    /// <summary>A subtype wildcard matches within its type and not outside it.</summary>
    [Fact]
    public void ASubtypeWildcardMatchesWithinItsType() {
        var json = Response(isDefault: false, produces: "application/json");
        var html = Response(isDefault: false, produces: "text/html");

        Assert.Same(html, Locator(serializers: new[] { json, html })
            .FindResponseSerializer(Pipeline.Context(accept: "text/*")));
    }

    /// <summary>
    /// Serializers sharing a preference position and an order keep the reverse-registration
    /// relationship, so an application's own still beats the framework's. The sort has to be stable.
    /// </summary>
    [Fact]
    public void WithinOneOrderTheLaterRegistrationIsTestedFirst() {
        var framework = Response(isDefault: true, produces: "application/json");
        var application = Response(isDefault: false, produces: "application/json");

        var chosen = Locator(serializers: new[] { framework, application })
            .FindResponseSerializer(Pipeline.Context(accept: "application/json"));

        Assert.Same(application, chosen);
    }

    // ── the default ────────────────────────────────────────────────────

    /// <summary>
    /// Nothing producing what was asked for falls back rather than failing, so a request with an
    /// unfamiliar Accept still gets an answer.
    /// </summary>
    [Fact]
    public void ADefaultSerializerIsTheFallbackWhenNothingProducesWhatWasAsked() {
        var specialist = Response(isDefault: false, produces: "text/csv");
        var fallback = Response(isDefault: true, produces: "application/json");

        var chosen = Locator(serializers: new[] { specialist, fallback })
            .FindResponseSerializer(Pipeline.Context(accept: "application/pdf"));

        Assert.Same(fallback, chosen);
    }

    /// <summary>
    /// A default does not shadow a serializer that actually produces what was asked for, however
    /// they were registered.
    /// </summary>
    [Fact]
    public void ADefaultDoesNotShadowASerializerThatProducesTheRequestedType() {
        var specialist = Response(isDefault: false, produces: "text/csv");
        var alwaysDefault = Response(isDefault: true, produces: "application/json");

        var chosen = Locator(serializers: new[] { specialist, alwaysDefault })
            .FindResponseSerializer(Pipeline.Context(accept: "text/csv"));

        Assert.Same(specialist, chosen);
    }

    [Fact]
    public void NoProducerAndNoDefaultIsAnError() {
        var locator = Locator(serializers: new[] { Response(isDefault: false, produces: "text/csv") });

        Assert.Throws<Exception>(
            () => locator.FindResponseSerializer(Pipeline.Context(accept: "application/pdf")));
    }

    [Fact]
    public void NoSerializerRegisteredAtAllIsAnError() {
        Assert.Throws<Exception>(() => Locator().FindResponseSerializer(Pipeline.Context()));
    }

    // ── a committed content type ───────────────────────────────────────

    /// <summary>
    /// A response that already carries a content type has committed to it, and the client does not
    /// get to overrule it. This is what <c>[RawResponse]</c> means: a handler returning a PDF
    /// returns a PDF whatever the request asked for.
    /// </summary>
    [Fact]
    public void ACommittedContentTypeSkipsNegotiation() {
        var csv = Response(isDefault: false, produces: "text/csv");
        var json = Response(isDefault: true, produces: "application/json");

        var context = Pipeline.Context(accept: "application/json");
        context.Response.ContentType = "text/csv";

        Assert.Same(csv, Locator(serializers: new[] { csv, json }).FindResponseSerializer(context));
    }

    /// <summary>
    /// Committing to something nothing can write is a configuration problem, and quietly answering
    /// with JSON instead would hide it.
    /// </summary>
    [Fact]
    public void ACommittedContentTypeNothingCanProduceIsAnError() {
        var json = Response(isDefault: true, produces: "application/json");

        var context = Pipeline.Context(accept: "application/json");
        context.Response.ContentType = "application/pdf";

        var locator = Locator(serializers: new[] { json });

        var exception = Assert.Throws<ContentTypeNotProducibleException>(
            () => locator.FindResponseSerializer(context));

        Assert.Contains("application/pdf", exception.Message);
    }

    // ── request side, unchanged ────────────────────────────────────────

    [Fact]
    public void TheDeserializerThatClaimsTheContextIsChosen() {
        var declines = Request(canProcess: false, isDefault: false);
        var claims = Request(canProcess: true, isDefault: false);

        var chosen = Locator(deserializers: new[] { declines, claims })
            .FindRequestDeserializer(Pipeline.Context());

        Assert.Same(claims, chosen);
    }

    [Fact]
    public void ADefaultDeserializerIsTheFallbackWhenNothingClaimsTheContext() {
        var specialist = Request(canProcess: false, isDefault: false);
        var fallback = Request(canProcess: false, isDefault: true);

        var chosen = Locator(deserializers: new[] { specialist, fallback })
            .FindRequestDeserializer(Pipeline.Context());

        Assert.Same(fallback, chosen);
    }

    [Fact]
    public void NoDeserializerAtAllIsAnError() {
        var locator = Locator(deserializers: new[] { Request(canProcess: false, isDefault: false) });

        Assert.Throws<Exception>(() => locator.FindRequestDeserializer(Pipeline.Context()));
    }

    /// <summary>
    /// The request side keeps the first default it meets, which is the last registered since the
    /// list is reversed. The response side now agrees, where it used to keep the first-registered
    /// one - the asymmetry went when the fallback became its own pass rather than a variable
    /// overwritten while walking.
    /// </summary>
    [Fact]
    public void TheDefaultDeserializerFallbackIsTheLastRegisteredOne() {
        var first = Request(canProcess: false, isDefault: true);
        var second = Request(canProcess: false, isDefault: true);

        var chosen = Locator(deserializers: new[] { first, second })
            .FindRequestDeserializer(Pipeline.Context());

        Assert.Same(second, chosen);
    }

    [Fact]
    public void TheDefaultResponseSerializerFallbackIsAlsoTheLastRegisteredOne() {
        var first = Response(isDefault: true, produces: "application/json");
        var second = Response(isDefault: true, produces: "application/json");

        var chosen = Locator(serializers: new[] { first, second })
            .FindResponseSerializer(Pipeline.Context(accept: "application/pdf"));

        Assert.Same(second, chosen);
    }
}
