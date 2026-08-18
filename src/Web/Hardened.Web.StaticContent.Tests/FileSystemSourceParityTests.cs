using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Utilities;
using Hardened.Web.Runtime.CacheControl;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Web.StaticContent.Tests;

/// <summary>
/// The protections the build task applies, applied to the directory this reads instead.
///
/// <para>
/// An application that declares no <c>&lt;HardenedStaticContent&gt;</c> items gets this source, so
/// anything only the manifest enforced was a protection you had to opt into - which is the wrong
/// way round for a symlink escaping the content root or a <c>.env</c> being served. These are the
/// cases where the two sources have to agree.
/// </para>
/// </summary>
public class FileSystemSourceParityTests : IDisposable {

    private readonly string _tempRoot;
    private readonly string _staticRoot;

    public FileSystemSourceParityTests() {
        _tempRoot = Path.Combine(Path.GetTempPath(), "hardened-parity-" + Guid.NewGuid().ToString("N"));
        _staticRoot = Path.Combine(_tempRoot, "wwwroot");

        Directory.CreateDirectory(_staticRoot);
    }

    public void Dispose() {
        try { Directory.Delete(_tempRoot, true); } catch { /* best effort */ }

        GC.SuppressFinalize(this);
    }

    private void Write(string relative, string content) {
        var path = Path.Combine(_staticRoot, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private FileSystemContentSource Source(Action<IStaticContentConfiguration>? configure = null) {
        var configuration = Substitute.For<IStaticContentConfiguration>();

        configuration.Path.Returns(_staticRoot);
        configuration.CacheContent.Returns(true);
        configuration.CacheControlType.Returns(CacheControlEnum.MaxAge | CacheControlEnum.Public);
        configuration.EnableRangeRequests.Returns(true);
        configuration.EnableETag.Returns(true);
        configuration.CompressTextContent.Returns(false);
        configuration.ServeHiddenFiles.Returns(false);
        configuration.FallBackFile.Returns((string?)null);
        configuration.CacheMaxAge.Returns((int?)null);

        configure?.Invoke(configuration);

        var mimeHelper = Substitute.For<IFileExtToMimeTypeHelper>();

        mimeHelper.GetMimeTypeInfo(Arg.Any<string>()).Returns(("text/plain", false));

        return new FileSystemContentSource(
            Options.Create(configuration), mimeHelper,
            new GZipStaticContentCompressor(new MemoryStreamPool()),
            new ETagProvider(new TestHashPool()),
            NullLogger<FileSystemContentSource>.Instance);
    }

    private static string? Key(FileSystemContentSource source, string requestPath) =>
        source.Locate(requestPath)?.Key;

    /// <summary>A source whose warnings are collected, for the ones that only log.</summary>
    private FileSystemContentSource SourceWithLog(
        List<string> messages, Action<IStaticContentConfiguration> configure) {
        var configuration = Substitute.For<IStaticContentConfiguration>();

        configuration.Path.Returns(_staticRoot);
        configuration.CacheContent.Returns(true);
        configuration.CompressTextContent.Returns(false);
        configuration.FallBackFile.Returns((string?)null);
        configuration.CacheMaxAge.Returns((int?)null);

        configure(configuration);

        var mimeHelper = Substitute.For<IFileExtToMimeTypeHelper>();

        mimeHelper.GetMimeTypeInfo(Arg.Any<string>()).Returns(("text/plain", false));

        return new FileSystemContentSource(
            Options.Create(configuration), mimeHelper,
            new GZipStaticContentCompressor(new MemoryStreamPool()),
            new ETagProvider(new TestHashPool()),
            new CollectingLogger(messages));
    }

    /// <summary>Keeps what was logged, for behaviour whose only output is a warning.</summary>
    private sealed class CollectingLogger(List<string> messages)
        : Microsoft.Extensions.Logging.ILogger<FileSystemContentSource> {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Add(formatter(state, exception));
    }

    #region symlinks

    /// <summary>
    /// A link out of the content root is refused. Lexical containment cannot see it -
    /// <c>Path.GetFullPath</c> does not follow links - so without resolving it the file is simply
    /// served, and anything that populates the directory can create one.
    /// </summary>
    [Fact]
    public void ALinkOutOfTheRootIsNotServed() {
        File.WriteAllText(Path.Combine(_tempRoot, "secret.txt"), "SECRET");

        try {
            File.CreateSymbolicLink(
                Path.Combine(_staticRoot, "link.txt"), Path.Combine(_tempRoot, "secret.txt"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            return; // No permission to create links here; nothing to assert.
        }

        Assert.Null(Key(Source(), "/link.txt"));
    }

    /// <summary>A link that stays inside is fine - it is escaping that is the problem.</summary>
    [Fact]
    public void ALinkInsideTheRootIsServed() {
        Write("real.txt", "content");

        try {
            File.CreateSymbolicLink(
                Path.Combine(_staticRoot, "link.txt"), Path.Combine(_staticRoot, "real.txt"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            return;
        }

        Assert.NotNull(Key(Source(), "/link.txt"));
    }

    /// <summary>
    /// A path that never exists on disk still resolves through its pre-compressed sibling. This is
    /// the case that makes the link check belong on the concrete file rather than on the request
    /// path: <c>ResolveLinkTarget</c> raises for a path that is not there, and <c>/app.js</c>
    /// served by <c>app.js.gz</c> is exactly that.
    /// </summary>
    [Fact]
    public void APathServedByASiblingStillResolves() {
        File.WriteAllBytes(Path.Combine(_staticRoot, "app.js.gz"), [0x1f, 0x8b, 0x08, 0x00]);

        var location = Source().Locate("/app.js");

        Assert.NotNull(location);
        Assert.Equal(KnownEncoding.GZip, location!.Value.ContentEncoding);
    }

    /// <summary>
    /// A traversal is refused by the containment check rather than by the file simply not being
    /// there. The transport under test does not normalise the path, which is the position a source
    /// is left in on API Gateway.
    /// </summary>
    [Theory]
    [InlineData("/../secret.txt")]
    [InlineData("/assets/../../secret.txt")]
    public void ATraversalOutOfTheRootIsRefused(string path) {
        File.WriteAllText(Path.Combine(_tempRoot, "secret.txt"), "SECRET");

        Assert.Null(Key(Source(), path));
    }

    /// <summary>
    /// A root that is the whole filesystem is served, and said so. Nothing escapes a root that
    /// contains everything, so the containment check cannot object - which is the reason to warn.
    /// </summary>
    [Fact]
    public void AFilesystemRootIsUsableAndReported() {
        var warnings = new List<string>();

        var source = SourceWithLog(warnings, c => c.Path.Returns(Path.GetPathRoot(_staticRoot)!));

        Assert.True(source.Enabled);
        Assert.Contains(warnings, message => message.Contains("filesystem root"));
    }

    /// <summary>And a root that is the application's own directory, which serves its settings.</summary>
    [Fact]
    public void TheApplicationsOwnDirectoryIsReported() {
        var warnings = new List<string>();

        SourceWithLog(warnings, c => c.Path.Returns(AppContext.BaseDirectory));

        Assert.Contains(warnings, message => message.Contains("application's own directory"));
    }

    #endregion

    #region hidden files

    [Theory]
    [InlineData("/.env")]
    [InlineData("/.git/config")]
    [InlineData("/nested/.htpasswd")]
    [InlineData("/.npmrc")]
    public void AHiddenPathIsNotServed(string path) {
        Write(path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar), "secret");

        Assert.Null(Key(Source(), path));
    }

    /// <summary>
    /// <c>.well-known</c> is the exception, and not a small one: ACME challenges live under it, so
    /// refusing every hidden path without it breaks certificate renewal in a way nobody connects
    /// back to a static content setting.
    /// </summary>
    [Fact]
    public void WellKnownIsServedDespiteBeingHidden() {
        Write(Path.Combine(".well-known", "acme-challenge", "token"), "challenge-response");

        Assert.NotNull(Key(Source(), "/.well-known/acme-challenge/token"));
    }

    /// <summary>An application that means to serve them says so.</summary>
    [Fact]
    public void HiddenFilesAreServedWhenTheMountAsksForIt() {
        Write(".env", "SECRET_KEY=abc");

        Assert.NotNull(Key(Source(c => c.ServeHiddenFiles.Returns(true)), "/.env"));
    }

    /// <summary>A dot inside a name is not a hidden file.</summary>
    [Theory]
    [InlineData("/app.min.js")]
    [InlineData("/v1.2.3/app.js")]
    public void AnOrdinaryDottedNameIsUnaffected(string path) {
        Write(path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar), "content");

        Assert.NotNull(Key(Source(), path));
    }

    #endregion

    #region default documents

    /// <summary>
    /// A directory answers with its index, for both spellings. Without it the only way to serve
    /// anything at <c>/</c> was the single-page fall back, which then answered every unknown path
    /// too - so a plain static site was not expressible.
    /// </summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/assets")]
    [InlineData("/assets/")]
    public void ADirectoryAnswersWithItsIndex(string path) {
        Write("index.html", "<html>root</html>");
        Write(Path.Combine("assets", "index.html"), "<html>assets</html>");

        Assert.NotNull(Key(Source(), path));
    }

    /// <summary>And a directory with no index does not answer.</summary>
    [Fact]
    public void ADirectoryWithNoIndexDoesNotAnswer() {
        Write(Path.Combine("assets", "app.js"), "console.log('hi');");

        Assert.Null(Key(Source(), "/assets/"));
    }

    #endregion

    #region the fall back file

    /// <summary>
    /// A fall back file that is not there is disabled rather than raised on every request. It used
    /// to throw, forever, so a typo turned every 404 into a 500.
    /// </summary>
    [Fact]
    public void AMissingFallBackFileIsDisabledRatherThanThrown() {
        Write("app.js", "console.log('hi');");

        var source = Source(c => c.FallBackFile.Returns("/index.html"));

        Assert.Null(Key(source, "/unknown/path"));
        Assert.NotNull(Key(source, "/app.js"));
    }

    [Fact]
    public void AFallBackFileThatIsThereAnswersUnknownPaths() {
        Write("index.html", "<html>shell</html>");

        var source = Source(c => c.FallBackFile.Returns("/index.html"));

        var location = source.Locate("/deep/spa/route");

        Assert.NotNull(location);
        Assert.True(location!.Value.IsFallback);
    }

    #endregion

    #region single flight

    /// <summary>
    /// Concurrent first-requests for one file read and compress it once between them.
    /// <c>ConcurrentDictionary.GetOrAdd</c> does not promise its factory runs once, so without a
    /// <c>Lazy</c> every request arriving before the first finished did the whole job again - at
    /// the compression level that produces the smallest result, which is the slowest there is.
    /// </summary>
    [Fact]
    public async Task ConcurrentFirstRequestsReadTheFileOnce() {
        Write("hot.txt", new string('z', 4000));

        var hashes = 0;

        var etag = Substitute.For<IETagProvider>();

        etag.GenerateETag(Arg.Any<byte[]>()).Returns(_ => {
            Interlocked.Increment(ref hashes);

            return "one";
        });

        var configuration = Substitute.For<IStaticContentConfiguration>();

        configuration.Path.Returns(_staticRoot);
        configuration.CacheContent.Returns(true);
        configuration.CompressTextContent.Returns(false);
        configuration.FallBackFile.Returns((string?)null);
        configuration.CacheMaxAge.Returns((int?)null);

        var mimeHelper = Substitute.For<IFileExtToMimeTypeHelper>();

        mimeHelper.GetMimeTypeInfo(Arg.Any<string>()).Returns(("text/plain", false));

        var source = new FileSystemContentSource(
            Options.Create(configuration), mimeHelper,
            new GZipStaticContentCompressor(new MemoryStreamPool()), etag,
            NullLogger<FileSystemContentSource>.Instance);

        var location = source.Locate("/hot.txt")!.Value;

        await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => Task.Run(async () => await source.Load(location))));

        Assert.Equal(1, hashes);
    }

    #endregion

    #region cache control

    /// <summary>
    /// The directives the configuration asks for reach the header. Only <c>max-age</c> and
    /// <c>immutable</c> did before, so a mount could not say <c>private</c> at all - which is what
    /// an authorized mount needs, since <c>public</c> invites a shared cache to keep it.
    /// </summary>
    [Theory]
    [InlineData(CacheControlEnum.MaxAge | CacheControlEnum.Public, "public, max-age=60")]
    [InlineData(CacheControlEnum.MaxAge | CacheControlEnum.Private, "private, max-age=60")]
    [InlineData(CacheControlEnum.MaxAge | CacheControlEnum.NoCache, "no-cache, max-age=60")]
    [InlineData(CacheControlEnum.MaxAge | CacheControlEnum.NoStore, "no-store, max-age=60")]
    [InlineData(CacheControlEnum.MaxAge, "max-age=60")]
    [InlineData(
        CacheControlEnum.MaxAge | CacheControlEnum.Private | CacheControlEnum.NoTransform,
        "private, max-age=60, no-transform")]
    public void TheConfiguredDirectivesAreRendered(CacheControlEnum type, string expected) {
        var configuration = Substitute.For<IStaticContentConfiguration>();

        configuration.CacheControlType.Returns(type);
        configuration.CacheMaxAge.Returns(60);
        configuration.Immutable.Returns(false);

        Assert.Equal(expected, StaticContentWriter.CacheControlFor(configuration));
    }

    /// <summary>
    /// No max age means the mount says nothing about caching, which is the contract that shipped.
    /// Rendering the rest would put <c>public</c> on a response whose author configured no caching.
    /// </summary>
    [Fact]
    public void NoMaxAgeSendsNoHeaderAtAll() {
        var configuration = Substitute.For<IStaticContentConfiguration>();

        configuration.CacheControlType.Returns(CacheControlEnum.MaxAge | CacheControlEnum.Public);
        configuration.CacheMaxAge.Returns((int?)null);

        Assert.Null(StaticContentWriter.CacheControlFor(configuration));
    }

    #endregion
}
