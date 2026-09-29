namespace Hardened.Requests.Abstract.Paging;

/// <summary>
/// One page of a list, and the token that fetches the page after it.
/// </summary>
/// <remarks>
/// <para>
/// The published document names the schema for the item type: <c>Page&lt;StaffMember&gt;</c> is
/// <c>PageOfStaffMember</c>, with <c>items</c> and <c>nextPageToken</c>. The token is null on the
/// last page.
/// </para>
/// <para>
/// A Native AOT application declares each closed type it returns in its
/// <c>JsonSerializerContext</c>, such as <c>[JsonSerializable(typeof(Page&lt;StaffMember&gt;))]</c>.
/// </para>
/// </remarks>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextPageToken);

/// <summary>
/// Builds a <see cref="Page{T}"/> from a query that read one row more than the page holds.
/// </summary>
public static class Page
{
    /// <summary>
    /// The first <paramref name="pageSize"/> of <paramref name="rows"/>, with a token when
    /// <paramref name="rows"/> holds more than that.
    /// </summary>
    /// <param name="rows">Up to <paramref name="pageSize"/> + 1 rows, in page order.</param>
    /// <param name="pageSize">The most items the page holds.</param>
    /// <param name="nextPageToken">
    /// The token for the page that follows a row. It is called with the last row on this page,
    /// usually to <see cref="IPageTokens.Encode{TCursor}"/> a cursor built from that row.
    /// </param>
    /// <remarks>
    /// The extra row says that a next page exists. It is not returned. Rows that fit the page get
    /// no token, so a client that reaches a full last page does not request an empty one.
    /// </remarks>
    public static Page<T> From<T>(
        IReadOnlyList<T> rows,
        int pageSize,
        Func<T, string> nextPageToken
    )
    {
        if (pageSize < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                pageSize,
                "A page holds at least one item."
            );
        }

        if (rows.Count <= pageSize)
        {
            return new Page<T>(rows, null);
        }

        var items = new T[pageSize];

        for (var index = 0; index < pageSize; index++)
        {
            items[index] = rows[index];
        }

        return new Page<T>(items, nextPageToken(items[pageSize - 1]));
    }
}
