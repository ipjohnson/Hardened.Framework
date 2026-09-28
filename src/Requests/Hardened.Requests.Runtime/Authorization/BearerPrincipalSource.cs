using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Reads a bearer token from the <c>Authorization</c> header and hands it to a delegate that
/// validates it.
/// </summary>
/// <remarks>
/// <para>
/// The delegate owns validation: it may parse a JWT, introspect the token with its issuer, or look
/// it up in a table. It returns the principal the token establishes, or null for a token it refuses.
/// A refused token becomes <see cref="AnonymousCallerPrincipal.Rejected"/>, so the request continues
/// anonymously and a requirement answers it 401 with <c>error="invalid_token"</c>. A delegate that
/// can say why returns <c>AnonymousCallerPrincipal.Rejected(description)</c> itself.
/// </para>
/// <para>
/// A request with no <c>Authorization</c> header, another scheme word, or nothing after
/// <c>Bearer</c> carries no bearer token, so this answers null and the next source is asked.
/// </para>
/// </remarks>
/// <typeparam name="TScheme">
/// The scheme <c>[Authorize&lt;TScheme&gt;]</c> names. See <see cref="IPrincipalSource{TScheme}"/>.
/// </typeparam>
public sealed class BearerPrincipalSource<TScheme> : IPrincipalSource<TScheme>
    where TScheme : IAuthenticationScheme
{
    private const string Scheme = "Bearer";

    private readonly Func<string, IExecutionContext, ValueTask<ICallerPrincipal?>> _validate;

    /// <param name="validate">
    /// Validates the token, which arrives without the <c>Bearer</c> word and its space.
    /// </param>
    public BearerPrincipalSource(
        Func<string, IExecutionContext, ValueTask<ICallerPrincipal?>> validate
    )
    {
        _validate = validate;
    }

    public async ValueTask<ICallerPrincipal?> Authenticate(IExecutionContext context)
    {
        if (Token(context) is not { } token)
        {
            return null;
        }

        return await _validate(token, context) ?? AnonymousCallerPrincipal.Rejected();
    }

    /// <remarks>
    /// The scheme word is compared without case, as RFC 9110 §11.1 says of every authentication
    /// scheme.
    /// </remarks>
    private static string? Token(IExecutionContext context)
    {
        if (!context.Request.Headers.TryGetValue(KnownHeaders.Authorization, out var header))
        {
            return null;
        }

        var value = header.ToString().AsSpan().Trim();

        if (
            value.Length <= Scheme.Length
            || !value.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)
            || value[Scheme.Length] != ' '
        )
        {
            return null;
        }

        var token = value.Slice(Scheme.Length).Trim();

        return token.IsEmpty ? null : token.ToString();
    }
}
