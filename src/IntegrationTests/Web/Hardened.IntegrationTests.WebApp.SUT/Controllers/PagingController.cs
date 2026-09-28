using Hardened.IntegrationTests.WebApp.SUT.Models;
using Hardened.Requests.Abstract.Paging;
using Hardened.Web.Runtime.Attributes;
using ValidationModules.Constraints;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// A keyset-paged list, written the way the paging guide writes one.
/// </summary>
/// <remarks>
/// Two members share a creation time, so a page boundary between them is decided by the id, which
/// is the half of the cursor a timestamp-only cursor would lose.
/// </remarks>
[BasePath("/paging")]
public class PagingController
{
    private static readonly DateTimeOffset Day = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly StaffMember[] Staff =
    [
        new(5, "Esme", Day.AddDays(4)),
        new(4, "Dara", Day.AddDays(3)),
        new(3, "Cato", Day.AddDays(3)),
        new(2, "Bea", Day.AddDays(1)),
        new(1, "Ade", Day),
    ];

    /// <summary>Lists staff newest first, a page at a time.</summary>
    [Get("/staff")]
    public Page<StaffMember> ListStaff(
        IPageTokens pageTokens,
        [FromQueryString] string? pageToken,
        [FromQueryString] [Range(Min = 1, Max = 100)] int pageSize = 2
    )
    {
        var after = pageTokens.Decode<StaffCursor>(pageToken);

        var rows = Staff
            .Where(member =>
                after == null
                || member.CreatedAt < after.CreatedAt
                || (member.CreatedAt == after.CreatedAt && member.Id < after.Id)
            )
            .Take(pageSize + 1)
            .ToList();

        return Page.From(
            rows,
            pageSize,
            last => pageTokens.Encode(new StaffCursor(last.CreatedAt, last.Id))
        );
    }
}
