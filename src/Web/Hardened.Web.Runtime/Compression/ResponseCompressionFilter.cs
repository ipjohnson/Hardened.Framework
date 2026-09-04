using Hardened.Requests.Abstract.Compression;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Compression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.Compression;

/// <summary>
/// Compresses the response body for a client that accepts it.
/// </summary>
/// <remarks>
/// <para>
/// One filter for every host, at <c>FilterOrder.Before + FilterOrder.ResponseCache</c>: outside
/// the response cache, so the cache stores identity bytes and encodes both a miss and a hit on the
/// way out, and inside everything that can refuse a request without reading a body. In
/// <c>Hardened.Web.Runtime</c> rather than the request runtime because only an HTTP host
/// negotiates a content coding.
/// </para>
/// <para>
/// <b>Negotiation happens here; the decision to compress happens on the first write.</b> The
/// coding is chosen from <c>Accept-Encoding</c> against the configured order, with the operation's
/// <see cref="CompressionType"/> preference tried first. Whether the body is then compressed at
/// all depends on the status and the content type, which nothing knows until something writes -
/// so that half lives in <see cref="CompressingResponseStream"/>.
/// </para>
/// <para>
/// Installed by <c>[Compress]</c> on an operation or a class, or on every handler by
/// <c>[Enable&lt;HardenedCompression&gt;]</c>. A second registration on the same handler finds the
/// body already wrapped and stands down, so a slip cannot produce two encoders.
/// </para>
/// </remarks>
public sealed class ResponseCompressionFilter : IExecutionFilter {
    private readonly ICompressionPredicate? _predicate;
    private readonly CompressionType _favor;

    /// <summary>
    /// Resolved on the first request this filter serves, for the reason the response cache
    /// resolves its store that way: there is no service provider where a filter is built.
    /// </summary>
    private ICompressionConfiguration? _configuration;

    /// <param name="predicate">
    /// The operation's own rule over the handler's return value, or null for the configured
    /// media-type rule.
    /// </param>
    /// <param name="favor">
    /// The coding to try first when the client accepts more than one.
    /// </param>
    /// <param name="configuration">
    /// Supplied by tests. Left null, it is read from the application's services on first use.
    /// </param>
    public ResponseCompressionFilter(
        ICompressionPredicate? predicate = null,
        CompressionType favor = CompressionType.Default,
        ICompressionConfiguration? configuration = null) {
        _predicate = predicate;
        _favor = favor;
        _configuration = configuration;
    }

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;
        var response = context.Response;

        if (response.Body is CompressingResponseStream) {
            await chain.Next();

            return;
        }

        var configuration = Configuration(context);
        var coding = Negotiate(context.Request.Headers, configuration, _favor);

        if (coding == null) {
            await chain.Next();

            return;
        }

        var transport = response.Body;
        var body = new CompressingResponseStream(context, transport, coding, _predicate, configuration);

        response.Body = body;

        try {
            await chain.Next();
        }
        finally {
            response.Body = transport;

            // Writes the trailer when an encoder was opened. Never closes the transport.
            await body.DisposeAsync();
        }
    }

    /// <summary>
    /// The coding to use, or null when the client accepts none the server offers or sent no
    /// <c>Accept-Encoding</c> at all.
    /// </summary>
    /// <remarks>
    /// The favoured coding has to be one the configuration lists. An operation can move a coding
    /// to the front of the order; it cannot re-enable one the application turned off.
    /// </remarks>
    public static string? Negotiate(
        IDictionary<string, StringValues> requestHeaders,
        ICompressionConfiguration configuration,
        CompressionType favor) {
        var accepted = Read(requestHeaders, KnownHeaders.AcceptEncoding);

        if (StringValues.IsNullOrEmpty(accepted)) {
            return null;
        }

        var favored = favor switch {
            CompressionType.GZip => KnownEncoding.GZip,
            CompressionType.Br => KnownEncoding.Br,
            _ => null
        };

        if (favored != null &&
            Offers(configuration, favored) &&
            AcceptEncodingHeader.Accepts(accepted, favored)) {
            return favored;
        }

        foreach (var coding in configuration.Encodings) {
            if (AcceptEncodingHeader.Accepts(accepted, coding)) {
                return coding;
            }
        }

        return null;
    }

    private static bool Offers(ICompressionConfiguration configuration, string coding) {
        foreach (var offered in configuration.Encodings) {
            if (string.Equals(offered, coding, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private ICompressionConfiguration Configuration(IExecutionContext context) {
        // Racy by construction and harmless: two requests may both resolve the same singleton.
        return _configuration ??= context.RootServiceProvider
            .GetRequiredService<IOptions<ICompressionConfiguration>>().Value;
    }

    /// <summary>
    /// A header value, looked up the way HTTP defines header names, because API Gateway delivers
    /// them lowercased and a forked request carries whatever dictionary it was handed.
    /// </summary>
    private static StringValues Read(IDictionary<string, StringValues> headers, string name) {
        if (headers.TryGetValue(name, out var value)) {
            return value;
        }

        foreach (var header in headers) {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)) {
                return header.Value;
            }
        }

        return StringValues.Empty;
    }
}
