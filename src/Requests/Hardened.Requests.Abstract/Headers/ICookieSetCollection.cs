using System.Text;

namespace Hardened.Requests.Abstract.Headers;

public enum SameSite {
    Strict,
    Lax,
    None
}

public record CookieSetOptions(
    DateTime? Expires = null,
    double? MaxAge = null,
    string? Domain = null,
    string? Path = null,
    bool Secure = true,
    bool HttpOnly = true,
    SameSite? SameSite = null
) {
    public static CookieSetOptions Empty = new();

    public void AppendSettings(StringBuilder builder) {
        if (Expires.HasValue) {
            builder.AppendFormat("; Expire={0:ddd, dd MMM yyyy HH:mm:ss}", Expires);
        }

        if (MaxAge.HasValue) {
            builder.Append("; Max-Age=");
            builder.Append(MaxAge);
        }

        if (!string.IsNullOrEmpty(Domain)) {
            builder.Append("; Domain=");
            builder.Append(Domain);
        }

        if (SameSite.HasValue) {
            builder.Append("; SameSite=");
            builder.Append(SameSite);
        }

        if (HttpOnly) {
            builder.Append("; HttpOnly");
        }

        if (Secure) {
            builder.Append("; Secure");
        }
    }
}

public interface ICookieSetCollection {
    void Append(string cookieName, string cookieValue, CookieSetOptions? options = null);

    IReadOnlyDictionary<string, Tuple<string, CookieSetOptions>> Cookies { get; }
}