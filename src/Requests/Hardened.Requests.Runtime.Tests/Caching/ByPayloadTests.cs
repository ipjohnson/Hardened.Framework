using System.Text;
using Hardened.Requests.Runtime.Caching;
using Hardened.Requests.Runtime.Tests.Support;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Caching;

/// <summary>
/// Keying on the request body, and putting the body back for the bind that follows.
/// </summary>
public class ByPayloadTests {

    private static async Task<string?> KeyFor(string body) {
        var context = Pipeline.Context("POST", "/quotes", body: Encoding.UTF8.GetBytes(body));

        return await ByPayload.Create([]).Key(context);
    }

    [Fact]
    public async Task TheSamePayloadKeysTheSame() {
        Assert.Equal(await KeyFor("""{"sku":"a"}"""), await KeyFor("""{"sku":"a"}"""));
    }

    [Fact]
    public async Task ADifferentPayloadKeysDifferently() {
        Assert.NotEqual(await KeyFor("""{"sku":"a"}"""), await KeyFor("""{"sku":"b"}"""));
    }

    /// <summary>
    /// The filter runs ahead of the bind, so hashing the body consumes the stream the bind is about
    /// to read. This is the half that puts it back.
    /// </summary>
    [Fact]
    public async Task TheBodyIsReadableAgainAfterwards() {
        var context = Pipeline.Context("POST", "/quotes", body: "payload"u8.ToArray());

        await ByPayload.Create([]).Key(context);

        using var reader = new StreamReader(context.Request.Body);

        Assert.Equal("payload", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A GET has no body and pays nothing, rather than hashing an empty stream differently every
    /// time.
    /// </summary>
    [Fact]
    public async Task ARequestWithNoBodyKeysAsEmpty() {
        var context = Pipeline.Context();

        Assert.Equal(string.Empty, await ByPayload.Create([]).Key(context));
    }

    /// <summary>
    /// <c>params string[]</c> cannot say "no arguments", so refusing them here is what turns
    /// <c>[CacheResponse&lt;ByPayload&gt;("culture")]</c> from a silently ignored argument into a
    /// failure.
    /// </summary>
    [Fact]
    public void ValuesAreRefused() {
        var exception = Assert.Throws<ArgumentException>(() => ByPayload.Create(["culture"]));

        Assert.Contains("culture", exception.Message);
    }
}
