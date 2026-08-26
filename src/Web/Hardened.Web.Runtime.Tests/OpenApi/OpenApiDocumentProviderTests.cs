using System.IO.Compression;
using System.Text;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.OpenApi;

/// <summary>
/// The document is held gzipped and served that way to anything that will take it, which is every
/// real client - so the uncompressed path is the exception rather than the norm, and both are worth
/// pinning.
/// </summary>
public class OpenApiDocumentProviderTests {

    private const string Document = """{"openapi":"3.1.0","info":{"title":"t","version":"1"},"paths":{}}""";

    /// <summary>
    /// A pass-through, standing in for the serialization filter.
    /// </summary>
    /// <remarks>
    /// The document sets <c>ShouldSerialize = false</c> and writes its own bytes, so the real
    /// filter has nothing to do here - and constructing one would drag a serializer and a content
    /// negotiator into tests about gzip.
    /// </remarks>
    private sealed class PassThrough : IExecutionFilter {
        public Task Execute(IExecutionChain chain) => chain.Next();
    }

    /// <summary>
    /// The services <c>ExecutionHelper</c> resolves while assembling a chain.
    /// </summary>
    /// <remarks>
    /// The provider builds its chain through the helper now, rather than hand-rolling one, which is
    /// what puts conventions and <c>IGlobalFilterRegistry</c> in front of the published document.
    /// The cost here is that these tests need the container a real application has.
    /// </remarks>
    private static ServiceProvider Services(Action<IServiceCollection>? configure = null) {
        var collection = new ServiceCollection();

        var ioProvider = Substitute.For<IIOFilterProvider>();
        ioProvider.ProvideFilter(
                Arg.Any<IExecutionRequestHandlerInfo>(),
                Arg.Any<Func<IExecutionContext, Task<IExecutionRequestParameters>>>())
            .Returns(new PassThrough());

        collection.AddSingleton(ioProvider);
        collection.AddSingleton<IInstanceFilterProvider, InstanceFilterProvider>();
        collection.AddSingleton<IGlobalFilterRegistry>(
            new GlobalFilterRegistry(Array.Empty<IRequestFilterProvider>()));
        collection.AddSingleton<OpenApiDocumentController>();

        configure?.Invoke(collection);

        return collection.BuildServiceProvider();
    }

    private static IExecutionContext Context(
        string path = "/openapi.json", string method = "GET", string? acceptEncoding = "gzip",
        IServiceProvider? services = null) {
        var request = new TestExecutionRequest(
            method, path, "application/json",
            new SimpleQueryStringCollection(new Dictionary<string, string>()));

        if (acceptEncoding != null) {
            request.Headers[KnownHeaders.AcceptEncoding] = acceptEncoding;
        }

        services ??= Services();

        return new TestExecutionContext(
            services, services, Substitute.For<IKnownServices>(), request,
            new TestExecutionResponse(new MemoryStream()), CancellationToken.None);
    }

    private static async Task<IExecutionContext> Serve(
        OpenApiDocumentProvider provider, IExecutionContext context) {
        var handler = provider.GetExecutionRequestHandler(context);

        Assert.NotNull(handler);

        await handler!.Handler!.GetExecutionChain(context).Next();

        return context;
    }

    private static byte[] BodyBytes(IExecutionContext context) {
        var body = (MemoryStream)context.Response.Body;

        return body.ToArray();
    }

