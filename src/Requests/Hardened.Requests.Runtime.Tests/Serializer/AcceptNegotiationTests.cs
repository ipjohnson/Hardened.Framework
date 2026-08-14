using Hardened.Requests.Abstract.Serializer;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// Parsing an <c>Accept</c> header, and matching one entry against what a serializer writes.
/// </summary>
/// <remarks>
/// Both live in one place so no serializer implements them again. Three copies of
/// <c>Request.Accept?.Contains("application/json")</c> is how the framework came to decline
/// <c>*/*</c> and a missing header - the two most common shapes there are - and get away with it
/// only because JSON was also the fallback.
/// </remarks>
public class AcceptNegotiationTests {

    // ── parsing ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_KeepsTheListedOrder() {
        var accepted = AcceptedContentTypes.Parse("text/html,application/json");

        Assert.Equal(new[] { "text/html", "application/json" }, accepted.MediaTypes);
    }

    /// <summary>
    /// Parameters are dropped, q among them. Preference comes from the order types are listed in.
    /// </summary>
    [Fact]
    public void Parse_DropsParameters() {
        var accepted = AcceptedContentTypes.Parse(
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

        Assert.Equal(
            new[] { "text/html", "application/xhtml+xml", "application/xml", "*/*" },
            accepted.MediaTypes);
    }

    /// <summary>Version tags and charsets are parameters too, not part of the media type.</summary>
    [Fact]
    public void Parse_DropsNonQParameters() {
        Assert.Equal(
            new[] { "application/signed-exchange", "text/html" },
            AcceptedContentTypes.Parse("application/signed-exchange;v=b3;q=0.7,text/html;charset=utf-8")
                .MediaTypes);
    }

    [Fact]
    public void Parse_TrimsWhitespaceAroundEntries() {
        Assert.Equal(
            new[] { "text/html", "application/json" },
            AcceptedContentTypes.Parse(" text/html ,  application/json ").MediaTypes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("*/*")]
    public void Parse_AnIndifferentClientResolvesToTheSharedAnyInstance(string? header) {
        Assert.Same(AcceptedContentTypes.Any, AcceptedContentTypes.Parse(header));
    }

    [Fact]
    public void Parse_AnyAcceptsEverything() {
        Assert.Equal(new[] { "*/*" }, AcceptedContentTypes.Any.MediaTypes);
    }

    // ── matching ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("application/json", "application/json")]
    [InlineData("APPLICATION/JSON", "application/json")]
    [InlineData("*/*", "application/json")]
    [InlineData("application/*", "application/json")]
    [InlineData("text/*", "text/html")]
    public void Matches_True(string requested, string produced) {
        Assert.True(MediaType.Matches(requested, produced));
    }

    [Theory]
    [InlineData("text/html", "application/json")]
    [InlineData("text/*", "application/json")]
    [InlineData("application/*", "text/html")]
    [InlineData("json", "application/json")]
    public void Matches_False(string requested, string produced) {
        Assert.False(MediaType.Matches(requested, produced));
    }

    /// <summary>
    /// A client that sent nothing takes anything. Answering false here is the original defect.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Matches_AnAbsentRequestTakesAnything(string? requested) {
        Assert.True(MediaType.Matches(requested, "application/json"));
    }

    /// <summary>A serializer that produces nothing matches nothing, wildcard included.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Matches_FalseWhenNothingIsProduced(string? produced) {
        Assert.False(MediaType.Matches("*/*", produced));
    }

    /// <summary>
    /// A prefix that is not a subtype wildcard must not match by accident - <c>text/ht</c> is not
    /// a request for <c>text/html</c>.
    /// </summary>
    [Fact]
    public void Matches_DoesNotTreatAPartialTypeAsAWildcard() {
        Assert.False(MediaType.Matches("text/ht", "text/html"));
        Assert.False(MediaType.Matches("text", "text/html"));
    }
}
