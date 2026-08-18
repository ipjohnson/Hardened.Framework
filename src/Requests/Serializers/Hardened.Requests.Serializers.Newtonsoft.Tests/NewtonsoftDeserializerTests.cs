using System.Text;
using Hardened.Requests.Serializers.Newtonsoft.Tests.Support;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace Hardened.Requests.Serializers.Newtonsoft.Tests;

/// <summary>
/// Reading a request body with Newtonsoft.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every test in this file failed when it was written.</b> <c>DeserializeRequestBody</c> returned
/// <c>default(T)</c> for every request and had done since the package shipped: it took one
/// reservation from the memory stream pool, copied the body into a second, seeked a third, and read
/// the first — which was empty. Two of the three were never returned to the pool. Fixed 2026-08-18;
/// see the method's own remarks.
/// </para>
/// <para>
/// The assembly was at 0% line coverage with no test project, which is exactly why a shipped
/// package could be entirely non-functional without anything going red.
/// </para>
/// </remarks>
public class NewtonsoftDeserializerTests {

    [Fact]
    public async Task AJsonBodyIsRead() {
        var payload = await Pipeline.Deserializer(Pipeline.Pool())
            .DeserializeRequestBody<Pipeline.Payload>(
                Pipeline.Context("""{"Name":"first","Count":2}"""));

        Assert.NotNull(payload);
        Assert.Equal("first", payload.Name);
        Assert.Equal(2, payload.Count);
    }

    [Fact]
    public async Task ABodyLargerThanThePoolsInitialBufferIsReadWhole() {
        var name = new string('a', 8192);

        var payload = await Pipeline.Deserializer(Pipeline.Pool())
            .DeserializeRequestBody<Pipeline.Payload>(
                Pipeline.Context($$"""{"Name":"{{name}}","Count":2}"""));

        Assert.NotNull(payload);
        Assert.Equal(name, payload.Name);
    }

    [Fact]
    public async Task AnEmptyBodyIsNull() {
        Assert.Null(
            await Pipeline.Deserializer(Pipeline.Pool())
                .DeserializeRequestBody<Pipeline.Payload>(Pipeline.Context()));
    }

    [Fact]
    public async Task AJsonNullBodyIsNull() {
        Assert.Null(
            await Pipeline.Deserializer(Pipeline.Pool())
                .DeserializeRequestBody<Pipeline.Payload>(Pipeline.Context("null")));
    }

    [Fact]
    public async Task AMalformedBodyThrows() {
        await Assert.ThrowsAsync<JsonReaderException>(
            () => Pipeline.Deserializer(Pipeline.Pool())
                .DeserializeRequestBody<Pipeline.Payload>(Pipeline.Context("{not json")).AsTask());
    }

    /// <summary>
    /// The configured serializer is the one that runs, which is the whole reason the package exists.
    /// </summary>
    [Fact]
    public async Task TheConfiguredNamingStrategyIsHonoured() {
        var serializer = JsonSerializer.CreateDefault(new JsonSerializerSettings {
            ContractResolver = new DefaultContractResolver {
                NamingStrategy = new SnakeCaseNamingStrategy()
            }
        });

        var payload = await Pipeline.Deserializer(Pipeline.Pool(), serializer)
            .DeserializeRequestBody<SnakePayload>(
                Pipeline.Context("""{"first_name":"ada"}"""));

        Assert.NotNull(payload);
        Assert.Equal("ada", payload.FirstName);
    }

    /// <summary>
    /// One reservation per request, and it goes back.
    /// </summary>
    /// <remarks>
    /// A leaked reservation is invisible through the pool's own interface — it just makes another
    /// stream — so this counts instead. The original took three per request and returned one, which
    /// is a <c>MemoryStream</c> leaked per request per two of them, growing with traffic.
    /// </remarks>
    [Fact]
    public async Task OneReservationIsTakenPerRequestAndReturned() {
        var pool = new Pipeline.CountingPool();

        await Pipeline.Deserializer(pool)
            .DeserializeRequestBody<Pipeline.Payload>(
                Pipeline.Context("""{"Name":"first","Count":2}"""));

        Assert.Equal(1, pool.Taken);
        Assert.Equal(1, pool.Returned);
    }

