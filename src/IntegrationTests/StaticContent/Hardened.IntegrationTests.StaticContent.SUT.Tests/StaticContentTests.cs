using System.IO.Compression;
using System.Text;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.IntegrationTests.StaticContent.SUT.Tests;

/// <summary>
/// Static content driven through the real pipeline.
///
/// <para>
/// Unit tests cover the source, the writer and the mount in isolation, against substituted
/// contexts they construct themselves. What these confirm is the wiring nothing else touches: that
/// the package's module registers a mount at all, that the mount is consulted after routing rather
/// than before it, that a request reaching it runs a handler chain, and that the headers a client
/// actually reads come off a response the pipeline produced.
/// </para>
///
/// <para>
/// The absence of exactly this is what let the original defects ship. A test that hands the
/// handler an ETag it computed itself cannot notice that no ETag was ever sent; a test that passes
/// <c>Accept-Encoding: gzip</c> cannot notice that no browser sends it that way. Both are asserted
/// here against what the server put on the wire.
/// </para>
/// </summary>
public class StaticContentTests {

    private static string Header(TestWebResponse response, string name) =>
        response.Headers.TryGetValue(name, out var value) ? value.ToString() : "";

    private static Action<TestWebRequest> With(params (string Name, string Value)[] headers) =>
        request => {
            foreach (var (name, value) in headers) {
                request.Headers[name] = value;
            }
        };

    private static async Task<byte[]> BytesOf(TestWebResponse response) {
        response.Body.Position = 0;

        using var buffer = new MemoryStream();

        await response.Body.CopyToAsync(buffer);

        return buffer.ToArray();
    }

    #region serving at all

    [HardenedTest]
    public async Task AFileIsServed(ITestWebApp app) {
        var response = await app.Get("/index.html");

        response.Assert.Ok();
        Assert.Contains("application shell", await response.ReadTextAsync());
    }

    /// <summary>
    /// A directory answers with its index, for both spellings. Without it the only way to serve
    /// anything at the site root was the single-page fall back, which then answered every unknown
    /// path too - so a plain static site was not expressible.
    /// </summary>
    [HardenedTest]
    [InlineData("/")]
    [InlineData("/assets")]
    [InlineData("/assets/")]
    public async Task ADirectoryAnswersWithItsIndex(string path, ITestWebApp app) {
        var response = await app.Get(path);

        response.Assert.Ok();
        Assert.Contains("<title>", await response.ReadTextAsync());
    }

    /// <summary>
    /// An unknown path serves the application shell. Every one of them shares the single entry the
    /// source read, which is the shape that used to grow the cache without bound.
    /// </summary>
    [HardenedTest]
    [InlineData("/app/deep/route")]
    [InlineData("/some-other-spa-path")]
    public async Task AnUnknownPathServesTheShell(string path, ITestWebApp app) {
        var response = await app.Get(path);

        response.Assert.Ok();
        Assert.Contains("application shell", await response.ReadTextAsync());
    }

    /// <summary>
    /// <b>The mount is consulted after routing, not before.</b> There is a <c>wwwroot/app.js</c>
    /// and a route declared at <c>/app.js</c>, and the route has to win - which is what
    /// <c>IFallbackRequestHandlerProvider</c> exists to guarantee independently of the order the
    /// application listed its modules in.
    /// </summary>
    [HardenedTest]
    public async Task ADeclaredRouteWinsOverAFileAtTheSamePath(ITestWebApp app) {
        var response = await app.Get("/app.js");

        response.Assert.Ok();

        var body = await response.ReadTextAsync();

        Assert.Contains("declared by a route", body);
        Assert.DoesNotContain("hello", body);
    }

    #endregion

    #region the validator, and what carries it

    /// <summary>
    /// A served file carries an ETag. Without the header there is nothing for a client to put in
    /// <c>If-None-Match</c>, so the conditional path was unreachable from a browser however correct
    /// the comparison was - and no unit test that supplies its own tag can see that.
    /// </summary>
    [HardenedTest]
    public async Task AServedFileCarriesAQuotedETag(ITestWebApp app) {
        var etag = Header(await app.Get("/index.html"), KnownHeaders.ETag);

        Assert.StartsWith("\"", etag);
        Assert.EndsWith("\"", etag);
        Assert.True(etag.Length > 2, "the ETag was an empty pair of quotes");
    }

    /// <summary>
    /// The round trip a browser actually performs: take the validator the server sent, send it
    /// back, get a 304 with no body. Nothing in the unit tests can prove this, because they supply
    /// the tag themselves.
    /// </summary>
    [HardenedTest]
    public async Task TheValidatorTheServerSentComesBackAsA304(ITestWebApp app) {
        var first = await app.Get("/index.html");
        var etag = Header(first, KnownHeaders.ETag);

        var second = await app.Get(
            "/index.html", With((KnownHeaders.IfNoneMatch, etag)));

        Assert.Equal(304, second.StatusCode);
        Assert.Empty(await BytesOf(second));
    }

