using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// The <see cref="IJsonSerializer"/> an AOT-published application resolves.
/// </summary>
/// <remarks>
/// <para>
/// CI measured it at <b>0%</b> — the last class in <c>Hardened.Requests.Runtime</c> with nothing at
/// all. <c>AotSerializerModuleTests</c> asserts it is the registration the module makes; nothing
/// ever constructed one.
/// </para>
/// <para>
/// <b>The resolver chain is what is actually under test.</b> Registered contexts go on first and
/// <see cref="PrimitiveJsonTypeInfoResolver"/> goes on last, and the order is load-bearing in both
/// directions: a context has to answer first for the types it knows, and the primitive table has to
/// answer at all — <c>JsonMetadataServices.CreatePropertyInfo&lt;T&gt;</c> resolves its converter by
/// asking the options, so serializing a record with one string property asks the chain for
/// <c>string</c> too. Without that entry every string property throws <c>NotSupportedException</c>.
/// </para>
/// </remarks>
public class AotJsonSerializerTests {

    // Payload and PayloadContext are declared once for this namespace in
    // ResponseSerializerCompressionTests.cs, at namespace scope because System.Text.Json's
    // generator does not emit for a context nested inside a type that is not itself partial.

    /// <summary>Answers for nothing, but records that it was asked.</summary>
    private sealed class RecordingResolver : IJsonTypeInfoResolver {
        public List<Type> Asked { get; } = [];

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) {
            Asked.Add(type);

            return null;
        }
    }

    /// <summary>
    /// Typed as the interface, which is what an application resolves and what carries the optional
    /// <c>pretty</c> and cancellation arguments — the concrete class declares neither.
    /// </summary>
    private static IJsonSerializer Serializer(params IJsonTypeInfoResolver[] resolvers) {
        var configuration = new JsonSerializerConfiguration {
            Options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        };

        return new AotJsonSerializer(
            Options.Create<IJsonSerializerConfiguration>(configuration), resolvers);
    }

    private static IJsonSerializer WithPayloadContext() => Serializer(PayloadContext.Default);

    private static MemoryStream Json(string json) => new(Encoding.UTF8.GetBytes(json));

    #region round trips through a generated context

    [Fact]
    public void SerializeWritesAModelTheContextKnows() {
        Assert.Equal(
            """{"name":"first","value":2}""",
            WithPayloadContext().Serialize(new Payload("first", 2)));
    }

    [Fact]
    public void DeserializeReadsAModelTheContextKnows() {
        var payload = WithPayloadContext().Deserialize<Payload>("""{"name":"first","value":2}""");

        Assert.Equal("first", payload.Name);
        Assert.Equal(2, payload.Value);
    }

    [Fact]
    public async Task DeserializeAsyncReadsFromAStream() {
        var payload = await WithPayloadContext()
            .DeserializeAsync<Payload>(Json("""{"name":"first","value":2}"""), TestContext.Current.CancellationToken);

        Assert.Equal("first", payload.Name);
        Assert.Equal(2, payload.Value);
    }

    [Fact]
    public async Task SerializeAsyncWritesToAStream() {
        var stream = new MemoryStream();

        await WithPayloadContext().SerializeAsync(
            stream, new Payload("first", 2), false, TestContext.Current.CancellationToken);

        Assert.Equal("""{"name":"first","value":2}""", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public async Task AModelRoundTripsThroughTheStreamApi() {
        var serializer = WithPayloadContext();
        var stream = new MemoryStream();

        await serializer.SerializeAsync(
            stream, new Payload("first", 2), false, TestContext.Current.CancellationToken);

        stream.Position = 0;

        Assert.Equal(
            new Payload("first", 2),
            await serializer.DeserializeAsync<Payload>(stream, TestContext.Current.CancellationToken));
    }

    #endregion

    #region the primitive table underneath

    /// <summary>
    /// The chain has to answer for <c>string</c> as well as for the record. Without the primitive
    /// resolver every string property on every generated model throws.
    /// </summary>
    [Fact]
    public void AStringSerializesWithoutAContextDeclaringIt() {
        Assert.Equal("\"hello\"", WithPayloadContext().Serialize("hello"));
    }

    [Theory]
    [InlineData(42, "42")]
    [InlineData(true, "true")]
    [InlineData(1.5, "1.5")]
    public void ThePrimitiveLeafTypesSerialize(object value, string expected) {
        Assert.Equal(expected, WithPayloadContext().Serialize(value));
    }

    [Fact]
    public void AStringDeserializes() {
        Assert.Equal("hello", WithPayloadContext().Deserialize<string>("\"hello\""));
    }

    /// <summary>
    /// With no registered context at all the primitive table is the whole chain, and still answers
    /// for the leaf types.
    /// </summary>
    [Fact]
    public void ThePrimitiveTableAnswersWithNoRegisteredResolvers() {
        Assert.Equal("\"hello\"", Serializer().Serialize("hello"));
    }

    #endregion

    #region resolver ordering

    /// <summary>
    /// A registered resolver is asked before the primitive table, which is what lets an application
    /// override how a leaf type is written.
    /// </summary>
    [Fact]
    public void ARegisteredResolverIsAskedFirst() {
        var recording = new RecordingResolver();

        Serializer(recording).Serialize("hello");

        Assert.Contains(typeof(string), recording.Asked);
    }

    /// <summary>
    /// A resolver that answers nothing does not break the chain — the next one is asked.
    /// </summary>
    [Fact]
    public void AResolverThatAnswersNothingFallsThroughToTheNext() {
        Assert.Equal("\"hello\"", Serializer(new RecordingResolver()).Serialize("hello"));
    }

    [Fact]
    public void EveryRegisteredResolverIsInTheChain() {
        var first = new RecordingResolver();
        var second = new RecordingResolver();

        Serializer(first, second).Serialize("hello");

        Assert.Contains(typeof(string), first.Asked);
        Assert.Contains(typeof(string), second.Asked);
    }

    /// <summary>
    /// A type nothing in the chain answers for is a clear failure rather than a silent reflection
    /// fallback — which is the whole difference between this serializer and the reflection-based
    /// one, and what makes a missing context a startup-shaped problem instead of a request that
    /// behaves differently once published.
    /// </summary>
    [Fact]
    public void ATypeNoResolverKnowsThrows() {
        Assert.ThrowsAny<Exception>(() => Serializer().Serialize(new Payload("first", 2)));
    }

    #endregion

    #region pretty printing

    [Fact]
    public void PrettyPrintingIndents() {
        var pretty = WithPayloadContext().Serialize(new Payload("first", 2), pretty: true);

        Assert.Contains("\n", pretty);
        Assert.Contains("  ", pretty);
    }

    [Fact]
    public void TheCompactFormIsTheDefault() {
        Assert.DoesNotContain("\n", WithPayloadContext().Serialize(new Payload("first", 2)));
    }

    [Fact]
    public async Task SerializeAsyncCanBePretty() {
        var stream = new MemoryStream();

        await WithPayloadContext().SerializeAsync(
            stream, new Payload("first", 2), true, TestContext.Current.CancellationToken);

        Assert.Contains("\n", Encoding.UTF8.GetString(stream.ToArray()));
    }

    /// <summary>
    /// The pretty options carry the same resolver chain as the compact ones. Built separately, so a
    /// resolver added to one and not the other would make pretty printing fail on exactly the types
    /// the application declared.
    /// </summary>
    [Fact]
    public void PrettyPrintingUsesTheSameResolverChain() {
        Assert.Contains("first", WithPayloadContext().Serialize(new Payload("first", 2), pretty: true));
        Assert.Equal("\"hello\"", WithPayloadContext().Serialize("hello", pretty: true));
    }

    #endregion

    [Fact]
    public async Task DeserializingNullThrowsRatherThanReturningIt() {
        await Assert.ThrowsAsync<Exception>(
            () => WithPayloadContext().DeserializeAsync<Payload>(
                Json("null"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void DeserializingNullFromAStringThrowsToo() {
        Assert.Throws<Exception>(() => WithPayloadContext().Deserialize<Payload>("null"));
    }
}
