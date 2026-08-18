using System.Text;
using Xunit;

namespace Hardened.Web.StaticContent.Tests;

/// <summary>
/// The ETag is the whole of the conditional-request contract: a client sends back what it was
/// given, and the handler compares the two strings. Both halves of that only work if the value is
/// a pure function of the bytes.
/// </summary>
public class ETagProviderTests {

    private static ETagProvider Provider() => new(new TestMD5Pool());

    [Fact]
    public void TheSameContentAlwaysProducesTheSameETag() {
        var content = "the same bytes"u8.ToArray();

        Assert.Equal(Provider().GenerateETag(content), Provider().GenerateETag(content));
    }

    /// <summary>
    /// Two provider instances agree. The MD5 instances come from a pool and are reused across
    /// requests, so a hash left dirty by the previous caller would show up here.
    /// </summary>
    [Fact]
    public void TheSameProviderInstanceProducesTheSameETagForRepeatedContent() {
        var provider = Provider();
        var content = "repeated"u8.ToArray();

        var first = provider.GenerateETag(content);
        var second = provider.GenerateETag(content);
        var third = provider.GenerateETag(content);

        Assert.Equal(first, second);
        Assert.Equal(first, third);
    }

    /// <summary>
    /// Different content produces a different tag. An ETag that did not change when the file did
    /// would serve a 304 for content the client has never seen.
    /// </summary>
    [Fact]
    public void DifferentContentProducesADifferentETag() {
        var provider = Provider();

        Assert.NotEqual(
            provider.GenerateETag("version one"u8.ToArray()),
            provider.GenerateETag("version two"u8.ToArray()));
    }

    /// <summary>A one-byte difference is enough.</summary>
    [Fact]
    public void ASingleByteChangeChangesTheETag() {
        var provider = Provider();

        Assert.NotEqual(
            provider.GenerateETag(Encoding.UTF8.GetBytes(new string('a', 4096))),
            provider.GenerateETag(Encoding.UTF8.GetBytes(new string('a', 4095) + "b")));
    }

    /// <summary>An empty file still gets a tag rather than an empty string.</summary>
    [Fact]
    public void EmptyContentStillProducesAnETag() {
        Assert.NotEmpty(Provider().GenerateETag([]));
    }

    /// <summary>
    /// The tag is base64 of the raw MD5, which is 16 bytes and therefore always 24 characters
    /// with a single pad. The length is worth pinning because the value travels in a header.
    /// </summary>
    [Fact]
    public void TheETagIsBase64OfTheSixteenByteHash() {
        var etag = Provider().GenerateETag("content"u8.ToArray());

        Assert.Equal(24, etag.Length);
        Assert.Equal(16, Convert.FromBase64String(etag).Length);
    }
}
