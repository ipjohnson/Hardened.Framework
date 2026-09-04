using Hardened.Requests.Runtime.Compression;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Compression;

/// <summary>
/// The default media-type rule, and the pattern language it is written in.
/// </summary>
public class CompressionConfigurationTests {

    private static readonly CompressionConfiguration Defaults = new();

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("APPLICATION/JSON")]
    [InlineData("application/problem+json")]
    [InlineData("application/vnd.api+json")]
    [InlineData("application/xml")]
    [InlineData("application/atom+xml")]
    [InlineData("application/javascript")]
    [InlineData("application/x-ndjson")]
    [InlineData("image/svg+xml")]
    [InlineData("text/plain")]
    [InlineData("text/html; charset=utf-8")]
    [InlineData("text/css")]
    [InlineData("text/xml")]
    public void TheDefaultRuleCompressesTextLikeTypes(string contentType) {
        Assert.True(Defaults.Compresses(contentType));
    }

    /// <summary>
    /// Event streams are under <c>text/*</c> and excluded by name, and a binary type is not text
    /// however it is spelled. A missing content type is nothing to reason about and is left alone.
    /// </summary>
    [Theory]
    [InlineData("text/event-stream")]
    [InlineData("text/event-stream; charset=utf-8")]
    [InlineData("image/png")]
    [InlineData("application/octet-stream")]
    [InlineData("application/zip")]
    [InlineData("application/gzip")]
    [InlineData("video/mp4")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nonsense")]
    public void TheDefaultRuleLeavesTheRestAlone(string? contentType) {
        Assert.False(Defaults.Compresses(contentType));
    }

    [Fact]
    public void ATypeAddedToTheListIsCompressed() {
        var configuration = new CompressionConfiguration();

        configuration.MediaTypes.Add("application/wasm");

        Assert.True(configuration.Compresses("application/wasm"));
    }

    [Fact]
    public void AnExclusionBeatsAPatternThatAdmitsIt() {
        var configuration = new CompressionConfiguration();

        configuration.ExcludedMediaTypes.Add("text/csv");

        Assert.False(configuration.Compresses("text/csv"));
        Assert.True(configuration.Compresses("text/plain"));
    }

    [Fact]
    public void AWildcardTypeMatchesAnything() {
        var configuration = new CompressionConfiguration { MediaTypes = ["*/*"] };

        Assert.True(configuration.Compresses("application/octet-stream"));
    }

    [Fact]
    public void ASuffixPatternMatchesTheSuffixOnly() {
        var configuration = new CompressionConfiguration { MediaTypes = ["application/*+json"] };

        Assert.True(configuration.Compresses("application/hal+json"));
        Assert.False(configuration.Compresses("application/json"));
        Assert.False(configuration.Compresses("text/vnd.something+json"));
    }

    [Fact]
    public void TheDefaultsAreGzipThenBrotliAtTheFastestLevel() {
        Assert.Equal(["gzip", "br"], Defaults.Encodings);
        Assert.Equal(System.IO.Compression.CompressionLevel.Fastest, Defaults.Level);
        Assert.Equal(30_000_000, Defaults.MaxDecompressedRequestBytes);
    }
}