    /// <summary>
    /// A body that failed to parse still gives its reservation back — the <c>using</c> has to hold
    /// on the throwing path, or a malformed-request flood drains the pool.
    /// </summary>
    [Fact]
    public async Task AReservationIsReturnedEvenWhenParsingThrows() {
        var pool = new Pipeline.CountingPool();

        await Assert.ThrowsAsync<JsonReaderException>(
            () => Pipeline.Deserializer(pool)
                .DeserializeRequestBody<Pipeline.Payload>(Pipeline.Context("{not json")).AsTask());

        Assert.Equal(1, pool.Taken);
        Assert.Equal(1, pool.Returned);
    }

    /// <summary>
    /// The returned stream is reset on its way back, so a borrower never sees the last request's
    /// bytes.
    /// </summary>
    [Fact]
    public async Task AReturnedStreamCarriesNothingFromTheLastRequest() {
        var pool = Pipeline.Pool();

        await Pipeline.Deserializer(pool)
            .DeserializeRequestBody<Pipeline.Payload>(
                Pipeline.Context("""{"Name":"first","Count":2}"""));

        using var reservation = pool.Get();

        Assert.Equal(0, reservation.Item.Length);
    }

    /// <summary>
    /// Two calls on one instance must not interfere — the deserializer is registered transient but
    /// nothing stops a scope resolving it once and using it twice.
    /// </summary>
    [Fact]
    public async Task TwoCallsOnOneInstanceEachReadTheirOwnBody() {
        var deserializer = Pipeline.Deserializer(Pipeline.Pool());

        var first = await deserializer.DeserializeRequestBody<Pipeline.Payload>(
            Pipeline.Context("""{"Name":"first","Count":1}"""));
        var second = await deserializer.DeserializeRequestBody<Pipeline.Payload>(
            Pipeline.Context("""{"Name":"second","Count":2}"""));

        Assert.Equal("first", first!.Name);
        Assert.Equal("second", second!.Name);
    }

    /// <summary>
    /// The request body belongs to the transport. Closing it here would break a host that reads
    /// anything after binding — and a retry, which rewinds and reads it again.
    /// </summary>
    [Fact]
    public async Task TheRequestBodyIsLeftOpen() {
        var context = Pipeline.Context("""{"Name":"first","Count":2}""");

        await Pipeline.Deserializer(Pipeline.Pool())
            .DeserializeRequestBody<Pipeline.Payload>(context);

        Assert.True(context.Request.Body.CanRead, "the deserializer closed a body it was handed");
    }

    #region content type

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    public void AJsonContentTypeIsClaimed(string contentType) {
        Assert.True(
            Pipeline.Deserializer(Pipeline.Pool())
                .CanProcessContext(Pipeline.Context(contentType: contentType)));
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    [InlineData("application/octet-stream")]
    public void AnotherContentTypeIsNotClaimed(string contentType) {
        Assert.False(
            Pipeline.Deserializer(Pipeline.Pool())
                .CanProcessContext(Pipeline.Context(contentType: contentType)));
    }

    [Fact]
    public void AMissingContentTypeIsNotClaimed() {
        Assert.False(
            Pipeline.Deserializer(Pipeline.Pool())
                .CanProcessContext(Pipeline.Context(contentType: null)));
    }

    [Fact]
    public void ItOffersItselfAsTheDefault() {
        Assert.True(Pipeline.Deserializer(Pipeline.Pool()).IsDefaultSerializer);
    }

    #endregion

    private class SnakePayload {
        public string? FirstName { get; set; }
    }
}