    private static string Inflate(byte[] gzip) {
        using var source = new MemoryStream(gzip, writable: false);
        using var stream = new GZipStream(source, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        stream.CopyTo(inflated);

        return Encoding.UTF8.GetString(inflated.ToArray());
    }

    [Fact]
    public async Task Handle_ServesGZipToAClientThatAcceptsIt() {
        var context = await Serve(new OpenApiDocumentProvider(Services(), Document), Context());

        Assert.Equal(200, context.Response.Status);
        Assert.Equal("application/json", context.Response.ContentType);
        Assert.Equal(
            KnownEncoding.GZip, context.Response.Headers[KnownHeaders.ContentEncoding].ToString());
        Assert.Equal(Document, Inflate(BodyBytes(context)));
    }

    [Fact]
    public async Task Handle_InflatesForAClientThatDoesNotAcceptGZip() {
        var context = await Serve(
            new OpenApiDocumentProvider(Services(), Document), Context(acceptEncoding: "identity"));

        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.ContentEncoding));
        Assert.Equal(Document, Encoding.UTF8.GetString(BodyBytes(context)));
    }

    [Fact]
    public async Task Handle_InflatesWhenNoAcceptEncodingIsSentAtAll() {
        var context = await Serve(
            new OpenApiDocumentProvider(Services(), Document), Context(acceptEncoding: null));

        Assert.Equal(Document, Encoding.UTF8.GetString(BodyBytes(context)));
    }

    /// <summary>
    /// The header a browser actually sends lists several codings in one value, so matching it means
    /// searching within the value rather than comparing against it.
    /// </summary>
    [Theory]
    [InlineData("gzip")]
    [InlineData("gzip, deflate, br")]
    [InlineData("deflate, gzip")]
    [InlineData("br, zstd, gzip, deflate")]
    [InlineData("GZIP")]
    [InlineData("gzip;q=1.0, identity;q=0.5")]
    public async Task Handle_RecognisesGZipAnywhereInTheAcceptEncodingValue(string accepted) {
        var context = await Serve(
            new OpenApiDocumentProvider(Services(), Document), Context(acceptEncoding: accepted));

        Assert.Equal(
            KnownEncoding.GZip, context.Response.Headers[KnownHeaders.ContentEncoding].ToString());
    }

    /// <summary>
    /// And only as a whole coding name. A coding that merely contains the letters is a different
    /// coding, and answering it with gzip would be answering something the client did not ask for.
    /// </summary>
    [Theory]
    [InlineData("deflate")]
    [InlineData("x-gzip")]
    [InlineData("gzip2")]
    [InlineData("identity")]
    [InlineData("")]
    public async Task Handle_DoesNotTreatAPartialTokenAsGZip(string accepted) {
        var context = await Serve(
            new OpenApiDocumentProvider(Services(), Document), Context(acceptEncoding: accepted));

        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.ContentEncoding));
        Assert.Equal(Document, Encoding.UTF8.GetString(BodyBytes(context)));
    }

    [Fact]
    public async Task Handle_SetsContentLengthToWhatWasWritten() {
        var context = await Serve(new OpenApiDocumentProvider(Services(), Document), Context());

        Assert.Equal(
            BodyBytes(context).Length.ToString(),
            context.Response.Headers[KnownHeaders.ContentLength].ToString());
    }

    [Fact]
    public async Task Handle_SendsNoCacheSoAStaleDocumentIsNotServedAfterADeploy() {
        var context = await Serve(new OpenApiDocumentProvider(Services(), Document), Context());

        Assert.Equal("no-cache", context.Response.Headers[KnownHeaders.CacheControl].ToString());
    }

    /// <summary>
    /// Serialization off, because the document already is JSON: through a serializer it would come
    /// back as a JSON-encoded string of a document.
    /// </summary>
    [Fact]
    public async Task Handle_DoesNotSerializeTheDocument() {
        var context = await Serve(new OpenApiDocumentProvider(Services(), Document), Context());

        Assert.False(context.Response.ShouldSerialize);
    }

    /// <summary>
    /// A HEAD is answered, which the routing table's HEAD-to-GET redirection does not do for a
    /// provider. Dropping the body is <c>WebExecutionHandlerService</c>'s job; being matched at all
    /// is this one's.
    /// </summary>
    [Fact]
    public void GetExecutionRequestHandler_MatchesHead() {
        var provider = new OpenApiDocumentProvider(Services(), Document);

        Assert.NotNull(provider.GetExecutionRequestHandler(Context(method: "HEAD")));
        Assert.NotNull(provider.GetExecutionRequestHandler(Context(method: "head")));
    }

    /// <summary>
    /// Another verb at this path is a 405 rather than a 404 - the resource exists, and a client and
    /// a CDN both read the difference.
    /// </summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public void GetExecutionRequestHandler_ReportsWhatIsAllowedForEveryOtherVerb(string method) {
        var provider = new OpenApiDocumentProvider(Services(), Document);

        var match = provider.GetExecutionRequestHandler(Context(method: method));

        Assert.NotNull(match);
        Assert.Null(match!.Handler);
        Assert.Equal("GET, HEAD", match.Allow);
    }

    [Fact]
    public void GetExecutionRequestHandler_IgnoresAnotherPath() {
        var provider = new OpenApiDocumentProvider(Services(), Document);

        Assert.Null(provider.GetExecutionRequestHandler(Context("/openapi.yaml")));
        Assert.Null(provider.GetExecutionRequestHandler(Context("/openapi.json/")));
    }

    /// <summary>
    /// A specification-first document keeps the type it is written in. Serving YAML as
    /// <c>application/json</c> is serving something a client cannot read.
    /// </summary>
    [Fact]
    public async Task Handle_KeepsTheContentTypeItWasGiven() {
        var provider = new OpenApiDocumentProvider(
            Services(), "openapi: 3.1.0", "/openapi.yaml", "application/yaml");

        var context = await Serve(provider, Context("/openapi.yaml"));

        Assert.Equal("application/yaml", context.Response.ContentType);
    }

    /// <summary>
    /// The generator's overload takes the document already compressed, which is the whole point of
    /// it - nothing inflates or recompresses on the way through.
    /// </summary>
    [Fact]
    public async Task Handle_ServesAPreCompressedDocumentByteForByte() {
        var compressed = Deflate(Document);

        var context = await Serve(
            new OpenApiDocumentProvider(Services(), new ReadOnlySpan<byte>(compressed)), Context());

        Assert.Equal(compressed, BodyBytes(context));
    }

    // ---------------------------------------------------- governable at last

    /// <summary>Requires a grant of everything under a path prefix.</summary>
    private sealed class PrefixConvention : IAuthorizationConvention {
        private readonly string _prefix;

        public PrefixConvention(string prefix) {
            _prefix = prefix;
        }

        public Requirement? Apply(IExecutionRequestHandlerInfo handlerInfo) =>
            handlerInfo.Path.StartsWith(_prefix, StringComparison.Ordinal)
                ? Requirement.Grant("docs:read")
                : null;
    }

    private static IExecutionRequestHandlerInfo HandlerInfoFor(
        OpenApiDocumentProvider provider, IServiceProvider services) {
        var match = provider.GetExecutionRequestHandler(
            Context(services: services));

        Assert.NotNull(match);

        return match!.Handler!.HandlerInfo;
    }

    /// <summary>
    /// A convention reaches the published document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It did not. The provider built its own one-filter chain, and <c>IGlobalFilterRegistry</c> -
    /// where <c>AuthorizationFilterProvider</c> lives - is only consulted inside
    /// <c>ExecutionHelper.CreateFilterArray</c>. So an application on default-deny, whose entire
    /// premise is that an unannotated handler is denied rather than public, published its whole API
    /// description anonymously and got no diagnostic saying so.
    /// </para>
    /// <para>
    /// The reference page at <c>/docs</c> was gate-able the whole time, because
    /// <c>OpenApiUiProvider</c> already went through the helper. Guarding the page while publishing
    /// the document is the inversion this closes.
    /// </para>
    /// </remarks>
    [Fact]
    public void AConventionReachesTheDocument() {
        var services = Services(collection =>
            collection.AddSingleton<IAuthorizationConvention>(new PrefixConvention("/openapi")));

        var handlerInfo = HandlerInfoFor(
            new OpenApiDocumentProvider(services, Document), services);

        Assert.NotNull(handlerInfo.Requirement);
        Assert.Contains("docs:read", handlerInfo.Requirement!.RequiredGrants);
    }

    /// <summary>
    /// A document that wants a policy of its own states it directly, which is what
    /// <c>IExecutionRequestHandlerInfo</c> documents as the supported way for a handler registered
    /// by hand to say what it needs.
    /// </summary>
    [Fact]
    public void ADeclaredRequirementReachesTheDocument() {
        var services = Services();

        var handlerInfo = HandlerInfoFor(
            new OpenApiDocumentProvider(
                services, Document, requirement: Requirement.Grant("docs:read")),
            services);

        Assert.Contains("docs:read", handlerInfo.Requirement!.RequiredGrants);
    }

    /// <summary>
    /// And the two conjoin rather than one replacing the other, so a convention can narrow a
    /// document that already declared something and can never open one.
    /// </summary>
    [Fact]
    public void ADeclaredRequirementAndAConventionBothApply() {
        var services = Services(collection =>
            collection.AddSingleton<IAuthorizationConvention>(new PrefixConvention("/openapi")));

        var handlerInfo = HandlerInfoFor(
            new OpenApiDocumentProvider(
                services, Document, requirement: Requirement.Grant("docs:internal")),
            services);

        var grants = handlerInfo.Requirement!.RequiredGrants.ToArray();

        Assert.Contains("docs:internal", grants);
        Assert.Contains("docs:read", grants);
    }

    /// <summary>
    /// Nothing configured leaves the document as it was: no requirement of its own, inheriting the
    /// application's posture rather than overriding it.
    /// </summary>
    [Fact]
    public void NothingConfiguredLeavesTheDocumentUnguarded() {
        var services = Services();

        Assert.Null(
            HandlerInfoFor(new OpenApiDocumentProvider(services, Document), services).Requirement);
    }

    private static byte[] Deflate(string value) {
        using var output = new MemoryStream();

        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) {
            var bytes = Encoding.UTF8.GetBytes(value);

            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }
}
