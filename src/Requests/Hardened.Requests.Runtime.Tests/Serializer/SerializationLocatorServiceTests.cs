using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Tests.Support;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// Which serializer handles a response. An application that registers its own serializer
/// expects it to win over the framework's, and a request that nothing claims still has to
/// produce something.
/// </summary>
public class SerializationLocatorServiceTests {

    private static IResponseSerializer Response(bool canProcess, bool isDefault, int order = 0) {
        var serializer = Substitute.For<IResponseSerializer>();

        serializer.CanProcessContext(Arg.Any<IExecutionContext>()).Returns(canProcess);
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

    /// <summary>
    /// A serializer that says it can handle the context is chosen over one that cannot,
    /// however they were registered.
    /// </summary>
    [Fact]
    public void TheSerializerThatClaimsTheContextIsChosen() {
        var declines = Response(canProcess: false, isDefault: false);
        var claims = Response(canProcess: true, isDefault: false);

        var chosen = Locator(serializers: new[] { declines, claims })
            .FindResponseSerializer(Pipeline.Context());

        Assert.Same(claims, chosen);
    }

    /// <summary>
    /// Order beats registration order. A serializer that asked to go first does, wherever it was
    /// registered.
    /// </summary>
    /// <remarks>
    /// This is the guarantee registration order could not give. Within a module DependencyModules
    /// sorts registrations by implementation type name, so before <c>Order</c> existed the winner
    /// of a contested response was decided by how two class names sorted alphabetically - and
    /// renaming a class changed which one served the request.
    /// </remarks>
    [Fact]
    public void ALowerOrderIsTestedFirstWhateverTheRegistrationOrder() {
        var normal = Response(canProcess: true, isDefault: true, order: 0);
        var ahead = Response(canProcess: true, isDefault: false, order: -1000);

        var chosen = Locator(serializers: new[] { ahead, normal })
            .FindResponseSerializer(Pipeline.Context());

        Assert.Same(ahead, chosen);
    }

    /// <summary>
    /// Order decides who is asked first, not who answers when nobody claims the context. A
    /// specialist sitting ahead of JSON must not cost JSON its role as the fallback, which is what
    /// answers <c>Accept: */*</c> and a request with no Accept header at all.
    /// </summary>
    [Fact]
    public void OrderDoesNotOverrideTheDefaultSerializerFallback() {
        var ahead = Response(canProcess: false, isDefault: false, order: -1000);
        var fallback = Response(canProcess: false, isDefault: true, order: 0);

        var chosen = Locator(serializers: new[] { ahead, fallback })
            .FindResponseSerializer(Pipeline.Context());

        Assert.Same(fallback, chosen);
    }

    /// <summary>
    /// Serializers sharing an order keep the reverse-registration relationship, so an application's
    /// own still beats the framework's. The sort has to be stable for that to hold.
    /// </summary>
    [Fact]
    public void WithinOneOrderTheLaterRegistrationIsStillTestedFirst() {
        var framework = Response(canProcess: true, isDefault: true);
        var application = Response(canProcess: true, isDefault: false);

        var chosen = Locator(serializers: new[] { framework, application })
            .FindResponseSerializer(Pipeline.Context());

        Assert.Same(application, chosen);
    }

    /// <summary>
    /// Registration order is reversed on the way in, so an application's own serializer -
    /// registered after the framework's - is asked first. Two serializers that both claim the
    /// context resolve to the later registration.
    /// </summary>
    [Fact]
    public void TheLastRegisteredClaimantWinsSoApplicationSerializersBeatTheFrameworks() {
        var framework = Response(canProcess: true, isDefault: true);
        var application = Response(canProcess: true, isDefault: false);

        var chosen = Locator(serializers: new[] { framework, application })
            .FindResponseSerializer(Pipeline.Context());

        Assert.Same(application, chosen);
    }

    /// <summary>
    /// Nothing claiming the context falls back to a default serializer rather than failing -
    /// a request with an unfamiliar <c>Accept</c> still gets JSON back.
    /// </summary>
    [Fact]
    public void ADefaultSerializerIsTheFallbackWhenNothingClaimsTheContext() {
        var specialist = Response(canProcess: false, isDefault: false);
        var fallback = Response(canProcess: false, isDefault: true);

        var chosen = Locator(serializers: new[] { specialist, fallback })
            .FindResponseSerializer(Pipeline.Context());

        Assert.Same(fallback, chosen);
    }

    /// <summary>
    /// No claimant and no default is a configuration error, and it says so rather than
    /// returning null for the caller to dereference.
    /// </summary>
    [Fact]
    public void NoSerializerAtAllIsAnError() {
        var locator = Locator(serializers: new[] { Response(canProcess: false, isDefault: false) });

        Assert.Throws<Exception>(() => locator.FindResponseSerializer(Pipeline.Context()));
    }

    [Fact]
    public void NoSerializerRegisteredAtAllIsAnError() {
        Assert.Throws<Exception>(() => Locator().FindResponseSerializer(Pipeline.Context()));
    }

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
    /// The deserializer fallback keeps the first default it meets - the last registered, since
    /// the list is reversed - while the response side keeps the last. The two differ, and this
    /// pins each so the difference is visible if either is changed.
    /// </summary>
    [Fact]
    public void TheDefaultDeserializerFallbackIsTheLastRegisteredOne() {
        var first = Request(canProcess: false, isDefault: true);
        var second = Request(canProcess: false, isDefault: true);

        var chosen = Locator(deserializers: new[] { first, second })
            .FindRequestDeserializer(Pipeline.Context());

        Assert.Same(second, chosen);
    }

    /// <summary>
    /// The response side keeps overwriting its remembered default as it walks the reversed
    /// list, so the fallback is the first-registered default rather than the last. That is the
    /// opposite of the request side, and of the "later registration wins" rule that applies
    /// when a serializer actually claims the context.
    /// </summary>
    [Fact]
    public void TheDefaultResponseSerializerFallbackIsTheFirstRegisteredOne() {
        var first = Response(canProcess: false, isDefault: true);
        var second = Response(canProcess: false, isDefault: true);

        var chosen = Locator(serializers: new[] { first, second })
            .FindResponseSerializer(Pipeline.Context());

        Assert.Same(first, chosen);
    }

    /// <summary>
    /// A claimant later in the search order is reached even when an earlier one has already
    /// been remembered as the default, so registering a default first does not shadow a
    /// specialist registered after it.
    /// </summary>
    [Fact]
    public void ADefaultDoesNotShadowASpecialistThatClaimsTheContext() {
        var specialist = Response(canProcess: true, isDefault: false);
        var alwaysDefault = Response(canProcess: false, isDefault: true);

        var chosen = Locator(serializers: new[] { specialist, alwaysDefault })
            .FindResponseSerializer(Pipeline.Context());

        Assert.Same(specialist, chosen);
    }
}
