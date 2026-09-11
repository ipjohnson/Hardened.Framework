using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Serializers.Newtonsoft.Impl;
using Hardened.Requests.Serializers.Newtonsoft.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hardened.Requests.Serializers.Newtonsoft.Tests;

/// <summary>
/// Installing the package is enough to be sure it is used.
/// </summary>
/// <remarks>
/// <para>
/// It was not, until 2026-08-18. Neither Newtonsoft type stated an order, so both it and
/// <c>SystemTextJsonResponseSerializer</c> claimed <c>application/json</c> and returned
/// <c>IsDefaultSerializer</c>, and which one wrote a response came down to which module registered
/// last. Displacing the built-in JSON serializer is the entire purpose of this package.
/// </para>
/// <para>
/// <b>The two halves no longer answer the same way, and that is deliberate.</b> A response
/// serializer declares the media type it writes, and the last registration under a media type is
/// the one that answers - so the response half is registration order again, stated as a rule rather
/// than left to how two class names sort. A deserializer is chosen by a predicate over the whole
/// request rather than by a tag, so there is nothing to key a registry on and it keeps its
/// <see cref="IRequestDeserializer.Order"/>.
/// </para>
/// <para>
/// What makes importing the package enough is that a module the application imports is applied
/// after the framework module it depends on. <c>SerializerRegistrationOrderTests</c> pins that
/// against a real container, because it is now the whole of response-side precedence.
/// </para>
/// </remarks>
public class SerializerPrecedenceTests {

    private static IOptions<IJsonSerializerConfiguration> JsonConfiguration() =>
        Options.Create<IJsonSerializerConfiguration>(new JsonSerializerConfiguration());

    private static IResponseSerializer Newtonsoft() =>
        Pipeline.ResponseSerializer(Pipeline.Pool());

    private static IResponseSerializer SystemTextJson() =>
        new SystemTextJsonResponseSerializer(
            JsonConfiguration(), Array.Empty<IJsonTypeInfoResolver>());

    private static IRequestDeserializer NewtonsoftReader() =>
        Pipeline.Deserializer(Pipeline.Pool());

    private static IRequestDeserializer SystemTextJsonReader() =>
        new SystemTextJsonRequestDeserializer(
            JsonConfiguration(),
            Array.Empty<IJsonTypeInfoResolver>());

    private static SerializationLocatorService Locator(
        IEnumerable<IRequestDeserializer> deserializers,
        IEnumerable<IResponseSerializer> serializers) =>
        new(deserializers, serializers);

    #region responses

    [Fact]
    public void NewtonsoftWritesTheResponseWhenItRegisteredLast() {
        var locator = Locator([], [SystemTextJson(), Newtonsoft()]);

        Assert.IsType<NewtonsoftSerializer>(
            locator.FindResponseSerializer(Pipeline.Context()));
    }

    /// <summary>
    /// Registered ahead of System.Text.Json, it loses - the last registration under a media type is
    /// the one that answers.
    /// </summary>
    /// <remarks>
    /// The rule read the other way, and the reason this test states the losing case rather than
    /// asserting symmetry: the two are interchangeable by registration order on purpose now. What
    /// makes the package win in an application is that its module is applied after
    /// <c>HardenedRequestModule</c>, not anything either class declares.
    /// </remarks>
    [Fact]
    public void SystemTextJsonWritesTheResponseWhenNewtonsoftRegisteredFirst() {
        var locator = Locator([], [Newtonsoft(), SystemTextJson()]);

        Assert.IsType<SystemTextJsonResponseSerializer>(
            locator.FindResponseSerializer(Pipeline.Context()));
    }

    [Fact]
    public void NewtonsoftIsTheDefaultResponseSerializerForAClientWithNoPreference() {
        var context = Pipeline.Context();

        context.Request.Headers["Accept"] = "*/*";

        Assert.IsType<NewtonsoftSerializer>(
            Locator([], [SystemTextJson(), Newtonsoft()]).FindResponseSerializer(context));
    }

    #endregion

    #region requests

    [Fact]
    public void NewtonsoftReadsTheRequestWhenItRegisteredLast() {
        var locator = Locator([SystemTextJsonReader(), NewtonsoftReader()], []);

        Assert.IsType<NewtonsoftDeserializer>(
            locator.FindRequestDeserializer(Pipeline.Context("{}")));
    }

