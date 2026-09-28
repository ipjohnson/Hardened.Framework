using System.Text;
using Hardened.Requests.Abstract.Paging;
using Hardened.Requests.Runtime.Paging;
using Hardened.Requests.Runtime.Validation;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// A keyset-paged list through the real pipeline: <c>Page&lt;T&gt;</c> on the wire, a token taken
/// from one response and sent back on the next request, and a bad token refused as a validation
/// error.
/// </summary>
public class PagingTests
{
    private const string Key = "integration-page-key";

    /// <summary>
    /// The cursor the first two-member page ends on, written without a key: member 4, created on
    /// 2026-09-04.
    /// </summary>
    private static readonly string UnsignedToken = Convert
        .ToBase64String(
            Encoding.UTF8.GetBytes("{\"createdAt\":\"2026-09-04T00:00:00+00:00\",\"id\":4}")
        )
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static Page<StaffMember> Read(TestWebResponse response)
    {
        response.Assert.Ok();

        var page = response.Deserialize<Page<StaffMember>>();

        Assert.NotNull(page);

        return page;
    }

    private static async Task<List<int>> EveryId(ITestWebApp app)
    {
        var ids = new List<int>();
        string? token = null;

        for (var request = 0; request < 10; request++)
        {
            var query = token == null ? "" : "&pageToken=" + Uri.EscapeDataString(token);
            var page = Read(await app.Get("/paging/staff?pageSize=2" + query));

            ids.AddRange(page.Items.Select(member => member.Id));
            token = page.NextPageToken;

            if (token == null)
            {
                break;
            }
        }

        return ids;
    }

    [ModuleTest]
    public async Task TheFirstPageHoldsThePageSizeAndATokenForTheNext(ITestWebApp app)
    {
        var page = Read(await app.Get("/paging/staff?pageSize=2"));

        Assert.Equal(new[] { 5, 4 }, page.Items.Select(member => member.Id));
        Assert.NotNull(page.NextPageToken);
    }

    /// <summary>
    /// Members 4 and 3 share a creation time and the first page ends between them, so the second
    /// page starts at 3 only because the cursor carries the id as well.
    /// </summary>
    [ModuleTest]
    public async Task FollowingTheTokensVisitsEveryMemberOnceInOrder(ITestWebApp app)
    {
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, await EveryId(app));
    }

    [ModuleTest]
    public async Task AFullLastPageCarriesNoToken(ITestWebApp app)
    {
        var page = Read(await app.Get("/paging/staff?pageSize=5"));

        Assert.Equal(5, page.Items.Count);
        Assert.Null(page.NextPageToken);
    }

    [ModuleTest]
    public async Task ATokenThatDoesNotDecodeIsRefusedNamingIt(ITestWebApp app)
    {
        var response = await app.Get("/paging/staff?pageToken=not.a.token");

        response.Assert.BadRequest();

        var error = Assert.Single(response.Deserialize<RequestValidationError>()!.Errors);

        Assert.Equal("pageToken", error.Field);
        Assert.Equal("invalid", error.Code);
    }

    [ModuleTest]
    public async Task WithoutAKeyAnUnsignedTokenIsAccepted(ITestWebApp app)
    {
        var page = Read(await app.Get("/paging/staff?pageSize=2&pageToken=" + UnsignedToken));

        Assert.Equal(new[] { 3, 2 }, page.Items.Select(member => member.Id));
    }

    [ModuleTest]
    [EnvironmentValue(PageTokenConfiguration.EnvironmentVariable, Key)]
    public async Task WithAKeyTheSignedTokensPageThroughEveryMember(ITestWebApp app)
    {
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, await EveryId(app));
    }

    /// <summary>The same token the test above accepts, refused once the key is set.</summary>
    [ModuleTest]
    [EnvironmentValue(PageTokenConfiguration.EnvironmentVariable, Key)]
    public async Task WithAKeyAnUnsignedTokenIsRefused(ITestWebApp app)
    {
        var response = await app.Get("/paging/staff?pageSize=2&pageToken=" + UnsignedToken);

        response.Assert.BadRequest();

        Assert.Equal(
            "pageToken",
            Assert.Single(response.Deserialize<RequestValidationError>()!.Errors).Field
        );
    }
}