    /// <summary>
    /// And the 304 repeats the freshness the 200 carried. Dropping it meant an asset was
    /// revalidated on every request after the first, whatever its max age said.
    /// </summary>
    [HardenedTest]
    public async Task A304RepeatsTheCacheHeaders(ITestWebApp app) {
        var first = await app.Get("/index.html");
        var etag = Header(first, KnownHeaders.ETag);

        var second = await app.Get("/index.html", With((KnownHeaders.IfNoneMatch, etag)));

        Assert.Equal(304, second.StatusCode);
        Assert.Equal(etag, Header(second, KnownHeaders.ETag));
        Assert.Equal(
            Header(first, KnownHeaders.CacheControl), Header(second, KnownHeaders.CacheControl));
    }

    /// <summary>
    /// The date validator, and the same round trip through it. It did not exist at all, so a cache
    /// with no entity-tag had nothing to revalidate against.
    /// </summary>
    [HardenedTest]
    public async Task TheLastModifiedTheServerSentComesBackAsA304(ITestWebApp app) {
        var lastModified = Header(await app.Get("/index.html"), KnownHeaders.LastModified);

        Assert.EndsWith("GMT", lastModified);

        var second = await app.Get(
            "/index.html", With((KnownHeaders.IfModifiedSince, lastModified)));

        Assert.Equal(304, second.StatusCode);
    }

    /// <summary>
    /// The directives the application configured reach the wire. Only <c>max-age</c> could be
    /// expressed before, so <c>private</c> - which is what an authorized mount needs, since
    /// <c>public</c> invites a shared cache to keep it - was unreachable.
    /// </summary>
    [HardenedTest]
    public async Task TheConfiguredCacheControlReachesTheWire(ITestWebApp app) {
        var response = await app.Get("/index.html");

        Assert.Equal("private, max-age=3600", Header(response, KnownHeaders.CacheControl));
    }

    #endregion

    #region content negotiation

    /// <summary>
    /// <b>The header a browser actually sends.</b> It lists four codings in one value, and the
    /// match used to ask whether the header <em>equalled</em> the coding - so no browser ever
    /// received a pre-compressed asset, and every one took the inflate-per-request path instead.
    /// </summary>
    [HardenedTest]
    public async Task ABrowserGetsThePreCompressedAsset(ITestWebApp app) {
        var response = await app.Get(
            "/vendor.js", With((KnownHeaders.AcceptEncoding, "gzip, deflate, br, zstd")));

        response.Assert.Ok();

        Assert.Equal(KnownEncoding.GZip, Header(response, KnownHeaders.ContentEncoding));
        Assert.Equal(KnownHeaders.AcceptEncoding, Header(response, KnownHeaders.Vary));

        using var compressed = new MemoryStream(await BytesOf(response));
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        await gzip.CopyToAsync(inflated);

        Assert.Contains("vendor", Encoding.UTF8.GetString(inflated.ToArray()));
    }

    /// <summary>
    /// And a client that does not take the stored coding is served the resource inflated, not bytes
    /// it cannot read. This is the branch that makes shipping a pre-compressed asset safe.
    ///
    /// <para>
    /// The coding is stated rather than omitted: the harness supplies <c>gzip</c> when a test sets
    /// none, so a request that says nothing is not the request this is about.
    /// </para>
    /// </summary>
    [HardenedTest]
    [InlineData("identity")]
    [InlineData("br")]
    public async Task AClientThatDoesNotTakeTheStoredCodingGetsItInflated(
        string acceptEncoding, ITestWebApp app) {
        var response = await app.Get(
            "/vendor.js", With((KnownHeaders.AcceptEncoding, acceptEncoding)));

        response.Assert.Ok();

        Assert.Equal("", Header(response, KnownHeaders.ContentEncoding));
        Assert.Contains("vendor", await response.ReadTextAsync());
    }

    /// <summary>
    /// A resource served one way does not vary, and says so by omission. Declaring it anyway has a
    /// CDN store a copy per coding of a file that is byte-identical for all of them.
    /// </summary>
    [HardenedTest]
    public async Task AnUncompressedResourceDoesNotSayItVaries(ITestWebApp app) {
        var response = await app.Get(
            "/clip.bin", With((KnownHeaders.AcceptEncoding, "gzip, deflate, br")));

        response.Assert.Ok();
        Assert.Equal("", Header(response, KnownHeaders.Vary));
    }

    #endregion

    #region ranges

