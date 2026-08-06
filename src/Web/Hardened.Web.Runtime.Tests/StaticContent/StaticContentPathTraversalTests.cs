using Hardened.Requests.Abstract.Execution;
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
/// StaticContentHandler maps a request path onto the filesystem with
/// Path.Combine(root, requestPath.TrimStart('/')) and no canonicalisation, so whether a
/// traversal sequence can escape the configured root depends entirely on the transport
/// having normalised the path first.
///
/// These tests exercise the handler directly, which is the position a transport that does
/// not normalise leaves it in.
/// </summary>
public class StaticContentPathTraversalTests : IDisposable {

    private readonly string _tempRoot;
    private readonly string _staticRoot;
    private readonly string _secretFile;
    private readonly string _originalCurrentDirectory;

    public StaticContentPathTraversalTests() {
        _originalCurrentDirectory = Directory.GetCurrentDirectory();

        _tempRoot = Path.Combine(Path.GetTempPath(), "hardened-static-" + Guid.NewGuid().ToString("N"));
        _staticRoot = Path.Combine(_tempRoot, "wwwroot");
        Directory.CreateDirectory(_staticRoot);

        File.WriteAllText(Path.Combine(_staticRoot, "public.txt"), "public content");

        // A file a sibling of the static root, standing in for anything outside the
        // directory the server is meant to expose.
        _secretFile = Path.Combine(_tempRoot, "secret.txt");
        File.WriteAllText(_secretFile, "SECRET-CONTENT");

        Directory.SetCurrentDirectory(_tempRoot);
    }

    public void Dispose() {
        Directory.SetCurrentDirectory(_originalCurrentDirectory);
        try { Directory.Delete(_tempRoot, true); } catch { /* best effort */ }
    }

    private static StaticContentHandler Handler() {
        var configuration = Substitute.For<IStaticContentConfiguration>();
        configuration.Path.Returns("wwwroot");
        configuration.EnableETag.Returns(false);
        configuration.CompressTextContent.Returns(false);
        configuration.FallBackFile.Returns((string?)null);
        configuration.CacheMaxAge.Returns((int?)null);

        var mimeHelper = Substitute.For<IFileExtToMimeTypeHelper>();
        mimeHelper.GetMimeTypeInfo(Arg.Any<string>()).Returns(("text/plain", false));

        var etag = Substitute.For<IETagProvider>();
        etag.GenerateETag(Arg.Any<byte[]>()).Returns("etag");

        return new StaticContentHandler(
            Options.Create(configuration),
            mimeHelper,
            Substitute.For<IGZipStaticContentCompressor>(),
            etag,
            new MemoryStreamPool(),
            NullLogger<StaticContentHandler>.Instance);
    }

    private static (IExecutionContext context, MemoryStream body) Context(string path) {
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();
        var response = Substitute.For<IExecutionResponse>();
        var body = new MemoryStream();

        request.Path.Returns(path);
        request.Headers.Returns(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase));
        response.Body.Returns(body);
        response.Headers.Returns(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase));
        context.Request.Returns(request);
        context.Response.Returns(response);

        return (context, body);
    }

    [Fact]
    public async Task ServesAFileInsideTheConfiguredRoot() {
        var (context, body) = Context("/public.txt");

        var handled = await Handler().Handle(context);

        Assert.True(handled);
        Assert.Equal("public content", System.Text.Encoding.UTF8.GetString(body.ToArray()));
    }

    [Fact]
    public async Task DoesNotHandleAMissingFile() {
        var (context, _) = Context("/does-not-exist.txt");

        Assert.False(await Handler().Handle(context));
    }

    /// <summary>
    /// A traversal sequence must not reach a file outside the configured root. If this
    /// fails, an unnormalised request path is able to read arbitrary files the process can
    /// see.
    /// </summary>
    [Fact]
    public async Task TraversalOutsideTheRootIsNotServed() {
        var (context, body) = Context("/../secret.txt");

        var handled = await Handler().Handle(context);
        var served = System.Text.Encoding.UTF8.GetString(body.ToArray());

        Assert.False(handled,
            "a path traversal escaped the configured static root and was served");
        Assert.DoesNotContain("SECRET-CONTENT", served);
    }

    [Fact]
    public async Task DeepTraversalOutsideTheRootIsNotServed() {
        var (context, body) = Context("/assets/../../secret.txt");

        var handled = await Handler().Handle(context);
        var served = System.Text.Encoding.UTF8.GetString(body.ToArray());

        Assert.False(handled,
            "a nested path traversal escaped the configured static root and was served");
        Assert.DoesNotContain("SECRET-CONTENT", served);
    }
}
