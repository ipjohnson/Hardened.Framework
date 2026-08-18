using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Headers;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Headers;

/// <summary>
/// The collection a handler appends response cookies to.
/// </summary>
/// <remarks>
/// <c>CookieSetOptionsTests</c> covers what an option set renders as; this covers the collection
/// underneath it, which was at 20%. The behaviour that is not obvious from the signature is that
/// <c>Append</c> indexes rather than adds — a second append under the same name replaces the first
/// instead of emitting two <c>Set-Cookie</c> headers for it. That is the right answer (a browser
/// keeps the last one anyway) and it is worth being deliberate about.
/// </remarks>
public class CookieSetCollectionImplTests {

    [Fact]
    public void AnAppendedCookieIsReadableByName() {
        var collection = new CookieSetCollectionImpl();

        collection.Append("session", "abc123");

        var (value, _) = collection.Cookies["session"];

        Assert.Equal("abc123", value);
    }

    /// <summary>
    /// Omitting options must not leave a null on the entry — <c>AppendSettings</c> is called on it
    /// unconditionally when the header is rendered.
    /// </summary>
    [Fact]
    public void AnAppendWithoutOptionsGetsTheEmptyOptions() {
        var collection = new CookieSetCollectionImpl();

        collection.Append("session", "abc123");

        var (_, options) = collection.Cookies["session"];

        Assert.Same(CookieSetOptions.Empty, options);
    }

    [Fact]
    public void SuppliedOptionsAreKept() {
        var collection = new CookieSetCollectionImpl();
        var options = new CookieSetOptions(Path: "/admin", SameSite: SameSite.Strict);

        collection.Append("session", "abc123", options);

        Assert.Same(options, collection.Cookies["session"].Item2);
    }

    [Fact]
    public void SeveralNamesAreAllKept() {
        var collection = new CookieSetCollectionImpl();

        collection.Append("session", "abc123");
        collection.Append("theme", "dark");

        Assert.Equal(2, collection.Cookies.Count);
        Assert.Equal("abc123", collection.Cookies["session"].Item1);
        Assert.Equal("dark", collection.Cookies["theme"].Item1);
    }

    /// <summary>
    /// Appending the same name twice replaces rather than accumulating.
    /// </summary>
    [Fact]
    public void AppendingTheSameNameTwiceKeepsTheLastValue() {
        var collection = new CookieSetCollectionImpl();

        collection.Append("session", "first");
        collection.Append("session", "second");

        Assert.Single(collection.Cookies);
        Assert.Equal("second", collection.Cookies["session"].Item1);
    }

    /// <summary>
    /// Replacing a value must replace its options too, or a cookie re-issued without a
    /// <c>Path</c> silently keeps the earlier one's scope.
    /// </summary>
    [Fact]
    public void AppendingTheSameNameTwiceReplacesTheOptionsAsWell() {
        var collection = new CookieSetCollectionImpl();

        collection.Append("session", "first", new CookieSetOptions(Path: "/admin"));
        collection.Append("session", "second");

        Assert.Same(CookieSetOptions.Empty, collection.Cookies["session"].Item2);
    }

    [Fact]
    public void CookieNamesAreCaseSensitive() {
        var collection = new CookieSetCollectionImpl();

        collection.Append("Session", "upper");
        collection.Append("session", "lower");

        Assert.Equal(2, collection.Cookies.Count);
    }

    [Fact]
    public void AFreshCollectionHasNoCookies() {
        Assert.Empty(new CookieSetCollectionImpl().Cookies);
    }
}
