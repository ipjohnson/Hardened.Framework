using System.Net;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.ApplicationLoadBalancerEvents;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.Headers;
using Hardened.Requests.Runtime.QueryString;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.Http;

/// <summary>
/// Which front door sent a payload format 1.0 event, which decides the shape of the answer.
/// </summary>
public enum LambdaProxySource
{
    /// <summary>An API Gateway REST API, or an HTTP API integration set to payload format 1.0.</summary>
    ApiGateway,

    /// <summary>An Application Load Balancer target group without multi-value headers.</summary>
    LoadBalancer,

    /// <summary>An Application Load Balancer target group with multi-value headers turned on.</summary>
    LoadBalancerMultiValue,
}

/// <summary>
/// The web-shaped request, from a payload format 1.0 event: an API Gateway REST API, an HTTP API
/// integration set to 1.0, or an Application Load Balancer.
/// </summary>
/// <remarks>
/// <para>
/// A class of its own rather than a branch in <see cref="LambdaHttpRequest"/>. The two formats put
/// the method, the path, the cookies and the caller's address in different places, and the answer
/// each front door reads is a different shape, so the event is read once into the values below and
/// the adapter reads <see cref="Source"/> to choose the answer.
/// </para>
/// <para>
/// The load balancer's event is the same shape as API Gateway's 1.0 with two differences that
/// matter here: it does not percent-decode the query string, and it carries no source address in
/// its request context.
/// </para>
/// </remarks>
public sealed class LambdaProxyRequest : IExecutionRequest
{
    private readonly IDictionary<string, StringValues> _eventHeaders;
    private readonly IQueryStringCollection _eventQueryString;
    private readonly ITransportInfo _transport;
    private IHeaderCollection? _headerCollection;
    private IReadOnlyList<string>? _cookies;

    private LambdaProxyRequest(
        LambdaProxySource source,
        string method,
        string path,
        IDictionary<string, StringValues> eventHeaders,
        IQueryStringCollection eventQueryString,
        ITransportInfo transport,
        Stream body
    )
    {
        Source = source;
        Method = method;
        Path = path;
        _eventHeaders = eventHeaders;
        _eventQueryString = eventQueryString;
        _transport = transport;
        Body = body;
    }

    /// <summary>
    /// From an API Gateway event in payload format 1.0.
    /// </summary>
    /// <param name="request">The event.</param>
    /// <param name="body">The decoded body.</param>
    /// <param name="httpApi">
    /// True for an HTTP API integration set to payload format 1.0, whose <c>path</c> starts with a
    /// named stage. A REST API's <c>path</c> never carries the stage, so nothing is stripped from it:
    /// a resource that happens to begin with the stage's name is still that resource.
    /// </param>
    /// <param name="protocol">
    /// <c>requestContext.protocol</c>, which the event carries and the AWS type does not bind.
    /// </param>
    public static LambdaProxyRequest FromApiGateway(
        APIGatewayProxyRequest request,
        Stream body,
        bool httpApi = false,
        string? protocol = null
    )
    {
        var path = request.Path ?? "/";

        if (httpApi)
        {
            path = LambdaHttpRequest.StripStagePath(path, request.RequestContext?.Stage);
        }

        return new LambdaProxyRequest(
            LambdaProxySource.ApiGateway,
            request.HttpMethod ?? "GET",
            path,
            EventHeaders(request.MultiValueHeaders, request.Headers),
            // API Gateway hands both collections over already percent-decoded.
            Query(request.MultiValueQueryStringParameters, request.QueryStringParameters, false),
            new LambdaProxyTransportInfo(
                request.RequestContext?.Identity?.SourceIp,
                request.RequestContext?.DomainName,
                protocol
            ),
            body
        );
    }

    /// <summary>
    /// From an Application Load Balancer event.
    /// </summary>
    /// <remarks>
    /// The caller's address is the last entry of <c>X-Forwarded-For</c>. The load balancer appends
    /// the address it saw, and every entry before it is whatever the caller chose to send.
    /// </remarks>
    public static LambdaProxyRequest FromLoadBalancer(
        ApplicationLoadBalancerRequest request,
        Stream body
    )
    {
        var multiValue = request.MultiValueHeaders != null;
        var headers = EventHeaders(request.MultiValueHeaders, request.Headers);

        return new LambdaProxyRequest(
            multiValue ? LambdaProxySource.LoadBalancerMultiValue : LambdaProxySource.LoadBalancer,
            request.HttpMethod ?? "GET",
            request.Path ?? "/",
            headers,
            // The load balancer passes the query string on as the caller encoded it.
            Query(request.MultiValueQueryStringParameters, request.QueryStringParameters, true),
            new LambdaProxyTransportInfo(
                LastForwardedFor(headers),
                headers.TryGetValue("Host", out var host) ? host.ToString() : null,
                null,
                headers.TryGetValue("X-Forwarded-Proto", out var scheme) ? scheme.ToString() : null
            ),
            body
        );
    }

