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
/// A request without the header stays anonymous, which is what an authorization test wants both
/// halves of: the refusal for the caller who presented nothing, and the answer for the caller
/// holding exactly the named grants. <see cref="AnonymousGrantsValue"/> is the third state - a
/// caller who is authenticated and holds nothing.
/// </para>
/// </remarks>
public sealed class TestGrantsPrincipalSource : IPrincipalSource {
    public const string GrantsHeader = "X-Test-Grants";

    /// <summary>Authenticates with no grants at all, for the caller who is merely known.</summary>
    public const string AnonymousGrantsValue = "-";

    /// <summary>The scheme name the produced principal carries.</summary>
    public const string SchemeName = "test";

    public ValueTask<ICallerPrincipal?> Authenticate(IExecutionContext context) {
        if (!context.Request.Headers.TryGetValue(GrantsHeader, out var header)) {
            return new ValueTask<ICallerPrincipal?>((ICallerPrincipal?)null);
        }

        var value = header.ToString();

        return new ValueTask<ICallerPrincipal?>(new CallerPrincipal(
            SchemeName,
            value == AnonymousGrantsValue
                ? []
                : value.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            subject: "integration-test"));
    }
}
