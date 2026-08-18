using System.IO.Compression;
using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Web.StaticContent.Tests;

/// <summary>
/// Serving from a manifest the build produced.
///
/// <para>
/// The properties worth asserting are the ones the file system source cannot have: a path not in
/// the table does not exist at all, so there is nothing for a traversal to reach; nothing is hashed
/// or compressed on the request path, so there is no cold cost and no stampede; and the table does
/// not grow, so no sequence of requests can make it.
/// </para>
/// </summary>
public class ManifestContentSourceTests : IDisposable {

    private readonly string _tempRoot;
    private readonly string _staticRoot;

    public ManifestContentSourceTests() {
        _tempRoot = Path.Combine(Path.GetTempPath(), "hardened-manifest-" + Guid.NewGuid().ToString("N"));
        _staticRoot = Path.Combine(_tempRoot, "wwwroot");

        Directory.CreateDirectory(_staticRoot);
    }

    public void Dispose() {
        try { Directory.Delete(_tempRoot, true); } catch { /* best effort */ }

        GC.SuppressFinalize(this);
    }

    #region harness

    private sealed record Manifest(
        IReadOnlyList<StaticContentManifestEntry> Entries, string? FallBackRoute)
        : IStaticContentManifest;

    private static byte[] GZip(byte[] bytes) {
        using var output = new MemoryStream();

        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)) {
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    private static StaticContentManifestEntry Embedded(
        string route, string content, bool compress = false) {
        var bytes = Encoding.UTF8.GetBytes(content);

        return new StaticContentManifestEntry(
            route, "hash-of-" + route, bytes.LongLength, new DateTimeOffset(
                2026, 8, 18, 10, 30, 0, TimeSpan.Zero).UtcTicks,
            bytes, compress ? GZip(bytes) : null, null);
    }

    private StaticContentPipeline Pipeline(
        IStaticContentManifest manifest, Action<IStaticContentConfiguration>? configure = null) {
        var configuration = Substitute.For<IStaticContentConfiguration>();

        configuration.Path.Returns(_staticRoot);
        configuration.CacheContent.Returns(true);
        configuration.EnableRangeRequests.Returns(true);
        configuration.EnableETag.Returns(true);
        configuration.CompressTextContent.Returns(false);
        configuration.CacheMaxAge.Returns((int?)null);

        configure?.Invoke(configuration);

        var mimeHelper = Substitute.For<IFileExtToMimeTypeHelper>();

        mimeHelper.GetMimeTypeInfo(Arg.Any<string>()).Returns(("text/plain", false));

        return new StaticContentPipeline(
            new ManifestContentSource(
                manifest, Options.Create(configuration), mimeHelper,
                NullLogger<ManifestContentSource>.Instance),
            configuration);
    }

    private static (IExecutionContext context, MemoryStream body, IExecutionResponse response,
        IDictionary<string, StringValues> headers)
        Context(string path, params (string Name, string Value)[] requestHeaders) {
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();
        var response = Substitute.For<IExecutionResponse>();
        var body = new MemoryStream();

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in requestHeaders) {
            headers[name] = value;
        }

        request.Path.Returns(path);
        request.Method.Returns("GET");
        request.Headers.Returns(headers);
        response.Body.Returns(body);
        response.Headers.Returns(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase));
        context.Request.Returns(request);
        context.Response.Returns(response);

        return (context, body, response, response.Headers);
    }

    private static string Served(MemoryStream body) => Encoding.UTF8.GetString(body.ToArray());

    #endregion

    #region serving

    [Fact]
    public async Task AnEmbeddedEntryIsServedFromTheAssembly() {
        var pipeline = Pipeline(new Manifest([Embedded("/app.js", "console.log('hi');")], null));

        var (context, body, _, _) = Context("/app.js");

        Assert.True(await pipeline.Handle(context));
        Assert.Equal("console.log('hi');", Served(body));
    }

    /// <summary>The validator comes from the build, so nothing is hashed on the request path.</summary>
    [Fact]
    public async Task TheValidatorComesFromTheManifest() {
        var pipeline = Pipeline(new Manifest([Embedded("/app.js", "x")], null));

        var (context, _, _, headers) = Context("/app.js");

        await pipeline.Handle(context);

        Assert.Equal("\"hash-of-/app.js\"", headers[KnownHeaders.ETag].ToString());
    }

    /// <summary>And so does the timestamp, so the date conditional works with no file access.</summary>
    [Fact]
    public async Task TheTimestampComesFromTheManifest() {
        var pipeline = Pipeline(new Manifest([Embedded("/app.js", "x")], null));

        var (context, _, _, headers) = Context("/app.js");

        await pipeline.Handle(context);

        Assert.Equal(
            "Tue, 18 Aug 2026 10:30:00 GMT", headers[KnownHeaders.LastModified].ToString());
    }

    /// <summary>
    /// A pre-compressed representation goes out as stored, compressed at build rather than on the
    /// first request - which is the cost the file system source pays, and pays again for every
    /// concurrent request that beats it to the cache.
    /// </summary>
    [Fact]
    public async Task ACompressedEntryIsServedWithoutCompressingAnything() {
        var pipeline = Pipeline(
            new Manifest([Embedded("/big.txt", new string('a', 4000), compress: true)], null));

        var (context, body, _, headers) = Context(
            "/big.txt", (KnownHeaders.AcceptEncoding, "gzip, deflate, br"));

        Assert.True(await pipeline.Handle(context));

        Assert.Equal(KnownEncoding.GZip, headers[KnownHeaders.ContentEncoding].ToString());
        Assert.Equal(KnownHeaders.AcceptEncoding, headers[KnownHeaders.Vary].ToString());
        Assert.True(body.Length < 4000);
    }

    /// <summary>And a client that cannot take the coding still gets the resource.</summary>
    [Fact]
    public async Task ACompressedEntryIsInflatedForAClientThatDidNotAskForIt() {
        var pipeline = Pipeline(
            new Manifest([Embedded("/big.txt", new string('a', 4000), compress: true)], null));

        var (context, body, _, headers) = Context("/big.txt");

        Assert.True(await pipeline.Handle(context));

        Assert.DoesNotContain(KnownHeaders.ContentEncoding, headers.Keys);
        Assert.Equal(4000, body.Length);
    }

    /// <summary>
    /// An entry the build left on disk is read from there, and the path comes from the manifest
    /// rather than from the request - so there is nothing a request can steer.
    /// </summary>
    [Fact]
    public async Task AnEntryLeftOnDiskIsReadFromDisk() {
        File.WriteAllText(Path.Combine(_staticRoot, "big.bin"), "on disk");

        var entry = new StaticContentManifestEntry(
            "/big.bin", "hash", 7, DateTimeOffset.UtcNow.UtcTicks, null, null, "big.bin");

        var (context, body, _, _) = Context("/big.bin");

        Assert.True(await Pipeline(new Manifest([entry], null)).Handle(context));
        Assert.Equal("on disk", Served(body));
    }

    /// <summary>
    /// One that is in the manifest and not on disk answers 404 rather than throwing. It is a
    /// deployment that shipped the assembly without its content.
    /// </summary>
    [Fact]
    public async Task AnEntryWhoseFileIsMissingIsNotFound() {
        var entry = new StaticContentManifestEntry(
            "/gone.bin", "hash", 7, DateTimeOffset.UtcNow.UtcTicks, null, null, "gone.bin");

        var (context, body, response, _) = Context("/gone.bin");

        await Pipeline(new Manifest([entry], null)).Handle(context);

        response.Received().Status = 404;
        Assert.Empty(body.ToArray());
    }

    #endregion

    #region what a manifest makes impossible

    /// <summary>
    /// A path not in the table does not exist. There is no filesystem lookup to escape, so the
    /// traversal question does not arise here at all - which is a stronger statement than the
    /// containment check the file system source has to make.
    /// </summary>
    [Theory]
    [InlineData("/../secret.txt")]
    [InlineData("/assets/../../secret.txt")]
    [InlineData("/does-not-exist.js")]
    [InlineData("/.env")]
    public async Task APathNotInTheManifestIsNotServed(string path) {
        File.WriteAllText(Path.Combine(_tempRoot, "secret.txt"), "SECRET");

        var pipeline = Pipeline(new Manifest([Embedded("/app.js", "x")], null));

        var (context, body, _, _) = Context(path);

        Assert.False(await pipeline.Handle(context));
        Assert.Empty(body.ToArray());
    }

    /// <summary>
    /// The route is matched exactly. A URL path is case sensitive, and matching loosely would serve
    /// a site in development that 404s in production, or the reverse.
    /// </summary>
    [Fact]
    public async Task RouteMatchingIsCaseSensitive() {
        var pipeline = Pipeline(new Manifest([Embedded("/app.js", "x")], null));

        var (context, _, _, _) = Context("/App.js");

        Assert.False(await pipeline.Handle(context));
    }

    /// <summary>
    /// The fall back answers unknown paths, and every one of them shares the single entry the build
    /// recorded. This is the shape that made the old cache grow without bound, and it cannot here:
    /// the table is fixed before the process starts.
    /// </summary>
    [Fact]
    public async Task EveryUnknownPathSharesTheOneFallbackEntry() {
        var pipeline = Pipeline(
            new Manifest([Embedded("/index.html", "<html>shell</html>")], "/index.html"));

        foreach (var path in new[] { "/a", "/b/c", "/deep/spa/route" }) {
            var (context, body, _, _) = Context(path);

            Assert.True(await pipeline.Handle(context));
            Assert.Equal("<html>shell</html>", Served(body));
        }
    }

    /// <summary>An empty manifest disables the mount rather than answering everything with nothing.</summary>
    [Fact]
    public async Task AnEmptyManifestServesNothing() {
        var pipeline = Pipeline(new Manifest([], null));

        var (context, _, _, _) = Context("/app.js");

        Assert.False(await pipeline.Handle(context));
    }

    #endregion
}
