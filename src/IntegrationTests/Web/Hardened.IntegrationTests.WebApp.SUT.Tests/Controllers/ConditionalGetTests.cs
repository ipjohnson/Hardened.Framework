using Hardened.IntegrationTests.WebApp.SUT.Controllers;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Testing;
using Microsoft.Extensions.Primitives;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// Conditional GET through the real pipeline: the validator a cached read carries, the 304 a
/// client holding it is answered, and the same for a handler that writes its own.
/// </summary>
/// <remarks>
/// Nothing here declares anything for it. The filter is installed on every GET by the web module,
/// which is what these prove - a built application, with the generated handler metadata and the
/// module's registrations, rather than a filter driven by hand.
/// </remarks>
public class ConditionalGetTests {

    private const string Catalog = "/response-cache/catalog?culture=en-GB";

    private const string Document = "/conditional/document";

    private static Action<TestWebRequest> IfNoneMatch(string tag) =>
        request => request.Headers[KnownHeaders.IfNoneMatch] = new StringValues(tag);

    private static Action<TestWebRequest> IfModifiedSince(DateTimeOffset when) =>
        request => request.Headers[KnownHeaders.IfModifiedSince] = new StringValues(HttpDate.Format(when));

    private static Action<TestWebRequest> Both(string tag, DateTimeOffset when) =>
        request => {
            IfNoneMatch(tag)(request);
            IfModifiedSince(when)(request);
        };

    private static string Tag(TestWebResponse response) =>
        response.Headers[KnownHeaders.ETag].ToString();

    private static Action<TestWebRequest> Grants(string value) =>
        request => request.Headers[TestGrantsPrincipalSource.GrantsHeader] = new StringValues(value);

    private static void AssertNotModified(TestWebResponse response) {
        Assert.Equal(304, response.StatusCode);
        Assert.Equal(0, response.Body.Length);
        Assert.False(response.Headers.ContainsKey(KnownHeaders.ContentType));
        Assert.False(response.Headers.ContainsKey(KnownHeaders.ContentEncoding));
    }

    // ---------------------------------------------------------------- the cache

    /// <summary>
    /// Weak, because the test client accepts gzip and the compression filter weakens the strong
    /// tag the cache wrote as it encodes. A hit carries the same one.
    /// </summary>
    [HardenedTest]
    public async Task ACachedReadCarriesAValidatorOnTheMissAndTheHit(ITestWebApp testWebApp) {
        var miss = await testWebApp.Get(Catalog);
        var hit = await testWebApp.Get(Catalog);

        Assert.Matches("^W/\"[A-Za-z0-9+/=]+\"$", Tag(miss));
        Assert.Equal(Tag(miss), Tag(hit));
    }

    /// <summary>
    /// The comparison the report asked for: <c>[OutputCache]</c> answers a revalidating client
    /// from the entry, and so does this - without the body, and without the handler.
    /// </summary>
    [HardenedTest]
    public async Task AClientHoldingTheTagIsAnswered304FromTheCache(ITestWebApp testWebApp) {
        var miss = await testWebApp.Get(Catalog);

        var revalidated = await testWebApp.Get(Catalog, IfNoneMatch(Tag(miss)));

        AssertNotModified(revalidated);
        Assert.Equal(Tag(miss), Tag(revalidated));

        // The entry is intact and the handler did not run: the counter in the body still reads 1.
        var again = await testWebApp.Get(Catalog);

        Assert.Equal("en-GB-1", again.Deserialize<string>());
    }

    [HardenedTest]
    public async Task AClientHoldingAStaleTagIsAnsweredInFull(ITestWebApp testWebApp) {
        await testWebApp.Get(Catalog);

        var response = await testWebApp.Get(Catalog, IfNoneMatch("\"stale\""));

        response.Assert.Ok();

        Assert.Equal("en-GB-1", response.Deserialize<string>());
    }