    [HardenedTest]
    public async Task AServedFileAdvertisesThatRangesWork(ITestWebApp app) {
        Assert.Equal("bytes", Header(await app.Get("/clip.bin"), KnownHeaders.AcceptRanges));
    }

    /// <summary>
    /// The request a media element makes before it will play anything, and a download manager makes
    /// to resume. Neither worked at all: there was no <c>Accept-Ranges</c>, so a client had to
    /// assume seeking was unavailable.
    /// </summary>
    [HardenedTest]
    public async Task ARangeIsAnsweredWith206AndOnlyThoseBytes(ITestWebApp app) {
        var response = await app.Get("/clip.bin", With((KnownHeaders.Range, "bytes=0-9")));

        Assert.Equal(206, response.StatusCode);
        Assert.Equal("bytes 0-9/100", Header(response, KnownHeaders.ContentRange));
        Assert.Equal("0123456789", Encoding.UTF8.GetString(await BytesOf(response)));
    }

    [HardenedTest]
    public async Task ARangePastTheEndIs416WithTheLength(ITestWebApp app) {
        var response = await app.Get("/clip.bin", With((KnownHeaders.Range, "bytes=500-600")));

        Assert.Equal(416, response.StatusCode);
        Assert.Equal("bytes */100", Header(response, KnownHeaders.ContentRange));
    }

    #endregion

    #region verbs

    /// <summary>
    /// A HEAD produces the headers its GET would and no body. Static content never reached
    /// <c>Dispatch</c>, which is what drops the body, because it was not a handler.
    /// </summary>
    [HardenedTest]
    public async Task AHeadCarriesTheHeadersOfItsGetAndNoBody(ITestWebApp app) {
        var get = await app.Get("/index.html");
        var head = await app.Request("HEAD", null, "/index.html");

        Assert.Equal(200, head.StatusCode);
        Assert.Empty(await BytesOf(head));

        Assert.Equal(Header(get, KnownHeaders.ETag), Header(head, KnownHeaders.ETag));
        Assert.Equal(
            Header(get, KnownHeaders.CacheControl), Header(head, KnownHeaders.CacheControl));
    }

    /// <summary>
    /// A verb a file does not answer is a 405 naming what it does, not a 404: the resource is
    /// there and the verb is the problem, which a client and a CDN both read differently.
    /// </summary>
    [HardenedTest]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task AWriteToAFileIsMethodNotAllowed(string method, ITestWebApp app) {
        var response = await app.Request(method, null, "/index.html");

        Assert.Equal(405, response.StatusCode);
        Assert.Equal("GET, HEAD", Header(response, KnownHeaders.Allow));
    }

    /// <summary>
    /// But a write to a path that only exists because the single-page fall back catches everything
    /// is a 404. Answering 405 there would tell a client that <c>POST /api/typo</c> reached
    /// something.
    /// </summary>
    [HardenedTest]
    public async Task AWriteToAPathOnlyTheFallbackAnswersIsNotFound(ITestWebApp app) {
        var response = await app.Request("POST", null, "/api/typo");

        response.Assert.NotFound();
    }

    #endregion

    #region what is not served

    /// <summary>
    /// A hidden file is refused. The application has one in its content directory on purpose,
    /// because the common case is a build step that copied a directory wholesale and nobody looked.
    /// </summary>
    [HardenedTest]
    public async Task AHiddenFileIsNotServed(ITestWebApp app) {
        var response = await app.Get("/.env");

        // The fall back answers instead, which is what an unknown path does here - the point is
        // that the secret is not in the body.
        Assert.DoesNotContain("must-never-be-served", await response.ReadTextAsync());
    }

    /// <summary>
    /// <c>.well-known</c> is the exception, and not a small one: ACME challenges live under it, so
    /// refusing every hidden path without it breaks certificate renewal.
    /// </summary>
    [HardenedTest]
    public async Task WellKnownIsServedDespiteBeingHidden(ITestWebApp app) {
        var response = await app.Get("/.well-known/security.txt");

        response.Assert.Ok();
        Assert.Contains("security@example.com", await response.ReadTextAsync());
    }

    /// <summary>
    /// A traversal does not escape the content root. The transport under test does not normalise
    /// the path, which is the position a source is left in on API Gateway.
    /// </summary>
    [HardenedTest]
    [InlineData("/../Application.cs")]
    [InlineData("/assets/../../Program.cs")]
    public async Task ATraversalDoesNotEscapeTheRoot(string path, ITestWebApp app) {
        var body = await (await app.Get(path)).ReadTextAsync();

        Assert.DoesNotContain("namespace Hardened.IntegrationTests", body);
        Assert.DoesNotContain("UseHardened", body);
    }

    #endregion
}
