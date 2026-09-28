using System.Runtime.CompilerServices;

namespace Hardened.Requests.Abstract.Paging;

/// <summary>
/// Writes a cursor as the opaque token a client sends back for the next page, and reads it back.
///
/// <code>
/// [Get("/staff")]
/// public async Task&lt;Page&lt;StaffMember&gt;&gt; List(
///     IStaffStore store,
///     IPageTokens pageTokens,
///     [FromQueryString] string? pageToken,
///     [FromQueryString] [Range(Min = 1, Max = 100)] int pageSize = 20)
/// {
///     var after = pageTokens.Decode&lt;StaffCursor&gt;(pageToken);
///     var rows = await store.NewestFirst(after, pageSize + 1);
///
///     return Page.From(rows, pageSize, last =&gt; pageTokens.Encode(new StaffCursor(last.CreatedAt, last.Id)));
/// }
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// A token is the cursor written as JSON by the application's <c>IJsonSerializer</c> and encoded
/// as base64url. A Native AOT application declares the cursor type in its
/// <c>JsonSerializerContext</c>.
/// </para>
/// <para>
/// When <c>HARDENED_PAGE_TOKEN_KEY</c> is set, a token also carries an HMAC-SHA256 of its JSON
/// under that key, and a token whose HMAC does not match is refused. Without the key a client can
/// decode a token and send back an edited one.
/// </para>
/// <para>
/// Registered as a singleton. A code-first handler takes it as a parameter. A handler that
/// implements a generated interface takes it through its constructor.
/// </para>
/// </remarks>
public interface IPageTokens
{
    /// <summary>The token that carries <paramref name="cursor"/>.</summary>
    string Encode<TCursor>(TCursor cursor);

    /// <summary>
    /// The cursor <paramref name="pageToken"/> carries, or <c>default</c> when the request sent no
    /// token.
    /// </summary>
    /// <param name="pageToken">The token as the request carried it. Null or empty is no token.</param>
    /// <param name="field">
    /// The field a refusal names. The compiler passes the argument's expression, so
    /// <c>Decode&lt;StaffCursor&gt;(pageToken)</c> names <c>pageToken</c>.
    /// </param>
    /// <remarks>
    /// A token that is not base64url, fails its HMAC, or does not read as
    /// <typeparamref name="TCursor"/> throws <c>ValidationException</c>. The request is answered
    /// 400 with a field error naming <paramref name="field"/> and the code <c>invalid</c>.
    /// </remarks>
    TCursor? Decode<TCursor>(
        string? pageToken,
        [CallerArgumentExpression(nameof(pageToken))] string field = ""
    );
}
