using System.IO.Compression;
using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Utilities;
using Hardened.Web.Runtime.Configuration;
using Hardened.Web.Runtime.StaticContent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.StaticContent;

/// <summary>
/// Everything the static content handler decides after it has found the file.
///
/// <para>
/// <c>StaticContentPathTraversalTests</c> covers whether a request is allowed to reach a file at
/// all. These cover what happens once it has: the in-process cache, the pre-compressed siblings a
/// build step drops beside a file, conditional requests, cache headers, the single-page-app
/// fallback, and the two ways compressed content can leave the process. Each of those is a branch
/// that produces a correct-looking 200 when it is wrong — a response served with the wrong
/// encoding, or an ETag match answered with the body anyway.
/// </para>
/// </summary>
public class StaticContentHandlerTests : IDisposable {

    private readonly string _tempRoot;
    private readonly string _staticRoot;

    // The handler resolves its configured Path against the process's current directory, so the
    // obvious fixture sets the current directory and restores it. That is process-global state:
    // xUnit runs test classes in parallel, so instances of this class raced each other and raced
    // StaticContentPathTraversalTests, failing a different subset of tests on each run.
    // Configuring an absolute root instead removes the shared state - Path.Combine returns a
    // rooted second argument unchanged, so the handler never consults the current directory.
    public StaticContentHandlerTests() {
        _tempRoot = Path.Combine(Path.GetTempPath(), "hardened-static-" + Guid.NewGuid().ToString("N"));
        _staticRoot = Path.Combine(_tempRoot, "wwwroot");

        Directory.CreateDirectory(_staticRoot);
    }