    /// <summary>Which front door sent the event, and so which answer it reads.</summary>
    public LambdaProxySource Source { get; }

    public string Method { get; }

    public string Path { get; }

    /// <remarks>
    /// Defaults to JSON when the request carried no header, as <see cref="LambdaHttpRequest"/> does.
    /// </remarks>
    public string? ContentType =>
        Headers.TryGet(KnownHeaders.ContentType, out var value) ? value : "application/json";

    public string? Accept =>
        Headers.TryGet(KnownHeaders.Accept, out var value) ? (string?)value : null;

    public IExecutionRequestParameters? Parameters { get; set; }

    public Stream Body { get; set; }

    public IHeaderCollection Headers =>
        _headerCollection ??= new HeaderCollectionStringValues(_eventHeaders);

    IDictionary<string, StringValues> IExecutionRequest.Headers => Headers;

    public IQueryStringCollection QueryString => _eventQueryString;

    public PathTokenCollection PathTokens { get; set; }

    /// <summary>
    /// The pairs of the <c>Cookie</c> header, which is where payload format 1.0 carries them.
    /// </summary>
    public IReadOnlyList<string> Cookies => _cookies ??= CookiePairs(Headers);

    public ITransportInfo Transport => _transport;

    public IExecutionRequest Clone(
        string? method = null,
        string? path = null,
        IDictionary<string, StringValues>? headers = null,
        IQueryStringCollection? queryString = null,
        IReadOnlyList<string>? cookies = null
    )
    {
        return new LambdaProxyRequest(
            Source,
            method ?? Method,
            path ?? Path,
            // Copied rather than shared, so setting a header in a fork does not write through to
            // the request it forked from.
            new Dictionary<string, StringValues>(
                headers ?? Headers,
                StringComparer.OrdinalIgnoreCase
            ),
            queryString ?? QueryString,
            // Shared: a fork is the same request from the same caller.
            _transport,
            Body
        )
        {
            _cookies = cookies ?? _cookies,
            Parameters = Parameters?.Clone(),
            PathTokens = PathTokens,
        };
    }

    /// <summary>
    /// The multi-valued collection where the event carries one, because it keeps a repeated header
    /// as separate values; otherwise the single-valued one.
    /// </summary>
    private static IDictionary<string, StringValues> EventHeaders(
        IDictionary<string, IList<string>>? multiValue,
        IDictionary<string, string>? single
    )
    {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        if (multiValue != null)
        {
            foreach (var pair in multiValue)
            {
                headers[pair.Key] = new StringValues(pair.Value?.ToArray());
            }
        }
        else if (single != null)
        {
            foreach (var pair in single)
            {
                headers[pair.Key] = pair.Value;
            }
        }

        return headers;
    }

    private static IQueryStringCollection Query(
        IDictionary<string, IList<string>>? multiValue,
        IDictionary<string, string>? single,
        bool decode
    )
    {
        var query = new Dictionary<string, StringValues>();

        string Decoded(string? value) => decode ? WebUtility.UrlDecode(value ?? "") : value ?? "";

        if (multiValue != null)
        {
            foreach (var pair in multiValue)
            {
                query[Decoded(pair.Key)] = new StringValues(pair.Value?.Select(Decoded).ToArray());
            }
        }
        else if (single != null)
        {
            foreach (var pair in single)
            {
                query[Decoded(pair.Key)] = Decoded(pair.Value);
            }
        }

        return new SimpleQueryStringCollection(query);
    }

    private static string? LastForwardedFor(IDictionary<string, StringValues> headers)
    {
        if (!headers.TryGetValue("X-Forwarded-For", out var values) || values.Count == 0)
        {
            return null;
        }

        var last = values[values.Count - 1] ?? "";
        var comma = last.LastIndexOf(',');
        var address = (comma < 0 ? last : last.Substring(comma + 1)).Trim();

        return address.Length == 0 ? null : address;
    }

    private static IReadOnlyList<string> CookiePairs(IHeaderCollection headers)
    {
        if (!headers.TryGet(KnownHeaders.Cookie, out var values))
        {
            return Array.Empty<string>();
        }

        var cookies = new List<string>();

        foreach (var value in values)
        {
            if (value == null)
            {
                continue;
            }

            foreach (var pair in value.Split(';'))
            {
                var trimmed = pair.Trim();

                if (trimmed.Length > 0)
                {
                    cookies.Add(trimmed);
                }
            }
        }

        return cookies;
    }
}
