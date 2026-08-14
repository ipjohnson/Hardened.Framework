using Hardened.Requests.Runtime.Execution;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Execution;

/// <summary>
/// Reading a named cookie out of the raw strings a transport received.
/// </summary>
/// <remarks>
/// Transports differ: some hand over one <c>Cookie</c> header carrying every pair, some split them.
/// Both have to work, because which one a request went through is not something a handler should
/// have to know.
/// </remarks>
public class CookieCollectionExtensionsTests {

    [Fact]
    public void ASinglePairIsFound() {
        Assert.Equal("abc123", new[] { "session=abc123" }.Get("session").ToString());
    }

    [Fact]
    public void OnePairPerEntryIsFound() {
        var cookies = new[] { "session=abc123", "theme=dark" };

        Assert.Equal("abc123", cookies.Get("session").ToString());
        Assert.Equal("dark", cookies.Get("theme").ToString());
    }

    /// <summary>A whole header value, which is how ASP.NET hands them over.</summary>
    [Fact]
    public void SeveralPairsInOneEntryAreFound() {
        var cookies = new[] { "session=abc123; theme=dark; lang=en" };

        Assert.Equal("abc123", cookies.Get("session").ToString());
        Assert.Equal("dark", cookies.Get("theme").ToString());
        Assert.Equal("en", cookies.Get("lang").ToString());
    }

    /// <summary>
    /// A miss is empty rather than an exception, matching what the query string and header
    /// collections do — the binder turns an empty value into a validation error or a default.
    /// </summary>
    [Fact]
    public void AMissIsEmpty() {
        Assert.Equal(StringValuesEmpty, new[] { "session=abc" }.Get("absent").ToString());
        Assert.Equal(StringValuesEmpty, System.Array.Empty<string>().Get("session").ToString());
    }

    private const string StringValuesEmpty = "";

    /// <summary>Names are case-sensitive, as cookie names are.</summary>
    [Fact]
    public void NamesAreMatchedExactly() {
        Assert.Equal("", new[] { "Session=abc" }.Get("session").ToString());
    }

    /// <summary>A value containing an equals sign keeps all of it.</summary>
    [Fact]
    public void AValueMayContainAnEqualsSign() {
        Assert.Equal("a=b=c", new[] { "token=a=b=c" }.Get("token").ToString());
    }

    [Fact]
    public void AnEmptyValueIsFoundAndEmpty() {
        Assert.Equal("", new[] { "session=; theme=dark" }.Get("session").ToString());
        Assert.Equal("dark", new[] { "session=; theme=dark" }.Get("theme").ToString());
    }

    /// <summary>Entries that are not pairs at all are skipped rather than throwing.</summary>
    [Fact]
    public void MalformedEntriesAreSkipped() {
        var cookies = new[] { "", "novalue", "=novalue", "theme=dark" };

        Assert.Equal("dark", cookies.Get("theme").ToString());
    }
}