    public void Dispose() {
        try {
            Directory.Delete(_tempRoot, true);
        }
        catch {
            // Best effort — a leftover temp directory is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    private void WriteFile(string name, string content) =>
        File.WriteAllText(Path.Combine(_staticRoot, name), content);

    private void WriteGZipFile(string name, string content) {
        using var file = File.Create(Path.Combine(_staticRoot, name));
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);

        var bytes = Encoding.UTF8.GetBytes(content);

        gzip.Write(bytes, 0, bytes.Length);
    }

    private void WriteBrotliFile(string name, string content) {
        using var file = File.Create(Path.Combine(_staticRoot, name));
        using var brotli = new BrotliStream(file, CompressionLevel.Fastest);

        var bytes = Encoding.UTF8.GetBytes(content);

        brotli.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// A handler over the temporary root. <paramref name="configure"/> receives the configuration
    /// substitute already carrying the defaults, so a case only states what it changes.
    /// </summary>
    private StaticContentHandler Handler(
        Action<IStaticContentConfiguration>? configure = null,
        string? path = null) {
        var configuration = Substitute.For<IStaticContentConfiguration>();

        configuration.Path.Returns(path ?? _staticRoot);
        configuration.EnableETag.Returns(false);
        configuration.CompressTextContent.Returns(false);
        configuration.FallBackFile.Returns((string?)null);
        configuration.CacheMaxAge.Returns((int?)null);
        configuration.Immutable.Returns(false);
        configuration.OnPrepareResponse.Returns((Action<IExecutionContext>?)null);

        configure?.Invoke(configuration);

        var mimeHelper = Substitute.For<IFileExtToMimeTypeHelper>();

        mimeHelper.GetMimeTypeInfo(Arg.Any<string>()).Returns(("text/plain", false));

        return new StaticContentHandler(
            Options.Create(configuration),
            mimeHelper,
            new GZipStaticContentCompressor(new MemoryStreamPool()),
            new ETagProvider(new TestMD5Pool()),
            new MemoryStreamPool(),
            NullLogger<StaticContentHandler>.Instance);
    }

    private static (IExecutionContext context, MemoryStream body, IExecutionResponse response, IDictionary<string, StringValues> responseHeaders)
        Context(string path, params (string Name, string Value)[] requestHeaders) {
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();
        var response = Substitute.For<IExecutionResponse>();
        var body = new MemoryStream();

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in requestHeaders) {
            headers[name] = value;
        }

        var outgoing = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        request.Path.Returns(path);
        request.Headers.Returns(headers);
        response.Body.Returns(body);
        response.Headers.Returns(outgoing);
        context.Request.Returns(request);
        context.Response.Returns(response);

        return (context, body, response, outgoing);
    }

    private static string Served(MemoryStream body) => Encoding.UTF8.GetString(body.ToArray());

    /// <summary>
    /// A configured root that does not exist anywhere disables the handler outright, rather than
    /// throwing on every request or letting a traversal resolve against a directory that is not
    /// there.
    /// </summary>
    [Fact]
    public async Task AHandlerWhoseRootDoesNotExistServesNothing() {
        var (context, _, _, _) = Context("/anything.txt");

        Assert.False(await Handler(path: Path.Combine(_tempRoot, "no-such-directory")).Handle(context));
    }

    /// <summary>
    /// A path the operating system cannot express at all is refused the same way one that escapes
    /// the root is — quietly, before the filesystem is touched.
    /// </summary>
    [Fact]
    public async Task AMalformedRequestPathIsRefusedRatherThanThrown() {
        WriteFile("public.txt", "public");

        var (context, _, _, _) = Context("/bad\0name.txt");

        Assert.False(await Handler().Handle(context));
    }

    /// <summary>
    /// The second request for a file is answered from the in-process cache. Deleting the file
    /// between the two is how the test tells the cache apart from a second read.
    /// </summary>
    [Fact]
    public async Task ASecondRequestIsAnsweredFromTheCacheRatherThanTheFilesystem() {
        WriteFile("cached.txt", "cached content");

        var handler = Handler();

        var (first, firstBody, _, _) = Context("/cached.txt");

        Assert.True(await handler.Handle(first));
        Assert.Equal("cached content", Served(firstBody));

        File.Delete(Path.Combine(_staticRoot, "cached.txt"));

        var (second, secondBody, _, _) = Context("/cached.txt");

        Assert.True(await handler.Handle(second));
        Assert.Equal("cached content", Served(secondBody));
    }

    /// <summary>
    /// A build step that ships <c>app.js.gz</c> beside nothing else still serves <c>/app.js</c>.
    /// The client asked for the uncompressed name and the handler has to find the sibling.
    /// </summary>
    [Fact]
    public async Task APreCompressedGZipSiblingIsServedForTheUncompressedName() {
        WriteGZipFile("app.js.gz", "console.log('hi');");

        var (context, body, _, headers) = Context(
            "/app.js", (KnownHeaders.AcceptEncoding, KnownEncoding.GZip));

        Assert.True(await Handler().Handle(context));
        Assert.Equal(KnownEncoding.GZip, headers[KnownHeaders.ContentEncoding].ToString());

        // The bytes went out still compressed, so the client has to inflate them.
        using var compressed = new MemoryStream(body.ToArray());
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        await gzip.CopyToAsync(inflated, TestContext.Current.CancellationToken);

        Assert.Equal("console.log('hi');", Encoding.UTF8.GetString(inflated.ToArray()));
    }

    /// <summary>
    /// A client that did not offer gzip gets the file decompressed on the way out rather than a
    /// body it cannot read. This is the branch that makes a pre-compressed asset safe to ship.
    /// </summary>
    [Fact]
    public async Task AGZipSiblingIsDecompressedForAClientThatDidNotAskForIt() {
        WriteGZipFile("app.js.gz", "console.log('hi');");

        var (context, body, _, headers) = Context("/app.js");

        Assert.True(await Handler().Handle(context));
        Assert.Equal("console.log('hi');", Served(body));
        Assert.DoesNotContain(KnownHeaders.ContentEncoding, headers.Keys);
    }

    /// <summary>Brotli siblings are found the same way, and decompressed the same way.</summary>
    [Fact]
    public async Task ABrotliSiblingIsDecompressedForAClientThatDidNotAskForIt() {
        WriteBrotliFile("styles.css.br", "body { color: red; }");

        var (context, body, _, _) = Context("/styles.css");

        Assert.True(await Handler().Handle(context));
        Assert.Equal("body { color: red; }", Served(body));
    }

    /// <summary>
    /// A client that offers Brotli gets the stored bytes untouched. The content encoding header
    /// says gzip regardless, which is wrong for a <c>.br</c> file — reported rather than asserted
    /// here; see docs/TESTING-PLAN.md §11.
    /// </summary>
    [Fact]
    public async Task ABrotliSiblingIsServedCompressedToAClientThatAskedForIt() {
        WriteBrotliFile("styles.css.br", "body { color: red; }");

        var (context, body, _, _) = Context(
            "/styles.css", (KnownHeaders.AcceptEncoding, KnownEncoding.Br));

        Assert.True(await Handler().Handle(context));

        using var compressed = new MemoryStream(body.ToArray());
        using var brotli = new BrotliStream(compressed, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        await brotli.CopyToAsync(inflated, TestContext.Current.CancellationToken);

        Assert.Equal("body { color: red; }", Encoding.UTF8.GetString(inflated.ToArray()));
    }

    /// <summary>
    /// A request for a path with no file behind it falls back to the configured file. This is what
    /// makes client-side routing work: every unknown path serves the application shell.
    /// </summary>
    [Fact]
    public async Task AnUnknownPathServesTheConfiguredFallbackFile() {
        WriteFile("index.html", "<html>shell</html>");

        var (context, body, _, _) = Context("/app/deep/route");

        var handler = Handler(configuration => configuration.FallBackFile.Returns("/index.html"));

        Assert.True(await handler.Handle(context));
        Assert.Equal("<html>shell</html>", Served(body));
    }

    /// <summary>
    /// A fallback file that does not exist is a configuration error, and it is raised as one on
    /// the first request rather than recursing. Returning false instead would turn every unknown
    /// path into a 404 and hide the misconfiguration.
    /// </summary>
    [Fact]
    public async Task AFallbackFileThatDoesNotExistIsReportedRatherThanIgnored() {
        var (context, _, _, _) = Context("/app/deep/route");

        var handler = Handler(configuration => configuration.FallBackFile.Returns("/missing.html"));

        var exception = await Assert.ThrowsAsync<Exception>(() => handler.Handle(context));

        Assert.Contains("missing.html", exception.Message);
    }

    /// <summary>
    /// A conditional request whose ETag matches is answered 304 with no body. Serving the bytes
    /// anyway is the failure mode that makes caching headers pointless.
    /// </summary>
    [Fact]
    public async Task AMatchingIfNoneMatchIsAnsweredWith304AndNoBody() {
        WriteFile("logo.txt", "image bytes");

        // EnableETag defaults to false in this fixture, and without it the handler never emits an
        // ETag for If-None-Match to match against.
        var handler = Handler(configuration => configuration.EnableETag.Returns(true));

        var (first, _, _, _) = Context("/logo.txt");

        await handler.Handle(first);

        var etag = new ETagProvider(new TestMD5Pool()).GenerateETag("image bytes"u8.ToArray());

        var (second, body, response, _) = Context("/logo.txt", (KnownHeaders.IfNoneMatch, etag));

        Assert.True(await handler.Handle(second));

        response.Received().Status = 304;
        Assert.Empty(body.ToArray());
    }

    /// <summary>A conditional request whose ETag does not match gets the file.</summary>
    [Fact]
    public async Task ANonMatchingIfNoneMatchGetsTheFile() {
        WriteFile("logo.txt", "image bytes");

        var (context, body, response, _) = Context(
            "/logo.txt", (KnownHeaders.IfNoneMatch, "\"some-other-etag\""));

        Assert.True(await Handler().Handle(context));

        response.Received().Status = 200;
        Assert.Equal("image bytes", Served(body));
    }

    /// <summary>
    /// A configured max age reaches the response as a Cache-Control header. Without it the browser
    /// revalidates every asset on every navigation.
    /// </summary>
    [Fact]
    public async Task AConfiguredMaxAgeIsSentAsCacheControl() {
        WriteFile("asset.txt", "asset");

        var (context, _, _, headers) = Context("/asset.txt");

        var handler = Handler(configuration => configuration.CacheMaxAge.Returns(3600));

        Assert.True(await handler.Handle(context));
        Assert.Equal("max-age=3600", headers[KnownHeaders.CacheControl].ToString());
    }

    /// <summary>
    /// <c>Immutable</c> is appended to the same header rather than replacing it — a fingerprinted
    /// asset is both cacheable for a long time and never revalidated.
    /// </summary>
    [Fact]
    public async Task AnImmutableAssetSaysSoAlongsideItsMaxAge() {
        WriteFile("asset.txt", "asset");

        var (context, _, _, headers) = Context("/asset.txt");

        var handler = Handler(configuration => {
            configuration.CacheMaxAge.Returns(31536000);
            configuration.Immutable.Returns(true);
        });

        Assert.True(await handler.Handle(context));
        Assert.Equal("max-age=31536000, immutable", headers[KnownHeaders.CacheControl].ToString());
    }

    /// <summary>No configured max age means no Cache-Control header at all, not an empty one.</summary>
    [Fact]
    public async Task NoConfiguredMaxAgeSendsNoCacheControlHeader() {
        WriteFile("asset.txt", "asset");

        var (context, _, _, headers) = Context("/asset.txt");

        Assert.True(await Handler().Handle(context));
        Assert.DoesNotContain(KnownHeaders.CacheControl, headers.Keys);
    }

    /// <summary>
    /// The response callback is the only hook an application has into a static response, so it
    /// runs for a served file.
    /// </summary>
    [Fact]
    public async Task ThePrepareResponseCallbackRunsForAServedFile() {
        WriteFile("asset.txt", "asset");

        var (context, _, _, _) = Context("/asset.txt");

        var calls = 0;

        var handler = Handler(configuration =>
            configuration.OnPrepareResponse.Returns(new Action<IExecutionContext>(_ => calls++)));

        Assert.True(await handler.Handle(context));
        Assert.Equal(1, calls);
    }

    /// <summary>
    /// It runs for a 304 as well. A 304 carries headers and no body, and an application adding a
    /// header from this callback needs it on both answers or the header disappears the moment a
    /// client starts caching.
    /// </summary>
    [Fact]
    public async Task ThePrepareResponseCallbackRunsForANotModifiedResponse() {
        WriteFile("asset.txt", "asset");

        var calls = 0;

        var handler = Handler(configuration =>
            configuration.OnPrepareResponse.Returns(new Action<IExecutionContext>(_ => calls++)));

        var (first, _, _, _) = Context("/asset.txt");

        await handler.Handle(first);

        var etag = new ETagProvider(new TestMD5Pool()).GenerateETag("asset"u8.ToArray());

        var (second, _, _, _) = Context("/asset.txt", (KnownHeaders.IfNoneMatch, etag));

        await handler.Handle(second);

        Assert.Equal(2, calls);
    }

    /// <summary>
    /// Text content over the size threshold is compressed on first read and stored compressed, so
    /// every later request is served from the compressed copy.
    /// </summary>
    [Fact]
    public async Task LargeTextContentIsCompressedOnTheWayIntoTheCache() {
        var large = new string('a', 2000);

        WriteFile("large.txt", large);

        var (context, body, _, headers) = Context(
            "/large.txt", (KnownHeaders.AcceptEncoding, KnownEncoding.GZip));

        var handler = Handler(configuration => configuration.CompressTextContent.Returns(true));

        Assert.True(await handler.Handle(context));
        Assert.Equal(KnownEncoding.GZip, headers[KnownHeaders.ContentEncoding].ToString());
        Assert.True(body.Length < large.Length, "the response was no smaller than the source text");
    }

    /// <summary>
    /// Text content under the threshold is left alone. Compressing a 20-byte file costs more than
    /// it saves, and the header would make the client inflate it for nothing.
    /// </summary>
    [Fact]
    public async Task SmallTextContentIsNotCompressed() {
        WriteFile("small.txt", "small");

        var (context, body, _, headers) = Context(
            "/small.txt", (KnownHeaders.AcceptEncoding, KnownEncoding.GZip));

        var handler = Handler(configuration => configuration.CompressTextContent.Returns(true));

        Assert.True(await handler.Handle(context));
        Assert.Equal("small", Served(body));
        Assert.DoesNotContain(KnownHeaders.ContentEncoding, headers.Keys);
    }

    /// <summary>
    /// Binary content is never compressed however large it is — a JPEG re-compressed with gzip
    /// grows.
    /// </summary>
    [Fact]
    public async Task LargeBinaryContentIsNotCompressed() {
        WriteFile("large.bin", new string('a', 2000));

        var configuration = Substitute.For<IStaticContentConfiguration>();

        configuration.Path.Returns(_staticRoot);
        configuration.CompressTextContent.Returns(true);
        configuration.CacheMaxAge.Returns((int?)null);
        configuration.FallBackFile.Returns((string?)null);

        var mimeHelper = Substitute.For<IFileExtToMimeTypeHelper>();

        mimeHelper.GetMimeTypeInfo(Arg.Any<string>()).Returns(("application/octet-stream", true));

        var handler = new StaticContentHandler(
            Options.Create(configuration),
            mimeHelper,
            new GZipStaticContentCompressor(new MemoryStreamPool()),
            new ETagProvider(new TestMD5Pool()),
            new MemoryStreamPool(),
            NullLogger<StaticContentHandler>.Instance);

        var (context, body, response, headers) = Context(
            "/large.bin", (KnownHeaders.AcceptEncoding, KnownEncoding.GZip));

        Assert.True(await handler.Handle(context));
        Assert.Equal(2000, body.Length);
        Assert.DoesNotContain(KnownHeaders.ContentEncoding, headers.Keys);

        response.Received().IsBinary = true;
    }

    /// <summary>
    /// The content type comes from the mime helper and reaches the response, and the length of
    /// what was written is declared. A transport that streams the body relies on the header.
    /// </summary>
    [Fact]
    public async Task AServedFileDeclaresItsContentTypeAndLength() {
        WriteFile("page.txt", "0123456789");

        var (context, _, response, headers) = Context("/page.txt");

        Assert.True(await Handler().Handle(context));

        response.Received().ContentType = "text/plain";
        Assert.Equal("10", headers[KnownHeaders.ContentLength].ToString());
    }
}
