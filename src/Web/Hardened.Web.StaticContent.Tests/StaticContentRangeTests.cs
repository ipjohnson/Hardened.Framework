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
/// Byte ranges, and the date-based conditional beside them.
///
/// <para>
/// Both are what a media element and a download manager need, and neither existed: there was no
/// <c>Accept-Ranges</c>, so a client had to assume seeking did not work, and no
/// <c>Last-Modified</c>, so a cache with no validator had nothing at all to revalidate against.
/// </para>
/// </summary>
public class StaticContentRangeTests : IDisposable {

    private readonly string _tempRoot;
    private readonly string _staticRoot;

    /// <summary>Distinguishable bytes, so a slice can be checked against where it came from.</summary>
    private const string Body = "0123456789";

    public StaticContentRangeTests() {
        _tempRoot = Path.Combine(Path.GetTempPath(), "hardened-range-" + Guid.NewGuid().ToString("N"));
        _staticRoot = Path.Combine(_tempRoot, "wwwroot");

        Directory.CreateDirectory(_staticRoot);

        File.WriteAllText(Path.Combine(_staticRoot, "clip.bin"), Body);
    }

    public void Dispose() {
        try { Directory.Delete(_tempRoot, true); } catch { /* best effort */ }

        GC.SuppressFinalize(this);
    }

