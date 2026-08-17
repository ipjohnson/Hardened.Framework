using System.Text;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// What a refused request is told: the status, and the <c>WWW-Authenticate</c> header that says why
/// and what would fix it.
/// </summary>
/// <remarks>
/// <para>
/// A type of its own rather than logic on an exception, because a refusal reaches the response two
/// different ways. An authorization filter placed after the serializing filter can throw and let the
/// exception path answer; one placed before it cannot, because nothing between there and the host
/// catches - so it hands the response the same challenge directly. Both must produce byte-identical
/// headers, which is only guaranteed if there is one thing that formats them.
/// </para>
/// <para>
/// The factory methods are the whole vocabulary, and they are exhaustive on purpose: the four cases
/// are the four rows of the design's response table, and inventing a fifth at a call site is how an
/// API ends up with two spellings of the same challenge.
/// </para>
/// </remarks>
public sealed class AuthorizationChallenge {
    public const string HeaderName = "WWW-Authenticate";

    public const string BearerScheme = "Bearer";

    private AuthorizationChallenge(
        int statusCode,
        string scheme,
        string? error,
        string? realm,
        IReadOnlyList<string> scope,
        string? description) {
        StatusCode = statusCode;
        Scheme = scheme;
        Error = error;
        Realm = realm;
        Scope = scope;
        Description = description;
        HeaderValue = Format(scheme, error, realm, scope, description);
    }

    /// <summary>
    /// No credential was presented - 401, and no <c>error</c> parameter.
    /// </summary>
    /// <remarks>
    /// RFC 6750 §3 is explicit that a challenge for a request carrying no credential omits
    /// <c>error</c>: there is no token to have been wrong about. Sending
    /// <c>error="invalid_token"</c> here would tell a client its credential was rejected when it
    /// never sent one, which sends it to refresh a token rather than to obtain one.
    /// </remarks>
    public static AuthorizationChallenge AuthenticationRequired(string? realm = null) =>
        new(401, BearerScheme, error: null, realm, [], description: null);

    /// <summary>
    /// A credential was presented and is not valid - 401, <c>error="invalid_token"</c>.
    /// </summary>
    public static AuthorizationChallenge InvalidToken(string? realm = null, string? description = null) =>
        new(401, BearerScheme, "invalid_token", realm, [], description);

    /// <summary>
    /// The credential is valid but too weak for this operation - 401,
    /// <c>error="insufficient_user_authentication"</c> (RFC 9470).
    /// </summary>
    /// <remarks>
    /// The one refusal of an authenticated caller that is a 401 rather than a 403, because the
    /// remedy is a stronger credential rather than more grants.
    /// </remarks>
    public static AuthorizationChallenge InsufficientAuthentication(
        string? realm = null, string? description = null) =>
        new(401, BearerScheme, "insufficient_user_authentication", realm, [], description);

    /// <summary>
    /// The caller is authenticated and lacks the grants - 403, <c>error="insufficient_scope"</c>.
    /// </summary>
    /// <remarks>
    /// A 403 still carries a challenge. The requirement knows exactly which grants it wanted, so
    /// naming them costs nothing and turns "no" into "no, and here is what you would need".
    /// </remarks>
    public static AuthorizationChallenge InsufficientScope(
        IEnumerable<string> requiredGrants, string? realm = null) =>
        new(403, BearerScheme, "insufficient_scope", realm, [..requiredGrants], description: null);

    public int StatusCode { get; }

    public string Scheme { get; }

    /// <summary>The <c>error</c> parameter, or null when the challenge carries none.</summary>
    public string? Error { get; }

    public string? Realm { get; }

    /// <summary>The grants that would have satisfied the requirement. Empty unless this is a 403.</summary>
    public IReadOnlyList<string> Scope { get; }

    public string? Description { get; }

    /// <summary>The formatted header value, built once.</summary>
    public string HeaderValue { get; }

    /// <summary>
    /// Writes the challenge onto a response.
    /// </summary>
    /// <remarks>
    /// Assigns rather than appends, so a forked or retried chain that refuses the same request twice
    /// sends one challenge rather than two.
    /// </remarks>
    public void Apply(IDictionary<string, StringValues> headers) {
        ArgumentNullException.ThrowIfNull(headers);

        headers[HeaderName] = HeaderValue;
    }

    public override string ToString() => HeaderValue;

    private static string Format(
        string scheme, string? error, string? realm, IReadOnlyList<string> scope, string? description) {
        var builder = new StringBuilder(scheme);
        var first = true;

        void Parameter(string name, string value) {
            builder.Append(first ? " " : ", ");
            first = false;
            builder.Append(name).Append("=\"").Append(Quote(value)).Append('"');
        }

        // realm first, which is the order RFC 6750's own examples use.
        if (!string.IsNullOrEmpty(realm)) {
            Parameter("realm", realm);
        }

        if (!string.IsNullOrEmpty(error)) {
            Parameter("error", error);
        }

        if (!string.IsNullOrEmpty(description)) {
            Parameter("error_description", description);
        }

        if (scope.Count > 0) {
            // Space-delimited, which is how OAuth writes a scope list on the wire.
            Parameter("scope", string.Join(' ', scope));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Escapes a value for a quoted-string.
    /// </summary>
    /// <remarks>
    /// A backslash and a double quote are the two characters that would otherwise end the parameter
    /// early, which turns a value carrying one into a header a client parses as something else. Grant
    /// names reach this from a specification and a realm reaches it from configuration, so neither is
    /// attacker-controlled today - but a header built by concatenation is exactly the thing that
    /// stops being true quietly.
    /// </remarks>
    private static string Quote(string value) =>
        value.Contains('\\') || value.Contains('"')
            ? value.Replace("\\", "\\\\").Replace("\"", "\\\"")
            : value;
}
