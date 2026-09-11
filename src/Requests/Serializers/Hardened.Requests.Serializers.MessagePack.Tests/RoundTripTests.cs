using Hardened.Requests.Serializers.MessagePack.Tests.Support;
using MessagePack;
using Xunit;

namespace Hardened.Requests.Serializers.MessagePack.Tests;

/// <summary>
/// A model out and the same model back, through the serializer pair the package registers.
/// </summary>
public class RoundTripTests {

    private static async Task<byte[]> Write(object value) {
        var context = Pipeline.Context();

        context.Response.ResponseValue = value;

        await Pipeline.ResponseSerializer(Pipeline.Pool()).SerializeResponse(context);

        return Pipeline.BodyOf(context);
    }

    [Fact]
    public async Task AModelGoesOutAsMessagePackAndComesBack() {
        var bytes = await Write(new Pipeline.Payload("first", 2));

        var read = await Pipeline.Deserializer(Pipeline.Pool())
            .DeserializeRequestBody<Pipeline.Payload>(Pipeline.Context(bytes));

        Assert.Equal(new Pipeline.Payload("first", 2), read);
    }

    /// <summary>
    /// Not JSON. Obvious, and worth one assertion: the pipeline holds the value as <c>object</c>,
    /// and a serializer that fell back to a type-keyed map or to a string would still round-trip
    /// through itself.
    /// </summary>
    [Fact]
    public async Task TheBytesAreMessagePack() {
        var bytes = await Write(new Pipeline.Payload("first", 2));

        Assert.Equal(
            MessagePackSerializer.Serialize(
                new Pipeline.Payload("first", 2), Pipeline.Options().Options,
                TestContext.Current.CancellationToken),
            bytes);
    }

    /// <summary>
    /// A model keyed by property name rather than by index, which is the other shape
    /// <c>[MessagePackObject]</c> takes.
    /// </summary>
    [Fact]
    public async Task AStringKeyedModelRoundTrips() {
        var bytes = await Write(new Pipeline.Named("first", 2));

        Assert.Equal(
            new Pipeline.Named("first", 2),
            await Pipeline.Deserializer(Pipeline.Pool())
                .DeserializeRequestBody<Pipeline.Named>(Pipeline.Context(bytes)));
    }

    /// <summary>
    /// A collection of models, which the generated formatters do not cover on their own: the item
    /// type is generated and the list around it comes from <c>DynamicGenericResolver</c>, which is
    /// why the chain carries it.
    /// </summary>
    [Fact]
    public async Task AListOfModelsRoundTrips() {
        var bytes = await Write(
            new List<Pipeline.Payload> { new("first", 1), new("second", 2) });

        var read = await Pipeline.Deserializer(Pipeline.Pool())
            .DeserializeRequestBody<List<Pipeline.Payload>>(Pipeline.Context(bytes));

        Assert.Equal(
            [new Pipeline.Payload("first", 1), new Pipeline.Payload("second", 2)],
            read);
    }

    /// <summary>
    /// The value is serialized as its runtime type. The pipeline holds it as <c>object</c>, and a
    /// formatter for <c>object</c> writes a type-keyed map rather than the model - which is the
    /// same trap <c>JsonTypeInfoLookup.For(options, value)</c> exists to avoid on the JSON side.
    /// </summary>
    [Fact]
    public async Task TheRuntimeTypeDecidesTheFormatter() {
        object boxed = new Pipeline.Payload("first", 2);

        Assert.Equal(
            MessagePackSerializer.Serialize(
                new Pipeline.Payload("first", 2), Pipeline.Options().Options,
                TestContext.Current.CancellationToken),
            await Write(boxed));
    }

    [Fact]
    public async Task ANullResponseWritesNothing() {
        var context = Pipeline.Context();

        context.Response.ResponseValue = null;

        await Pipeline.ResponseSerializer(Pipeline.Pool()).SerializeResponse(context);

        Assert.Empty(Pipeline.BodyOf(context));
    }
}
