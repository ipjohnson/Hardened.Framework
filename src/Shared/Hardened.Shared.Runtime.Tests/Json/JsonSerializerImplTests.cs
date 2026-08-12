using System.Text;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Json;

/// <summary>
/// The serializer every request body goes through.
///
/// <para>
/// The behaviour worth guarding is ownership: it reads and writes streams it is handed and closes
/// none of them. <c>DeserializeAsync</c> used to open a <c>StreamReader</c> over the stream in a
/// <c>using</c> and never read from it — deserialization goes to the stream directly — so the
/// reader's only effect was its disposal, and the default constructor is <c>leaveOpen: false</c>.
/// Every call closed a stream it did not own. Fixed 2026-08-12.
/// </para>
/// </summary>
public class JsonSerializerImplTests {

    private record Payload(string Name, int Count);

    private static IJsonSerializer Serializer() {
        var configuration = Substitute.For<IJsonSerializerConfiguration>();

        configuration.Options.Returns(new System.Text.Json.JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        });

        return new JsonSerializerImpl(Options.Create(configuration));
    }

    private static MemoryStream Json(string json) => new(Encoding.UTF8.GetBytes(json));

    [Fact]
    public async Task DeserializeReadsTheStream() {
        var payload = await Serializer()
            .DeserializeAsync<Payload>(
                Json("""{"name":"first","count":2}"""), TestContext.Current.CancellationToken);

        Assert.Equal("first", payload.Name);
        Assert.Equal(2, payload.Count);
    }

    /// <summary>
    /// The caller closes what the caller opened. Anything that pools or reuses a stream depends on
    /// this — see <see cref="ReturningADeserializedStreamToThePoolDoesNotThrow"/>.
    /// </summary>
    [Fact]
    public async Task DeserializeLeavesTheStreamOpen() {
        var stream = Json("""{"name":"first","count":2}""");

        await Serializer().DeserializeAsync<Payload>(stream, TestContext.Current.CancellationToken);

        Assert.True(stream.CanRead, "DeserializeAsync closed a stream it was handed");

        stream.Position = 0;
    }

    /// <summary>
    /// The shape the SQS integration harness hit: a pooled stream is handed to the pipeline, the
    /// pipeline deserializes it, and the caller then returns the reservation. The pool resets
    /// <c>Position</c> on return, which throws on a closed stream.
    /// </summary>
    [Fact]
    public async Task ReturningADeserializedStreamToThePoolDoesNotThrow() {
        var pool = new MemoryStreamPool();
        var serializer = Serializer();

        using (var reservation = pool.Get()) {
            await serializer.SerializeAsync(reservation.Item, new Payload("pooled", 1), cancellationToken: TestContext.Current.CancellationToken);

            reservation.Item.Position = 0;

            var payload = await serializer.DeserializeAsync<Payload>(reservation.Item, TestContext.Current.CancellationToken);

            Assert.Equal("pooled", payload.Name);
        }
    }

    [Fact]
    public async Task SerializeLeavesTheStreamOpen() {
        var stream = new MemoryStream();

        await Serializer().SerializeAsync(stream, new Payload("written", 3), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(stream.CanWrite, "SerializeAsync closed a stream it was handed");
        Assert.True(stream.Length > 0);
    }

    /// <summary>Two payloads written to one stream, which only works if the first left it open.</summary>
    [Fact]
    public async Task AStreamCanBeReusedAcrossCalls() {
        var serializer = Serializer();
        var stream = new MemoryStream();

        await serializer.SerializeAsync(stream, new Payload("first", 1), cancellationToken: TestContext.Current.CancellationToken);

        stream.Position = 0;

        var first = await serializer.DeserializeAsync<Payload>(stream, TestContext.Current.CancellationToken);

        stream.SetLength(0);

        await serializer.SerializeAsync(stream, new Payload("second", 2), cancellationToken: TestContext.Current.CancellationToken);

        stream.Position = 0;

        var second = await serializer.DeserializeAsync<Payload>(stream, TestContext.Current.CancellationToken);

        Assert.Equal("first", first.Name);
        Assert.Equal("second", second.Name);
    }
}