    /// <summary>
    /// A HEAD is revalidated like the GET it stands for, and a 304 reports no length: the count
    /// would be of bytes the filter discarded, not of the body a 200 carries.
    /// </summary>
    [HardenedTest]
    public async Task AHeadHoldingTheTagIs304WithoutALength(ITestWebApp testWebApp) {
        var miss = await testWebApp.Get(Catalog);

        var head = await testWebApp.Request("HEAD", null, Catalog, IfNoneMatch(Tag(miss)));

        AssertNotModified(head);
        Assert.False(head.Headers.ContainsKey(KnownHeaders.ContentLength));
    }

    // ---------------------------------------------------------------- a handler's own validator

    [HardenedTest]
    public async Task AHandlerThatWritesItsOwnTagIsRevalidatedAgainstIt(ITestWebApp testWebApp) {
        var first = await testWebApp.Get(Document);

        first.Assert.Ok();

        Assert.Equal("W/" + ConditionalController.Version, Tag(first));

        var revalidated = await testWebApp.Get(Document, IfNoneMatch(ConditionalController.Version));

        AssertNotModified(revalidated);
        Assert.Equal(HttpDate.Format(ConditionalController.UpdatedAt), revalidated.Headers[KnownHeaders.LastModified].ToString());
    }

    [HardenedTest]
    public async Task AHandlerThatWritesLastModifiedIsRevalidatedAgainstTheDate(ITestWebApp testWebApp) {
        var unchanged = await testWebApp.Get(Document, IfModifiedSince(ConditionalController.UpdatedAt));
        var changed = await testWebApp.Get(Document, IfModifiedSince(ConditionalController.UpdatedAt.AddDays(-1)));

        AssertNotModified(unchanged);
        changed.Assert.Ok();
    }

    /// <summary>
    /// RFC 9110 §13.2.1: the validator outranks the date, including when it does not match.
    /// </summary>
    [HardenedTest]
    public async Task AStaleTagOutranksASatisfiedDate(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(Document, Both("\"stale\"", ConditionalController.UpdatedAt));

        response.Assert.Ok();
    }

    /// <summary>
    /// The 304 costs the body, not the handler: a handler that wrote its own validator ran to
    /// write it. Skipping the work needs the validator before the handler runs, which is a
    /// different feature.
    /// </summary>
    [HardenedTest]
    public async Task A304FromAHandlersOwnTagStillRanTheHandler(ITestWebApp testWebApp) {
        await testWebApp.Get(Document, IfNoneMatch(ConditionalController.Version));

        var next = await testWebApp.Get(Document);

        Assert.Equal(2, next.Deserialize<ConditionalController.Document>().Served);
    }

    // ---------------------------------------------------------------- what is never a 304

    /// <summary>
    /// Nothing computes a validator for a handler that neither caches nor writes one, so there is
    /// nothing to match and the body is sent whatever the caller claims to hold.
    /// </summary>
    [HardenedTest]
    public async Task AHandlerWithNoValidatorIsSentInFull(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/response-cache/uncached", IfNoneMatch("*"));

        response.Assert.Ok();

        Assert.False(response.Headers.ContainsKey(KnownHeaders.ETag));
        Assert.Equal("1", response.Deserialize<string>());
    }

    /// <summary>
    /// A caller the guard refuses is refused, not told the resource has not changed. The refusal
    /// is recorded ahead of the conditional stage and written behind it, and the stage reads what
    /// was recorded rather than the tag.
    /// </summary>
    [HardenedTest]
    public async Task ARefusedCallerHoldingTheTagIsStillRefused(ITestWebApp testWebApp) {
        var warm = await testWebApp.Get("/response-cache/granted", Grants("pets:read"));

        warm.Assert.Ok();

        var grantless = await testWebApp.Get("/response-cache/granted", IfNoneMatch(Tag(warm)));

        Assert.True(grantless.StatusCode is 401 or 403,
            $"a grantless caller holding the tag was answered {grantless.StatusCode}");
    }
}
