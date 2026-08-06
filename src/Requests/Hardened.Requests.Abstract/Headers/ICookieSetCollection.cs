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
            // RFC 6265 names the attribute "Expires" and requires an RFC 1123 date in GMT.
            // The "R" format specifier produces exactly that, but only reads correctly if
            // the value is already UTC.
            builder.AppendFormat("; Expires={0:R}", Expires.Value.ToUniversalTime());
        }

        if (MaxAge.HasValue) {
            builder.Append("; Max-Age=");
            builder.Append(MaxAge);
        }

        if (!string.IsNullOrEmpty(Domain)) {
            builder.Append("; Domain=");
            builder.Append(Domain);
        }

        if (!string.IsNullOrEmpty(Path)) {
            builder.Append("; Path=");
            builder.Append(Path);
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