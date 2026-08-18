using System.IO.Compression;
using System.Text;
using Hardened.Web.StaticContent.BuildTask;

namespace Hardened.Web.StaticContent.BuildTask.Tests;

/// <summary>
/// What the build works out so the runtime does not have to.
///
/// <para>
/// Each of these corresponds to something the file system source discovers per request, and to a
/// defect that discovery caused: a hash computed on every cold read, a compression that two
/// concurrent requests both perform, a link that lexical containment cannot see, a fall back file
/// whose absence surfaced as an exception on every unknown path forever.
/// </para>
/// </summary>
public class StaticContentScanTests : IDisposable {

    private readonly string _root;

    public StaticContentScanTests() {
        _root = Path.Combine(Path.GetTempPath(), "hardened-scan-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_root);
    }

    public void Dispose() {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }

        GC.SuppressFinalize(this);
    }

    private void Write(string relative, string content) {
        var path = Path.Combine(_root, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private ScanResult Scan(string prefix = "/", string? fallBack = null, long embed = 1024 * 1024) =>
        StaticContentScan.Scan(_root, prefix, fallBack, embed);

    private static ScannedFile Route(ScanResult scan, string route) =>
        scan.Files.Single(file => file.RoutePath == route);

    #region what a file becomes

    [Fact]
    public void AFileBecomesARouteUnderThePrefix() {
        Write("app.js", "console.log('hi');");

        Assert.Equal("/app.js", Route(Scan(), "/app.js").RoutePath);
        Assert.Equal("/static/app.js", Route(Scan("/static"), "/static/app.js").RoutePath);
        Assert.Equal("/static/app.js", Route(Scan("static/"), "/static/app.js").RoutePath);
    }

    /// <summary>Nested directories become nested routes, with forward slashes on every platform.</summary>
    [Fact]
    public void ANestedFileKeepsItsPath() {
        Write(Path.Combine("assets", "css", "site.css"), "body{}");

        var file = Route(Scan(), "/assets/css/site.css");

        Assert.Equal("/assets/css/site.css", file.RoutePath);
        Assert.DoesNotContain('\\', file.RoutePath);
    }

    /// <summary>
    /// The hash is of the content and is stable, which is the whole contract of a validator.
    /// SHA-256 rather than MD5, so a FIPS-enforcing host cannot take the static path down.
    /// </summary>
    [Fact]
    public void TheHashIsOfTheContent() {
        Write("a.txt", "same");
        Write("b.txt", "same");
        Write("c.txt", "different");

        var scan = Scan();

        Assert.Equal(Route(scan, "/a.txt").Hash, Route(scan, "/b.txt").Hash);
        Assert.NotEqual(Route(scan, "/a.txt").Hash, Route(scan, "/c.txt").Hash);

        // 32 bytes of SHA-256, base64.
        Assert.Equal(32, Convert.FromBase64String(Route(scan, "/a.txt").Hash).Length);
    }

    [Fact]
    public void AFileCarriesItsLengthAndWriteTime() {
        Write("a.txt", "0123456789");

        var file = Route(Scan(), "/a.txt");

        Assert.Equal(10, file.Length);
        Assert.True(file.LastModifiedUtcTicks > 0);
    }

    #endregion

    #region embedding and compression

    [Fact]
    public void AFileUnderTheThresholdIsEmbedded() {
        Write("small.txt", "small");

        var file = Route(Scan(embed: 1024), "/small.txt");

        Assert.NotNull(file.Content);
        Assert.Equal("small", Encoding.UTF8.GetString(file.Content!));
    }

    /// <summary>
    /// Over the threshold the bytes stay on disk and the manifest records where. Embedding makes
    /// assembly size a function of asset size, which is the wrong trade for anything large.
    /// </summary>
    [Fact]
    public void AFileOverTheThresholdIsLeftOnDisk() {
        Write("big.bin", new string('a', 5000));

        var file = Route(Scan(embed: 1024), "/big.bin");

        Assert.Null(file.Content);
        Assert.Null(file.GZipContent);
        Assert.Equal("big.bin", file.RelativePath);
    }

    /// <summary>
    /// Compressed once, here, at build. It is the cost the runtime used to pay on a cold request -
    /// and pay again for every concurrent request that arrived before the first one finished.
    /// </summary>
    [Fact]
    public void CompressibleContentIsCompressedAtBuild() {
        Write("big.txt", new string('a', 5000));

        var file = Route(Scan(), "/big.txt");

        Assert.NotNull(file.GZipContent);
        Assert.True(file.GZipContent!.Length < file.Content!.Length);

        using var source = new MemoryStream(file.GZipContent);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        gzip.CopyTo(inflated);

        Assert.Equal(5000, inflated.Length);
    }

    /// <summary>
    /// Compression is kept only when it helps. An already-compressed format reliably grows, and
    /// shipping that costs assembly size to make the response bigger.
    /// </summary>
    [Fact]
    public void ContentThatDoesNotCompressCarriesNoCompressedCopy() {
        // Random bytes stand in for a PNG or a woff2: nothing for gzip to find.
        var random = new byte[4096];
        new Random(20260818).NextBytes(random);

        File.WriteAllBytes(Path.Combine(_root, "noise.bin"), random);

        Assert.Null(Route(Scan(), "/noise.bin").GZipContent);
    }

    #endregion

    #region default documents

    /// <summary>
    /// A directory answers with its index, which is what makes a plain static site expressible.
    /// Without it the only way to answer <c>/</c> was the single-page fall back, which then
    /// answered every unknown path too.
    /// </summary>
    [Fact]
    public void AnIndexAnswersForItsDirectory() {
        Write("index.html", "<html>root</html>");
        Write(Path.Combine("assets", "index.html"), "<html>assets</html>");

        var scan = Scan();

        Assert.Equal("index.html", Route(scan, "/").RelativePath);
        Assert.Equal(Path.Combine("assets", "index.html"), Route(scan, "/assets/").RelativePath);
        Assert.Equal(Path.Combine("assets", "index.html"), Route(scan, "/assets").RelativePath);
    }

    /// <summary>An alias shares the file's bytes rather than emitting a second copy of them.</summary>
    [Fact]
    public void AnAliasSharesTheFileItPointsAt() {
        Write("index.html", "<html>root</html>");

        var scan = Scan();

        Assert.Same(Route(scan, "/index.html").Content, Route(scan, "/").Content);
    }

    /// <summary>
    /// A directory holding two default documents resolves by preference and not by whichever the
    /// file system handed over first - which on an ordinal walk is <c>index.htm</c>, the one nobody
    /// means.
    /// </summary>
    [Fact]
    public void ADirectoryWithTwoDefaultDocumentsPrefersTheHtmlOne() {
        Write("index.html", "<html>preferred</html>");
        Write("index.htm", "<html>also here</html>");

        Assert.Equal("index.html", Route(Scan(), "/").RelativePath);
    }

    /// <summary>
    /// A real file at the aliased route wins. Nothing invents a route over one that exists.
    /// </summary>
    [Fact]
    public void AFileAtTheDirectoryRouteIsNotOverwritten() {
        Write(Path.Combine("assets", "index.html"), "<html>nested</html>");

        var scan = Scan();

        // /assets is an alias here, because nothing real occupies it.
        Assert.Equal(Path.Combine("assets", "index.html"), Route(scan, "/assets").RelativePath);

        // And /assets/ is the other spelling of the same thing.
        Assert.Equal(Path.Combine("assets", "index.html"), Route(scan, "/assets/").RelativePath);
    }

    #endregion

    #region what the build refuses

    [Fact]
    public void AMissingDirectoryIsAnError() {
        var scan = StaticContentScan.Scan(
            Path.Combine(_root, "no-such-directory"), "/", null, 1024);

        var diagnostic = Assert.Single(scan.Diagnostics);

        Assert.Equal("HSTATIC001", diagnostic.Code);
        Assert.True(diagnostic.IsError);
    }

    /// <summary>
    /// A link out of the content root is refused at build. Lexical containment cannot see it -
    /// <c>Path.GetFullPath</c> does not follow links - so at run time it is simply served.
    /// </summary>
    [Fact]
    public void ALinkOutOfTheRootIsAnError() {
        var outside = Path.Combine(Path.GetTempPath(), "hardened-outside-" + Guid.NewGuid().ToString("N"));

        File.WriteAllText(outside, "SECRET");

        try {
            File.CreateSymbolicLink(Path.Combine(_root, "link.txt"), outside);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            return; // No permission to create links here; nothing to assert.
        }

        try {
            var scan = Scan();

            var diagnostic = Assert.Single(scan.Diagnostics, d => d.Code == "HSTATIC002");

            Assert.True(diagnostic.IsError);
            Assert.Empty(scan.Files);
        }
        finally {
            File.Delete(outside);
        }
    }

    /// <summary>A link that stays inside is fine - it is escaping that is the problem.</summary>
    [Fact]
    public void ALinkThatStaysInsideTheRootIsAccepted() {
        Write("real.txt", "content");

        try {
            File.CreateSymbolicLink(
                Path.Combine(_root, "link.txt"), Path.Combine(_root, "real.txt"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            return;
        }

        var scan = Scan();

        Assert.DoesNotContain(scan.Diagnostics, d => d.Code == "HSTATIC002");
        Assert.Equal(2, scan.Files.Count);
    }

    /// <summary>
    /// A file that looks like a secret is reported. Not a boundary - an author can still ship it -
    /// but the common case is a build step that copied a directory wholesale and nobody looked.
    /// </summary>
    [Theory]
    [InlineData(".env")]
    [InlineData("server.pem")]
    [InlineData("cert.pfx")]
    [InlineData("id_rsa")]
    public void ASecretLookingFileIsAWarning(string name) {
        Write(name, "secret");

        var diagnostic = Assert.Single(Scan().Diagnostics, d => d.Code == "HSTATIC003");

        Assert.False(diagnostic.IsError);
    }

    /// <summary>It is a warning rather than an error, so the file is still served.</summary>
    [Fact]
    public void ASecretLookingFileIsStillScanned() {
        Write(".env", "SECRET_KEY=abc");

        Assert.Equal("/.env", Route(Scan(), "/.env").RoutePath);
    }

    [Fact]
    public void AnEmptyDirectoryIsAWarning() {
        var diagnostic = Assert.Single(Scan().Diagnostics);

        Assert.Equal("HSTATIC004", diagnostic.Code);
        Assert.False(diagnostic.IsError);
    }

    #endregion

    #region the fall back file

    [Fact]
    public void AFallBackFileResolvesToItsRoute() {
        Write("index.html", "<html>shell</html>");

        Assert.Equal("/index.html", Scan(fallBack: "index.html").FallBackRoute);
        Assert.Equal("/index.html", Scan(fallBack: "/index.html").FallBackRoute);
        Assert.Equal("/app/index.html", Scan("/app", "index.html").FallBackRoute);
    }

    /// <summary>
    /// A fall back file that is not there is a build error. At run time it was an exception raised
    /// on every unknown path, forever - so a typo turned every 404 into a 500 in production.
    /// </summary>
    [Fact]
    public void AMissingFallBackFileIsAnError() {
        Write("app.js", "console.log('hi');");

        var scan = Scan(fallBack: "index.html");

        var diagnostic = Assert.Single(scan.Diagnostics, d => d.Code == "HSTATIC005");

        Assert.True(diagnostic.IsError);
        Assert.Null(scan.FallBackRoute);
    }

    [Fact]
    public void NoFallBackConfiguredIsNoFallBackRoute() {
        Write("app.js", "console.log('hi');");

        Assert.Null(Scan().FallBackRoute);
        Assert.DoesNotContain(Scan().Diagnostics, d => d.Code == "HSTATIC005");
    }

    #endregion

    #region determinism

    /// <summary>
    /// Two scans of the same tree produce the same thing, in the same order. Incremental builds and
    /// reproducible builds both require it, and a compressor stamping a timestamp would break both
    /// while still producing a valid file.
    /// </summary>
    [Fact]
    public void TwoScansOfOneTreeAgree() {
        Write("a.txt", "first");
        Write(Path.Combine("nested", "b.txt"), new string('b', 3000));
        Write("index.html", "<html/>");

        var first = Scan(fallBack: "index.html");
        var second = Scan(fallBack: "index.html");

        Assert.Equal(
            first.Files.Select(file => file.RoutePath),
            second.Files.Select(file => file.RoutePath));

        Assert.Equal(first.FallBackRoute, second.FallBackRoute);

        foreach (var (left, right) in first.Files.Zip(second.Files)) {
            Assert.Equal(left.Hash, right.Hash);
            Assert.Equal(left.GZipContent, right.GZipContent);
        }
    }

    #endregion
}
