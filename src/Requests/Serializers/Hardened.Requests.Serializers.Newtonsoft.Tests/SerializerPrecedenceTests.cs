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
/// It was not, until 2026-08-18. Neither Newtonsoft type stated an order, so
/// <see cref="NewtonsoftSerializer"/> sat at <see cref="ResponseSerializerOrder.Normal"/> — exactly
/// where <c>SystemTextJsonResponseSerializer</c> sits — and both return
/// <c>IsDefaultSerializer</c>. Which one wrote a JSON response came down to which module registered
/// last, and <c>SystemTextJsonResponseSerializer</c>'s own remarks say at length that registration
/// order here is not something an application can steer. Displacing the built-in JSON serializer is
/// the entire purpose of this package.
/// </para>
/// <para>
/// The read half was worse: <see cref="IRequestDeserializer"/> had no <c>Order</c> at all, so
/// nothing could be stated. It has one now.
/// </para>
/// <para>
/// <b>Both registration orders are tested.</b> One order alone proves nothing here — the whole
/// defect was that the answer depended on it.
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
            NullLogger<SystemTextJsonRequestDeserializer>.Instance,
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
    /// The order that used to lose. Reverse-registration put System.Text.Json first, and nothing
    /// outranked it.
    /// </summary>
    [Fact]
    public void NewtonsoftWritesTheResponseWhenItRegisteredFirst() {
        var locator = Locator([], [Newtonsoft(), SystemTextJson()]);

        Assert.IsType<NewtonsoftSerializer>(
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

    #region behind the AOT serializers

    /// <summary>
    /// An application importing <c>[AotSerializerModule]</c> keeps the source-generated serializers.
    /// </summary>
    /// <remarks>
    /// The combination is contradictory — this package is reflection-based and the AOT module exists
    /// because reflection is not there — but if anything resolves it, the generated one has to be
    /// the answer. That is why Newtonsoft sits one step behind <c>Specialized</c> rather than at it.
    /// </remarks>
    [Fact]
    public void TheAotResponseSerializerStillOutranksNewtonsoft() {
        Assert.True(
            new AotResponseSerializer(JsonConfiguration(), []).Order < Newtonsoft().Order,
            "the AOT response serializer must stay ahead of Newtonsoft");
    }

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
    /// Newtonsoft outranks the built-in pair without being ahead of a serializer that claims one
    /// specific media type.
    /// </summary>
    [Fact]
    public void NewtonsoftSitsBetweenSpecializedAndNormal() {
        Assert.InRange(
            Newtonsoft().Order,
            (int)ResponseSerializerOrder.Specialized + 1,
            (int)ResponseSerializerOrder.Normal - 1);

        Assert.InRange(
            NewtonsoftReader().Order,
            (int)RequestDeserializerOrder.Specialized + 1,
            (int)RequestDeserializerOrder.Normal - 1);
    }
}
