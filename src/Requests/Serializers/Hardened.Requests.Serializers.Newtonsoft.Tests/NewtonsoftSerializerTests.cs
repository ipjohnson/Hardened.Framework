using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Serializers.Newtonsoft.Tests.Support;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace Hardened.Requests.Serializers.Newtonsoft.Tests;

/// <summary>
/// Writing a response body with Newtonsoft.
/// </summary>
/// <remarks>
/// The write path was never broken the way the read path was, but it was equally unrun — the
/// assembly had no test project and sat at 0%. What is worth pinning is the buffering: the
/// serializer writes into a pooled stream and copies to the response only once the whole value has
/// been written, so a serializer that throws part-way leaves nothing half-written on the wire.
/// </remarks>
public class NewtonsoftSerializerTests {

    [Fact]
    public async Task AValueIsWrittenAsJson() {
        var context = Pipeline.Context();

        context.Response.ResponseValue = new Pipeline.Payload("first", 2);

        await Pipeline.ResponseSerializer(Pipeline.Pool()).SerializeResponse(context);

        Assert.Equal("""{"Name":"first","Count":2}""", Pipeline.BodyOf(context));
    }

    [Fact]
    public async Task ANullValueIsWrittenAsJsonNull() {
        var context = Pipeline.Context();

        context.Response.ResponseValue = null;

        await Pipeline.ResponseSerializer(Pipeline.Pool()).SerializeResponse(context);

        Assert.Equal("null", Pipeline.BodyOf(context));
    }

    [Fact]
    public async Task TheConfiguredNamingStrategyIsHonoured() {
        var serializer = JsonSerializer.CreateDefault(new JsonSerializerSettings {
            ContractResolver = new DefaultContractResolver {
                NamingStrategy = new SnakeCaseNamingStrategy()
            }
        });

        var context = Pipeline.Context();

        context.Response.ResponseValue = new Pipeline.Payload("first", 2);

        await Pipeline.ResponseSerializer(Pipeline.Pool(), serializer).SerializeResponse(context);

        Assert.Equal("""{"name":"first","count":2}""", Pipeline.BodyOf(context));
    }

    /// <summary>
    /// The setting that most often motivates reaching for this package at all.
    /// </summary>
    [Fact]
    public async Task ConfiguredNullHandlingIsHonoured() {
        var serializer = JsonSerializer.CreateDefault(new JsonSerializerSettings {
            NullValueHandling = NullValueHandling.Ignore
        });

        var context = Pipeline.Context();

        context.Response.ResponseValue = new Optional(null, 2);

        await Pipeline.ResponseSerializer(Pipeline.Pool(), serializer).SerializeResponse(context);

        Assert.Equal("""{"Count":2}""", Pipeline.BodyOf(context));
    }

    [Fact]
    public async Task ThePooledBufferIsReturnedAfterUse() {
        var pool = Pipeline.Pool();
        var context = Pipeline.Context();

        context.Response.ResponseValue = new Pipeline.Payload("first", 2);

        await Pipeline.ResponseSerializer(pool).SerializeResponse(context);

        using var reservation = pool.Get();

        Assert.Equal(0, reservation.Item.Length);
    }

    /// <summary>
    /// The response body belongs to the transport, which writes headers and closes it afterwards.
    /// </summary>
    [Fact]
    public async Task TheResponseBodyIsLeftOpen() {
        var context = Pipeline.Context();

        context.Response.ResponseValue = new Pipeline.Payload("first", 2);

        await Pipeline.ResponseSerializer(Pipeline.Pool()).SerializeResponse(context);

        Assert.True(context.Response.Body.CanWrite, "the serializer closed a body it was handed");
    }

    [Fact]
    public async Task TwoResponsesOnOneInstanceDoNotBleedIntoEachOther() {
        var serializer = Pipeline.ResponseSerializer(Pipeline.Pool());

        var first = Pipeline.Context();
        first.Response.ResponseValue = new Pipeline.Payload("first", 1);

        var second = Pipeline.Context();
        second.Response.ResponseValue = new Pipeline.Payload("second", 2);

        await serializer.SerializeResponse(first);
        await serializer.SerializeResponse(second);

        Assert.Equal("""{"Name":"first","Count":1}""", Pipeline.BodyOf(first));
        Assert.Equal("""{"Name":"second","Count":2}""", Pipeline.BodyOf(second));
    }

    #region media type

    [Theory]
    [InlineData("application/json")]
    [InlineData("*/*")]
    [InlineData("application/*")]
    public void JsonMediaTypesAreClaimed(string mediaType) {
        Assert.True(
            Pipeline.ResponseSerializer(Pipeline.Pool()).CanProduce(mediaType, Pipeline.Context()));
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/html")]
    [InlineData("application/xml")]
    public void OtherMediaTypesAreNotClaimed(string mediaType) {
        Assert.False(
            Pipeline.ResponseSerializer(Pipeline.Pool()).CanProduce(mediaType, Pipeline.Context()));
    }

    [Fact]
    public void ItOffersItselfAsTheDefault() {
        Assert.True(Pipeline.ResponseSerializer(Pipeline.Pool()).IsDefaultSerializer);
    }

    #endregion

    private record Optional(string? Name, int Count);
}