    private StaticContentPipeline Handler(
        Action<IStaticContentConfiguration>? configure = null, bool text = false) {
        var configuration = Substitute.For<IStaticContentConfiguration>();

        configuration.Path.Returns(_staticRoot);
        configuration.CacheContent.Returns(true);
        configuration.EnableRangeRequests.Returns(true);
        configuration.EnableETag.Returns(true);
        configuration.CompressTextContent.Returns(false);
        configuration.FallBackFile.Returns((string?)null);
        configuration.CacheMaxAge.Returns((int?)null);

        configure?.Invoke(configuration);

        var mimeHelper = Substitute.For<IFileExtToMimeTypeHelper>();

        mimeHelper.GetMimeTypeInfo(Arg.Any<string>())
            .Returns(text ? ("text/plain", false) : ("application/octet-stream", true));

        return new StaticContentPipeline(
            new FileSystemContentSource(
                Options.Create(configuration), mimeHelper,
                new GZipStaticContentCompressor(new MemoryStreamPool()),
                new ETagProvider(new TestMD5Pool()),
                NullLogger<FileSystemContentSource>.Instance),
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

        var outgoing = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        request.Path.Returns(path);
        request.Method.Returns("GET");
        request.Headers.Returns(headers);
        response.Body.Returns(body);
        response.Headers.Returns(outgoing);
        context.Request.Returns(request);
        context.Response.Returns(response);

        return (context, body, response, outgoing);
    }

    private static string Served(MemoryStream body) => Encoding.UTF8.GetString(body.ToArray());

    #region ranges

    /// <summary>
    /// A resource says ranges work. Without the header a client assumes they do not, which is why a
    /// video served by the old handler played from the start and could not be scrubbed.
    /// </summary>
    [Fact]
    public async Task AServedFileAdvertisesThatRangesWork() {
        var (context, _, _, headers) = Context("/clip.bin");

        Assert.True(await Handler().Handle(context));
        Assert.Equal("bytes", headers[KnownHeaders.AcceptRanges].ToString());
    }

    [Theory]
    [InlineData("bytes=0-4", "01234", "bytes 0-4/10")]
    [InlineData("bytes=5-9", "56789", "bytes 5-9/10")]
    [InlineData("bytes=3-", "3456789", "bytes 3-9/10")]
    [InlineData("bytes=-3", "789", "bytes 7-9/10")]
    [InlineData("bytes=0-0", "0", "bytes 0-0/10")]
    public async Task ARangeIsAnsweredWith206AndOnlyThoseBytes(
        string range, string expected, string contentRange) {
        var (context, body, response, headers) = Context(
            "/clip.bin", (KnownHeaders.Range, range));

        Assert.True(await Handler().Handle(context));

        response.Received().Status = 206;
        Assert.Equal(expected, Served(body));
        Assert.Equal(contentRange, headers[KnownHeaders.ContentRange].ToString());

        // The length of the slice, not of the resource. A client that trusted the whole length
        // would sit waiting for bytes that are never coming.
        Assert.Equal(expected.Length.ToString(), headers[KnownHeaders.ContentLength].ToString());
    }

    /// <summary>
    /// A range starting past the end is refused with the length attached, which is how a client
    /// that guessed wrong learns what to ask for without a second round trip.
    /// </summary>
    [Fact]
    public async Task ARangePastTheEndIs416WithTheLength() {
        var (context, body, response, headers) = Context(
            "/clip.bin", (KnownHeaders.Range, "bytes=50-60"));

        Assert.True(await Handler().Handle(context));

        response.Received().Status = 416;
        Assert.Empty(body.ToArray());
        Assert.Equal("bytes */10", headers[KnownHeaders.ContentRange].ToString());
    }

    /// <summary>
    /// A range nothing can parse is ignored and the whole entity sent, per RFC 9110 §14.2. A 416
    /// would say the range was impossible when what happened is that it was not understood.
    /// </summary>
    [Theory]
    [InlineData("items=0-4")]
    [InlineData("bytes=nonsense")]
    [InlineData("bytes=0-4,6-9")]
    public async Task AnUnparseableOrMultipleRangeGetsTheWholeEntity(string range) {
        var (context, body, response, headers) = Context(
            "/clip.bin", (KnownHeaders.Range, range));

        Assert.True(await Handler().Handle(context));

        response.Received().Status = 200;
        Assert.Equal(Body, Served(body));
        Assert.DoesNotContain(KnownHeaders.ContentRange, headers.Keys);
    }

    /// <summary>
    /// Ranges are refused outright for a mount that turned them off, which is a mount serving
    /// nothing seekable and not wanting a second code path exercised.
    /// </summary>
    [Fact]
    public async Task AMountWithRangesOffNeitherAdvertisesNorHonoursThem() {
        var handler = Handler(configuration => configuration.EnableRangeRequests.Returns(false));

        var (context, body, response, headers) = Context(
            "/clip.bin", (KnownHeaders.Range, "bytes=0-4"));

        Assert.True(await handler.Handle(context));

        response.Received().Status = 200;
        Assert.Equal(Body, Served(body));
        Assert.DoesNotContain(KnownHeaders.AcceptRanges, headers.Keys);
    }

    /// <summary>
    /// A compressed representation is never ranged. A byte offset into a gzip stream is not a byte
    /// offset into the resource, and <c>Content-Range</c> cannot say which one it meant.
    /// </summary>
    [Fact]
    public async Task ACompressedRepresentationIsNotRangeable() {
        File.WriteAllText(Path.Combine(_staticRoot, "big.txt"), new string('a', 4000));

        var handler = Handler(
            configuration => configuration.CompressTextContent.Returns(true), text: true);

        var (context, _, response, headers) = Context(
            "/big.txt",
            (KnownHeaders.AcceptEncoding, "gzip, deflate, br"),
            (KnownHeaders.Range, "bytes=0-99"));

        Assert.True(await handler.Handle(context));

        // Compressed on the way out, so the range is not honoured and not advertised.
        Assert.Equal(KnownEncoding.GZip, headers[KnownHeaders.ContentEncoding].ToString());

        response.Received().Status = 200;
        Assert.DoesNotContain(KnownHeaders.ContentRange, headers.Keys);
        Assert.DoesNotContain(KnownHeaders.AcceptRanges, headers.Keys);
    }

    /// <summary>
    /// <c>If-Range</c> is the guard against resuming a download into a file that changed underneath
    /// it: the range holds only while the client's copy is still current.
    /// </summary>
    [Fact]
    public async Task AnIfRangeThatStillMatchesHonoursTheRange() {
        var handler = Handler();

        var (warm, _, _, warmHeaders) = Context("/clip.bin");

        await handler.Handle(warm);

        var etag = warmHeaders[KnownHeaders.ETag].ToString();

        var (context, body, response, _) = Context(
            "/clip.bin", (KnownHeaders.Range, "bytes=0-4"), (KnownHeaders.IfRange, etag));

        Assert.True(await handler.Handle(context));

        response.Received().Status = 206;
        Assert.Equal("01234", Served(body));
    }

    /// <summary>
    /// And one that does not match sends the whole entity rather than an error - the client wants
    /// the resource either way, and has just learned its copy is stale.
    /// </summary>
    [Fact]
    public async Task AnIfRangeThatNoLongerMatchesSendsTheWholeEntity() {
        var (context, body, response, _) = Context(
            "/clip.bin",
            (KnownHeaders.Range, "bytes=0-4"),
            (KnownHeaders.IfRange, "\"a-tag-from-a-previous-deploy\""));

        Assert.True(await Handler().Handle(context));

        response.Received().Status = 200;
        Assert.Equal(Body, Served(body));
    }

    #endregion

    #region the date validator

    /// <summary>
    /// A file says when it changed. It is what a cache with no entity-tag falls back on, and there
    /// was no way to get one at all before.
    /// </summary>
    [Fact]
    public async Task AServedFileSaysWhenItLastChanged() {
        var (context, _, _, headers) = Context("/clip.bin");

        Assert.True(await Handler().Handle(context));

        var lastModified = headers[KnownHeaders.LastModified].ToString();

        Assert.False(string.IsNullOrEmpty(lastModified));

        // The one date format HTTP has. A client echoes this back verbatim.
        Assert.EndsWith("GMT", lastModified);
        Assert.True(DateTimeOffset.TryParseExact(
            lastModified, "R", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out _));
    }

    /// <summary>A client holding the current copy, asking by date, is told nothing changed.</summary>
    [Fact]
    public async Task AnIfModifiedSinceAtOrAfterTheFileIsNotModified() {
        var handler = Handler();

        var (warm, _, _, warmHeaders) = Context("/clip.bin");

        await handler.Handle(warm);

        var lastModified = warmHeaders[KnownHeaders.LastModified].ToString();

        var (context, body, response, _) = Context(
            "/clip.bin", (KnownHeaders.IfModifiedSince, lastModified));

        Assert.True(await handler.Handle(context));

        response.Received().Status = 304;
        Assert.Empty(body.ToArray());
    }

    /// <summary>And one holding an older copy gets the file.</summary>
    [Fact]
    public async Task AnIfModifiedSinceBeforeTheFileGetsTheFile() {
        var (context, body, response, _) = Context(
            "/clip.bin",
            (KnownHeaders.IfModifiedSince, new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero)
                .ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

        Assert.True(await Handler().Handle(context));

        response.Received().Status = 200;
        Assert.Equal(Body, Served(body));
    }

    /// <summary>
    /// <c>If-None-Match</c> outranks the date outright, per RFC 9110 §13.2.1 - including when it
    /// does not match, in which case the date is not consulted at all. A validator is a stronger
    /// statement than a timestamp, and a client that sent both meant the validator.
    /// </summary>
    [Fact]
    public async Task AnEntityTagOutranksTheDateEvenWhenItDoesNotMatch() {
        var handler = Handler();

        var (warm, _, _, warmHeaders) = Context("/clip.bin");

        await handler.Handle(warm);

        var lastModified = warmHeaders[KnownHeaders.LastModified].ToString();

        // The date says "unchanged", the tag says "different". The tag wins, so this is a 200.
        var (context, body, response, _) = Context(
            "/clip.bin",
            (KnownHeaders.IfNoneMatch, "\"from-another-deploy\""),
            (KnownHeaders.IfModifiedSince, lastModified));

        Assert.True(await handler.Handle(context));

        response.Received().Status = 200;
        Assert.Equal(Body, Served(body));
    }

    /// <summary>A date nothing can parse is ignored rather than refused.</summary>
    [Fact]
    public async Task AnUnparseableIfModifiedSinceIsIgnored() {
        var (context, body, response, _) = Context(
            "/clip.bin", (KnownHeaders.IfModifiedSince, "not a date"));

        Assert.True(await Handler().Handle(context));

        response.Received().Status = 200;
        Assert.Equal(Body, Served(body));
    }

    #endregion

    #region not caching

    /// <summary>
    /// With caching off an edit is visible on the next request. This is the whole of the
    /// development story - no watcher, no change token, no invalidation.
    /// </summary>
    [Fact]
    public async Task WithCachingOffAnEditIsVisibleOnTheNextRequest() {
        var handler = Handler(configuration => configuration.CacheContent.Returns(false));

        var (first, firstBody, _, _) = Context("/clip.bin");

        Assert.True(await handler.Handle(first));
        Assert.Equal(Body, Served(firstBody));

        File.WriteAllText(Path.Combine(_staticRoot, "clip.bin"), "CHANGED");

        var (second, secondBody, _, _) = Context("/clip.bin");

        Assert.True(await handler.Handle(second));
        Assert.Equal("CHANGED", Served(secondBody));
    }

    /// <summary>
    /// And the validator moves with it, so a browser holding the old copy is not told it is still
    /// current. Serving fresh bytes under a stale ETag would make not caching worse than caching.
    /// </summary>
    [Fact]
    public async Task WithCachingOffTheValidatorMovesWithTheFile() {
        var handler = Handler(configuration => configuration.CacheContent.Returns(false));

        var (first, _, _, firstHeaders) = Context("/clip.bin");

        await handler.Handle(first);

        File.WriteAllText(Path.Combine(_staticRoot, "clip.bin"), "CHANGED");

        var (second, _, _, secondHeaders) = Context("/clip.bin");

        await handler.Handle(second);

        Assert.NotEqual(
            firstHeaders[KnownHeaders.ETag].ToString(),
            secondHeaders[KnownHeaders.ETag].ToString());
    }

    /// <summary>
    /// Compression follows caching. Paid once on the way into a cache it is recovered on every
    /// request after; paid per request at the level that produces the smallest result, it is the
    /// slowest thing on the path and buys a browser on localhost nothing.
    /// </summary>
    [Fact]
    public async Task WithCachingOffTextIsNotCompressed() {
        File.WriteAllText(Path.Combine(_staticRoot, "big.txt"), new string('a', 4000));

        var handler = Handler(configuration => {
            configuration.CacheContent.Returns(false);
            configuration.CompressTextContent.Returns(true);
        });

        var (context, body, _, headers) = Context(
            "/big.txt", (KnownHeaders.AcceptEncoding, "gzip, deflate, br"));

        Assert.True(await handler.Handle(context));

        Assert.DoesNotContain(KnownHeaders.ContentEncoding, headers.Keys);
        Assert.Equal(4000, body.Length);
    }

    #endregion
}
