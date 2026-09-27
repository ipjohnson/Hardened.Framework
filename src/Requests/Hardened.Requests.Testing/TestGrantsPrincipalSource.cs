using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Testing;

/// <summary>
/// Authenticates a test request from an <c>X-Test-Grants</c> header.
/// </summary>
/// <remarks>
/// <para>
/// The supported spelling of the middleware every application test suite was writing for itself -
/// three arms of the last trial wrote it independently, and both of this repository's own
/// authorization fixtures carried a copy. Register it as an <see cref="IPrincipalSource"/> and a
/// test states a caller by setting the header:
/// </para>
/// <code>
/// services.AddSingleton&lt;IPrincipalSource, TestGrantsPrincipalSource&gt;();
///
/// request.Headers[TestGrantsPrincipalSource.GrantsHeader] = "pets:read pets:write";
/// </code>
/// <para>
/// <see cref="SubjectHeader"/> names which caller, for a test where one caller's data reaching
/// another is the thing being asserted. <see cref="ClaimsHeader"/> carries claims, for a policy that
/// reads one.
/// </para>
/// <para>
/// A request without the header stays anonymous, which is what an authorization test wants both
/// halves of: the refusal for the caller who presented nothing, and the answer for the caller
/// holding exactly the named grants. <see cref="AnonymousGrantsValue"/> is the third state - a
/// caller who is authenticated and holds nothing.
/// </para>
/// </remarks>
public sealed class TestGrantsPrincipalSource : IPrincipalSource
{
    public const string GrantsHeader = "X-Test-Grants";

    /// <summary>
    /// Which caller, for a test where two of them is the point.
    /// </summary>
    /// <remarks>
    /// Optional, and <see cref="DefaultSubject"/> without it, so a test that only cares about
    /// grants states only grants. It exists because the grants header cannot distinguish one caller
    /// from another and an ownership test is exactly a test that has to: every caller had the same
    /// subject, so "does one subscriber get another's row" was not a question this could ask.
    /// </remarks>
    public const string SubjectHeader = "X-Test-Subject";

    /// <summary>
    /// The claims the caller carries, for a policy that reads one through
    /// <see cref="ICallerPrincipal.TryGetClaim"/>: a tenant, an organisation.
    /// </summary>
    /// <remarks>
    /// Written as a query string is, <c>tenant=acme&amp;region=eu-west</c>, with each name and value
    /// percent-encoded so a value may hold any character. <see cref="FormatClaims"/> writes it. Read
    /// only beside <see cref="GrantsHeader"/>, as the subject is.
    /// </remarks>
    public const string ClaimsHeader = "X-Test-Claims";

    /// <summary>The subject a request that names none is authenticated as.</summary>
    public const string DefaultSubject = "integration-test";

    /// <summary>Authenticates with no grants at all, for the caller who is merely known.</summary>
    public const string AnonymousGrantsValue = "-";

    /// <summary>The scheme name the produced principal carries.</summary>
    public const string SchemeName = "test";

    public ValueTask<ICallerPrincipal?> Authenticate(IExecutionContext context)
    {
        if (!context.Request.Headers.TryGetValue(GrantsHeader, out var header))
        {
            return new ValueTask<ICallerPrincipal?>((ICallerPrincipal?)null);
        }

        var value = header.ToString();

        var subject = context.Request.Headers.TryGetValue(SubjectHeader, out var named)
            ? named.ToString()
            : DefaultSubject;

        var claims = context.Request.Headers.TryGetValue(ClaimsHeader, out var carried)
            ? ParseClaims(carried)
            : null;

        return new ValueTask<ICallerPrincipal?>(
            new CallerPrincipal(
                SchemeName,
                value == AnonymousGrantsValue
                    ? []
                    : value.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                subject: subject,
                claims: claims
            )
        );
    }

    /// <summary>The <see cref="ClaimsHeader"/> value for <paramref name="claims"/>.</summary>
    public static string FormatClaims(IEnumerable<KeyValuePair<string, string>> claims) =>
        string.Join(
            "&",
            claims.Select(claim =>
                Uri.EscapeDataString(claim.Key) + "=" + Uri.EscapeDataString(claim.Value)
            )
        );

    /// <summary>
    /// Every <c>name=value</c> pair in every value of the header. A pair with no <c>=</c> is a claim
    /// with an empty value.
    /// </summary>
    private static List<KeyValuePair<string, string>> ParseClaims(IEnumerable<string?> values)
    {
        var claims = new List<KeyValuePair<string, string>>();

        foreach (var value in values)
        {
            foreach (var pair in (value ?? "").Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = pair.IndexOf('=');
                var name = separator < 0 ? pair : pair.Substring(0, separator);
                var claim = separator < 0 ? "" : pair.Substring(separator + 1);

                claims.Add(
                    new KeyValuePair<string, string>(
                        Uri.UnescapeDataString(name),
                        Uri.UnescapeDataString(claim)
                    )
                );
            }
        }

        return claims;
    }
}
