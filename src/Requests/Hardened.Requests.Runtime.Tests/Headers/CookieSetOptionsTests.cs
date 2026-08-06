using System.Text;
using Hardened.Requests.Abstract.Headers;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Headers;

/// <summary>
/// Cookie attributes are a security surface - Secure, HttpOnly and SameSite are what keep a
/// session cookie out of reach of script and cross-site requests - so the emitted string is
/// asserted exactly.
/// </summary>
public class CookieSetOptionsTests {

    private static string Render(CookieSetOptions options) {
        var builder = new StringBuilder();
        options.AppendSettings(builder);
        return builder.ToString();
    }

    [Fact]
    public void DefaultsAreSecureAndHttpOnly() {
        var options = new CookieSetOptions();

        Assert.True(options.Secure);
        Assert.True(options.HttpOnly);

        var rendered = Render(options);
        Assert.Contains("; Secure", rendered);
        Assert.Contains("; HttpOnly", rendered);
    }

    [Fact]
    public void SecureAndHttpOnlyAreOmittedWhenDisabled() {
        var rendered = Render(new CookieSetOptions(Secure: false, HttpOnly: false));

        Assert.DoesNotContain("Secure", rendered);
        Assert.DoesNotContain("HttpOnly", rendered);
    }

    [Theory]
    [InlineData(SameSite.Strict, "; SameSite=Strict")]
    [InlineData(SameSite.Lax, "; SameSite=Lax")]
    [InlineData(SameSite.None, "; SameSite=None")]
    public void SameSiteIsEmittedWhenSet(SameSite sameSite, string expected) {
        Assert.Contains(expected, Render(new CookieSetOptions(SameSite: sameSite)));
    }

    [Fact]
    public void SameSiteIsOmittedWhenNull() {
        Assert.DoesNotContain("SameSite", Render(new CookieSetOptions()));
    }

    [Fact]
    public void MaxAgeIsEmittedWhenSet() {
        Assert.Contains("; Max-Age=3600", Render(new CookieSetOptions(MaxAge: 3600)));
    }

    [Fact]
    public void DomainIsEmittedWhenSet() {
        Assert.Contains("; Domain=example.com", Render(new CookieSetOptions(Domain: "example.com")));
    }

    [Fact]
    public void DomainIsOmittedWhenEmpty() {
        Assert.DoesNotContain("Domain", Render(new CookieSetOptions(Domain: "")));
    }

    [Fact]
    public void EmptyIsAReusableDefaultInstance() {
        Assert.NotNull(CookieSetOptions.Empty);
        Assert.True(CookieSetOptions.Empty.Secure);
        Assert.True(CookieSetOptions.Empty.HttpOnly);
    }

    /// <summary>
    /// A cookie scoped to a path must actually carry that scope, otherwise the browser
    /// applies it to the whole origin and it is sent on requests it was never meant for.
    /// </summary>
    [Fact]
    public void PathIsEmittedWhenSet() {
        Assert.Contains("; Path=/admin", Render(new CookieSetOptions(Path: "/admin")));
    }

    [Fact]
    public void PathIsOmittedWhenNotSet() {
        Assert.DoesNotContain("Path=", Render(new CookieSetOptions()));
    }

    /// <summary>
    /// RFC 6265 names the attribute "Expires" and requires an RFC 1123 date in GMT. Anything
    /// else is ignored by the browser, which silently turns an expiring cookie into a
    /// session cookie.
    /// </summary>
    [Fact]
    public void ExpiresUsesTheRfcAttributeNameAndGmtFormat() {
        var rendered = Render(new CookieSetOptions(
            Expires: new DateTime(2026, 6, 9, 10, 18, 14, DateTimeKind.Utc)));

        Assert.Contains("; Expires=", rendered);
        Assert.Contains("GMT", rendered);
        Assert.Contains("Tue, 09 Jun 2026 10:18:14 GMT", rendered);
    }

    [Fact]
    public void ExpiresIsConvertedToUtcBeforeFormatting() {
        var local = new DateTime(2026, 6, 9, 10, 18, 14, DateTimeKind.Utc).ToLocalTime();

        var rendered = Render(new CookieSetOptions(Expires: local));

        Assert.Contains("Tue, 09 Jun 2026 10:18:14 GMT", rendered);
    }

    [Fact]
    public void ExpiresIsOmittedWhenNotSet() {
        Assert.DoesNotContain("Expires", Render(new CookieSetOptions()));
    }

    [Fact]
    public void AttributesCombineInASingleRender() {
        var rendered = Render(new CookieSetOptions(
            MaxAge: 600,
            Domain: "example.com",
            SameSite: SameSite.Strict));

        Assert.Contains("; Max-Age=600", rendered);
        Assert.Contains("; Domain=example.com", rendered);
        Assert.Contains("; SameSite=Strict", rendered);
        Assert.Contains("; HttpOnly", rendered);
        Assert.Contains("; Secure", rendered);
    }
}