    [Fact]
    public void NewtonsoftReadsTheRequestWhenItRegisteredFirst() {
        var locator = Locator([NewtonsoftReader(), SystemTextJsonReader()], []);

        Assert.IsType<NewtonsoftDeserializer>(
            locator.FindRequestDeserializer(Pipeline.Context("{}")));
    }

    /// <summary>
    /// A body carrying no content type falls to the default, and that has to be the same
    /// deserializer that would have claimed it — otherwise one request is read by Newtonsoft and
    /// the next by System.Text.Json depending on a header.
    /// </summary>
    [Fact]
    public void NewtonsoftIsAlsoTheDefaultForABodyWithNoContentType() {
        var locator = Locator([SystemTextJsonReader(), NewtonsoftReader()], []);

        Assert.IsType<NewtonsoftDeserializer>(
            locator.FindRequestDeserializer(Pipeline.Context("{}", contentType: null)));
    }

    #endregion

    /// <summary>
    /// Both directions agree. A package that wrote responses with Newtonsoft while System.Text.Json
    /// read the requests would apply one naming strategy on the way out and another on the way in.
    /// </summary>
    [Fact]
    public void BothDirectionsResolveToNewtonsoft() {
        var locator = Locator(
            [SystemTextJsonReader(), NewtonsoftReader()],
            [SystemTextJson(), Newtonsoft()]);

        Assert.IsType<NewtonsoftDeserializer>(
            locator.FindRequestDeserializer(Pipeline.Context("{}")));
        Assert.IsType<NewtonsoftSerializer>(
            locator.FindResponseSerializer(Pipeline.Context()));
    }

    #region the AOT serializers

    /// <summary>
    /// An application importing <c>[AotSerializerModule]</c> as well as this package gets whichever
    /// it named last, on the response side.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The combination is contradictory — this package is reflection-based and the AOT module exists
    /// because reflection is not there — and the rule resolves it the way the author wrote it. The
    /// previous <c>Order</c> resolved it in the AOT serializer's favour whichever way round the two
    /// were imported, which is a stronger guarantee than the tag rule can make and the one thing
    /// removing <c>Order</c> gave up.
    /// </para>
    /// <para>
    /// Both orders are asserted, because the point is that the answer follows registration.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheAotResponseSerializerAnswersWhenItRegisteredLast() {
        var locator = Locator([], [Newtonsoft(), new AotResponseSerializer(JsonConfiguration(), [])]);

        Assert.IsType<AotResponseSerializer>(locator.FindResponseSerializer(Pipeline.Context()));
    }

    [Fact]
    public void NewtonsoftAnswersWhenItRegisteredAfterTheAotSerializer() {
        var locator = Locator([], [new AotResponseSerializer(JsonConfiguration(), []), Newtonsoft()]);

        Assert.IsType<NewtonsoftSerializer>(locator.FindResponseSerializer(Pipeline.Context()));
    }

    /// <summary>
    /// The request half keeps its order, so the AOT deserializer still outranks this one however the
    /// two are registered.
    /// </summary>
    [Fact]
    public void TheAotRequestDeserializerStillOutranksNewtonsoft() {
        var aot = new AotRequestDeserializer(
            JsonConfiguration(), NullLogger<AotRequestDeserializer>.Instance, []);

        Assert.True(
            aot.Order < NewtonsoftReader().Order,
            "the AOT request deserializer must stay ahead of Newtonsoft");
    }

    #endregion

    /// <summary>
    /// It claims the media type it means to displace, which is what puts it in the same contest as
    /// the built-in pair in the first place.
    /// </summary>
    [Fact]
    public void NewtonsoftClaimsApplicationJson() {
        Assert.Equal("application/json", Newtonsoft().ContentType);
        Assert.Equal(SystemTextJson().ContentType, Newtonsoft().ContentType);
    }

    /// <summary>
    /// The read half sits between the two named values, so it outranks the built-in pair without
    /// being ahead of a deserializer that claims one specific content type.
    /// </summary>
    [Fact]
    public void TheNewtonsoftReaderSitsBetweenSpecializedAndNormal() {
        Assert.InRange(
            NewtonsoftReader().Order,
            (int)RequestDeserializerOrder.Specialized + 1,
            (int)RequestDeserializerOrder.Normal - 1);
    }
}
