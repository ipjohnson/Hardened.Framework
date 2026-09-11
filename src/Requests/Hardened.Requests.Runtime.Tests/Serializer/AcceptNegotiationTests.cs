using Hardened.Requests.Abstract.Serializer;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// Walking an <c>Accept</c> header, and matching one entry against what a serializer writes.
/// </summary>
/// <remarks>
/// <para>
/// Both live in one place so no serializer implements them again. Three copies of
/// <c>Request.Accept?.Contains("application/json")</c> is how the framework came to decline
/// <c>*/*</c> and a missing header - the two most common shapes there are - and get away with it
/// only because JSON was also the fallback.
/// </para>
/// <para>
/// The header used to be split into a <c>List&lt;string&gt;</c> and these tests asserted on the
/// list. They assert on the answers instead, because there is no list any more: the same cases,
/// read through <see cref="MediaType.Enumerate"/> and <see cref="MediaType.FirstAccepted"/>.
/// </para>
/// </remarks>
public class AcceptNegotiationTests {

    // ── walking the header ─────────────────────────────────────────────

    private static string[] Walk(string? accept) {
        var entries = new List<string>();

        foreach (var mediaType in MediaType.Enumerate(accept)) {
            entries.Add(mediaType.ToString());
        }

        return entries.ToArray();
    }

    [Fact]
    public void Enumerate_KeepsTheListedOrder() {
        Assert.Equal(new[] { "text/html", "application/json" }, Walk("text/html,application/json"));
    }

    /// <summary>
    /// Parameters are dropped, q among them. Preference comes from the order types are listed in.
    /// </summary>
    [Fact]
    public void Enumerate_DropsParameters() {
        Assert.Equal(
            new[] { "text/html", "application/xhtml+xml", "application/xml", "*/*" },
            Walk("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));
    }

    /// <summary>Version tags and charsets are parameters too, not part of the media type.</summary>
    [Fact]
    public void Enumerate_DropsNonQParameters() {
        Assert.Equal(
            new[] { "application/signed-exchange", "text/html" },
            Walk("application/signed-exchange;v=b3;q=0.7,text/html;charset=utf-8"));
    }

    [Fact]
    public void Enumerate_TrimsWhitespaceAroundEntries() {
        Assert.Equal(
            new[] { "text/html", "application/json" },
            Walk(" text/html ,  application/json "));
    }

    /// <summary>
    /// A client that named nothing stated no preference, which is the same as <c>*/*</c>. Yielding
    /// it keeps every caller to one loop rather than a loop and a fallback.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("*/*")]
    [InlineData(",")]
    [InlineData(";q=1")]
    public void Enumerate_AnIndifferentClientYieldsTheWildcardOnce(string? header) {
        Assert.Equal(new[] { "*/*" }, Walk(header));
    }

    // ── the answers callers actually ask for ───────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("*/*")]
    [InlineData("text/csv,application/json")]
    public void Accepts_True(string? accept) {
        Assert.True(MediaType.Accepts(accept, "application/json"));
    }

    [Theory]
    [InlineData("text/csv")]
    [InlineData("text/csv,text/html;q=0.9")]
    public void Accepts_False(string accept) {
        Assert.False(MediaType.Accepts(accept, "application/json"));
    }

    /// <summary>A serializer that produces nothing is accepted by nobody, wildcard included.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Accepts_FalseWhenNothingIsProduced(string? produced) {
        Assert.False(MediaType.Accepts("*/*", produced));
    }

    /// <summary>
    /// The client's ranking decides, not the order the operation declares its representations in.
    /// </summary>
    [Fact]
    public void FirstAccepted_TakesTheClientsPreferenceOrder() {
        Assert.Equal(
            1,
            MediaType.FirstAccepted(
                "application/json,text/csv", new[] { "text/csv", "application/json" }));
    }

    /// <summary>
    /// A client expressing no preference is answered with the first representation declared, which
    /// is the one a document leads with.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("*/*")]
    public void FirstAccepted_AnIndifferentClientTakesTheFirstDeclared(string? accept) {
        Assert.Equal(0, MediaType.FirstAccepted(accept, new[] { "text/csv", "application/json" }));
    }

    [Fact]
    public void FirstAccepted_MinusOneWhenNothingIsOnOffer() {
        Assert.Equal(-1, MediaType.FirstAccepted("application/pdf", new[] { "text/csv" }));
    }

    [Fact]
    public void FirstAccepted_MinusOneWhenNothingIsDeclared() {
        Assert.Equal(-1, MediaType.FirstAccepted("*/*", Array.Empty<string>()));
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
