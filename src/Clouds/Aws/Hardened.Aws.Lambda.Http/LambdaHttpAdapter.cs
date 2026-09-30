using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Core.ResponseStreaming;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Responses;

namespace Hardened.Aws.Lambda.Http;

/// <summary>
/// The web-shaped front doors: API Gateway payload format 2.0, which is also what a function URL
/// delivers, and payload format 1.0, which a REST API and an Application Load Balancer send.
/// </summary>
/// <remarks>
/// <para>
/// Web-shaped: the handler sees a path, a query string, headers, cookies and a body, and answers
/// with a status. A throw is answered with a 500 rather than rethrown, because the caller is on the
/// other end of an HTTP connection and a failed invocation would give them a 502 with nothing in it.
/// </para>
/// <para>
/// It brings its own <c>JsonTypeInfo</c>, per D3. That is what keeps ahead-of-time publishing
/// honest: an adapter cannot reach a serializer the application did not declare, and the proxy
/// request is declared in <see cref="LambdaHttpSerializerContext"/> rather than reflected over.
/// Only the request needs one - the response is written field by field.
/// </para>
/// <para>
/// On its own function it is the only adapter, so <see cref="Handles"/> is never called and the
/// deserialize below is the only pass over the payload. The lookup exists for the case where a
/// function does serve several sources and something has to choose.
/// </para>
/// </remarks>
public sealed class LambdaHttpAdapter : IStreamingPayloadAdapter
{
    /// <summary>
    /// The object every front door's event carries, and the whole of how a web event is told from a
    /// caller's own payload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Format 2.0 has an <c>http</c> object inside it. Format 1.0 has none, and puts the method at
    /// <c>httpMethod</c> on the root instead. An ALB is the same shape as 1.0 and also carries
    /// <c>requestContext.elb</c>.
    /// </para>
    /// <para>
    /// The formats also answer differently. 2.0 reads Set-Cookie out of a <c>cookies</c> array, 1.0
    /// out of <c>multiValueHeaders</c>, and an ALB wants a <c>statusDescription</c> as well, so the
    /// request remembers which front door sent it and <see cref="WriteResponse"/> reads that.
    /// </para>
    /// </remarks>
    private const string RequestContext = "requestContext";

    private const string Http = "http";

    private const string HttpMethod = "httpMethod";

    private const string Elb = "elb";

    /// <remarks>
    /// A direct property of <c>requestContext</c>, so a caller's own payload carrying an <c>http</c>
    /// object somewhere further down inside its own context is not a match. Reading the parsed
    /// document is what makes that free: the byte-level peek this replaced had to track depth by
    /// hand to say the same thing, and got it wrong.
    /// </remarks>
    public bool Handles(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(RequestContext, out var context)
        && context.ValueKind == JsonValueKind.Object
        && (
            (context.TryGetProperty(Http, out var http) && http.ValueKind == JsonValueKind.Object)
            || (
                payload.TryGetProperty(HttpMethod, out var method)
                && method.ValueKind == JsonValueKind.String
            )
        );

    /// <remarks>
    /// <para>
    /// From <see cref="LambdaPayload.Raw"/> rather than from the element the peek read.
    /// <c>JsonElement.Deserialize</c> does not read an element in place - it writes the element back
    /// out to a pooled buffer and parses that - so binding from the bytes skips a whole re-encode.
    /// It also leaves the proxy request owning its own strings, so nothing here outlives the
    /// document.
    /// </para>
    /// <para>
    /// Bound as 2.0 first, because a function URL and an HTTP API send 2.0 and that bind is then
    /// the only pass over the payload. An event that binds without <c>requestContext.http</c>, or
    /// that does not bind as 2.0 at all, is read again as 1.0. That second pass is paid only by a
    /// 1.0 front door.
    /// </para>
    /// </remarks>
    public IExecutionRequest CreateRequest(LambdaPayload payload, ILambdaContext context)
    {
        APIGatewayHttpApiV2ProxyRequest? proxy;

        try
        {
            proxy = JsonSerializer.Deserialize(
                payload.Raw.Span,
                LambdaHttpSerializerContext.Default.APIGatewayHttpApiV2ProxyRequest
            );
        }
        catch (JsonException)
        {
            // A 1.0 authorizer context is a free-form map, which need not bind as 2.0's.
            proxy = null;
        }

        if (proxy?.RequestContext?.Http != null)
        {
            return new LambdaHttpRequest(proxy, RequestBody(proxy.Body, proxy.IsBase64Encoded));
        }

        return FormatOne(payload);
    }

    /// <summary>
    /// A payload format 1.0 event, from an API Gateway REST API, an HTTP API integration set to
    /// 1.0, or an Application Load Balancer.
    /// </summary>
    private static IExecutionRequest FormatOne(LambdaPayload payload)
    {
        var root = payload.Json;

        if (
            root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(HttpMethod, out var method)
            || method.ValueKind != JsonValueKind.String
        )
        {
            throw new InvalidOperationException(
                "The Lambda HTTP adapter was given an event with neither requestContext.http "
                    + "(payload format 2.0) nor httpMethod (payload format 1.0). It serves an API "
                    + "Gateway HTTP API or REST API, a function URL, or an Application Load "
                    + "Balancer, and this event came from none of them."
            );
        }

        if (
            root.TryGetProperty(RequestContext, out var requestContext)
            && requestContext.ValueKind == JsonValueKind.Object
            && requestContext.TryGetProperty(Elb, out _)
        )
        {
            var balancer = JsonSerializer.Deserialize(
                payload.Raw.Span,
                LambdaProxySerializerContext.Default.ApplicationLoadBalancerRequest
            )!;

            return LambdaProxyRequest.FromLoadBalancer(
                balancer,
                RequestBody(balancer.Body, balancer.IsBase64Encoded)
            );
        }

        var gateway = JsonSerializer.Deserialize(
            payload.Raw.Span,
            LambdaProxySerializerContext.Default.APIGatewayProxyRequest
        )!;

        // Only an HTTP API integration set to 1.0 states the version. Its path carries the stage,
        // where a REST API's does not.
        var httpApi =
            root.TryGetProperty("version", out var version)
            && version.ValueKind == JsonValueKind.String
            && version.GetString() == "1.0";

        var protocol =
            requestContext.ValueKind == JsonValueKind.Object
            && requestContext.TryGetProperty("protocol", out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        return LambdaProxyRequest.FromApiGateway(
            gateway,
            RequestBody(gateway.Body, gateway.IsBase64Encoded),
            httpApi,
            protocol
        );
    }

    /// <summary>
    /// Answered rather than rethrown. A failed invocation gives the caller a 502 with nothing in it,
    /// where answering gives them the status and body the application chose.
    /// </summary>
    public HostFailurePolicy FailurePolicy => HostFailurePolicy.Answer500;

    public IExecutionResponse CreateResponse(Stream output) => new LambdaHttpResponse(output);

    /// <summary>
    /// The same status, headers and cookies <see cref="WriteResponse"/> would have written, sent as
    /// the prelude that opens the stream instead of as an envelope around a finished body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Headers</c> rather than <c>MultiValueHeaders</c>: a function URL reads the single-valued
    /// collection, and it is the deployment shape that streams. A multi-valued header joins on ","
    /// here exactly as it does in the buffered envelope, so the two modes put the same bytes on the
    /// wire.
    /// </para>
    /// <para>
    /// Null and zero both become 200, for the reason the envelope gives: null is "handled, no
    /// opinion", and zero is not a status a handler can have meant.
    /// </para>
    /// </remarks>
    public HttpResponseStreamPrelude CreatePrelude(IExecutionResponse response)
    {
        var prelude = new HttpResponseStreamPrelude
        {
            StatusCode = (HttpStatusCode)(
                response.Status is null or 0 ? 200 : response.Status.Value
            ),
        };

        foreach (var header in response.Headers)
        {
            prelude.Headers[header.Key] = header.Value.ToString();
        }

        foreach (var cookie in SetCookies((LambdaHttpResponse)response))
        {
            prelude.Cookies.Add(cookie);
        }

        return prelude;
    }

    /// <remarks>
    /// <para>
    /// <b>Written rather than serialized, because the DTO would cost a copy of the whole body.</b>
    /// <c>APIGatewayHttpApiV2ProxyResponse.Body</c> is a <c>string</c>, so binding one turns a
    /// six-megabyte response into a twelve-megabyte UTF-16 string that the serializer then escapes
    /// straight back to UTF-8. The body is already UTF-8 bytes in a buffer, and the writer wants
    /// UTF-8 bytes, so nothing in between is needed.
    /// </para>
    /// <para>
    /// The request half still binds the AWS type. The asymmetry is the point: reading a request
    /// means pulling named fields out of a shape someone else defined, which is what a DTO is good
    /// at, while writing a response means emitting five fields we chose, where the DTO only adds a
    /// round trip.
    /// </para>
    /// </remarks>
    public async ValueTask WriteResponse(IExecutionContext context, Stream output)
    {
        var response = (LambdaHttpResponse)context.Response;

        var body =
            response.Body as MemoryStream
            ?? throw new InvalidOperationException(
                "The Lambda HTTP adapter buffers its response, so the body has to be a "
                    + "MemoryStream it can read back. Stream mode is a different response mode, "
                    + "not a different body type here."
            );

        await using var writer = new Utf8JsonWriter(output);

        if (context.Request is LambdaProxyRequest proxy)
        {
            WriteFormatOne(writer, response, body, proxy.Source);
        }
        else
        {
            Write(writer, response, body);
        }

        await writer.FlushAsync();
    }

    /// <summary>
    /// The whole payload, written synchronously into the writer's buffer.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="WriteResponse"/> only because an async method cannot hold a
    /// <see cref="ReadOnlySpan{T}"/> local, and reading the body without copying it is the point of
    /// writing the response by hand.
    /// </remarks>
    private static void Write(Utf8JsonWriter writer, LambdaHttpResponse response, MemoryStream body)
    {
        writer.WriteStartObject();

        // Null means "handled, no opinion" - nothing sets a status on an ordinary success path -
        // and becomes 200. It no longer means "unmatched": the not-found handler has run by now and
        // set a 404 if the routing table did not match, which it could not do while the status was
        // backed by a non-nullable int.
        writer.WriteNumber("statusCode", response.Status ?? 200);

        WriteHeaders(writer, response);
        WriteCookies(writer, response);

        writer.WriteBoolean("isBase64Encoded", response.IsBinary);

        var bytes = Written(body);

        if (response.IsBinary)
        {
            writer.WriteBase64String("body", bytes);
        }
        else
        {
            // Already UTF-8, and the writer wants UTF-8, so this escapes in place.
            writer.WriteString("body", bytes);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// The payload format 1.0 answer, for a REST API or an ALB.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set-Cookie goes in as a header, because 1.0 has no <c>cookies</c> array. In
    /// <c>multiValueHeaders</c> each cookie is its own value, which is the only way two of them
    /// arrive as two headers.
    /// </para>
    /// <para>
    /// An ALB reads <c>headers</c> or <c>multiValueHeaders</c> according to the target group's
    /// setting, and the event says which by the collection it carried. With single-valued headers
    /// there is room for one Set-Cookie, and it is the last one the response set. API Gateway reads
    /// <c>multiValueHeaders</c> whatever the request carried.
    /// </para>
    /// </remarks>
    private static void WriteFormatOne(
        Utf8JsonWriter writer,
        LambdaHttpResponse response,
        MemoryStream body,
        LambdaProxySource source
    )
    {
        writer.WriteStartObject();

        var status = response.Status ?? 200;

        writer.WriteNumber("statusCode", status);

        if (source != LambdaProxySource.ApiGateway)
        {
            var phrase = HttpStatusText.ReasonPhrase(status);

            writer.WriteString(
                "statusDescription",
                phrase == null ? status.ToString() : status + " " + phrase
            );
        }

        if (source == LambdaProxySource.LoadBalancer)
        {
            WriteHeaders(writer, response, SetCookies(response).LastOrDefault());
        }
        else
        {
            WriteMultiValueHeaders(writer, response);
        }

        writer.WriteBoolean("isBase64Encoded", response.IsBinary);

        var bytes = Written(body);

        if (response.IsBinary)
        {
            writer.WriteBase64String("body", bytes);
        }
        else
        {
            writer.WriteString("body", bytes);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// What the chain wrote, as the buffer itself where the stream will lend it.
    /// </summary>
    /// <remarks>
    /// <c>TryGetBuffer</c> rather than <c>GetBuffer</c>, which throws on a stream built over an
    /// array it does not own. A response body is a stream the host made and so ordinarily lends its
    /// buffer; a filter that swapped in its own does not, and that costs a copy rather than a
    /// failure.
    /// </remarks>
    private static ReadOnlySpan<byte> Written(MemoryStream body) =>
        body.TryGetBuffer(out var buffer) ? buffer.AsSpan() : body.ToArray();

    /// <summary>
    /// The request body as a stream, decoded from base64 when the event says so.
    /// </summary>
    private static Stream RequestBody(string? body, bool base64)
    {
        if (string.IsNullOrEmpty(body))
        {
            return Stream.Null;
        }

        var bytes = base64 ? Convert.FromBase64String(body) : Encoding.UTF8.GetBytes(body);

        return new MemoryStream(bytes, writable: false);
    }

    /// <remarks>
    /// <c>ToString()</c> rather than the implicit <c>StringValues</c> conversion, which is nullable
    /// and would put a JSON null in the map. A multi-valued header joins on "," either way.
    /// </remarks>
    private static void WriteHeaders(
        Utf8JsonWriter writer,
        IExecutionResponse response,
        string? setCookie = null
    )
    {
        writer.WriteStartObject("headers");

        foreach (var header in response.Headers)
        {
            writer.WriteString(header.Key, header.Value.ToString());
        }

        if (setCookie != null)
        {
            writer.WriteString(KnownHeaders.SetCookie, setCookie);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Every header as an array of its values, with each Set-Cookie string as one value.
    /// </summary>
    private static void WriteMultiValueHeaders(Utf8JsonWriter writer, LambdaHttpResponse response)
    {
        writer.WriteStartObject("multiValueHeaders");

        foreach (var header in response.Headers)
        {
            writer.WriteStartArray(header.Key);

            foreach (var value in header.Value)
            {
                writer.WriteStringValue(value);
            }

            writer.WriteEndArray();
        }

        var cookies = false;

        foreach (var cookie in SetCookies(response))
        {
            if (!cookies)
            {
                writer.WriteStartArray(KnownHeaders.SetCookie);
                cookies = true;
            }

            writer.WriteStringValue(cookie);
        }

        if (cookies)
        {
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Set-Cookie strings, which payload format 2.0 carries in its own array rather than as
    /// repeated headers.
    /// </summary>
    private static void WriteCookies(Utf8JsonWriter writer, LambdaHttpResponse response)
    {
        writer.WriteStartArray("cookies");

        foreach (var cookie in SetCookies(response))
        {
            writer.WriteStringValue(cookie);
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// The response's cookies as Set-Cookie strings.
    /// </summary>
    /// <remarks>
    /// Shared by the envelope and the prelude, so a cookie cannot be rendered one way buffered and
    /// another way streamed. <c>Item1</c> is the value: appending the tuple itself resolves to
    /// <c>Append(object)</c> and emits its <c>ToString()</c>, which is how every Set-Cookie once
    /// read "name=(value, CookieSetOptions { Expires = , ... })".
    /// </remarks>
    private static IEnumerable<string> SetCookies(LambdaHttpResponse response)
    {
        var builder = new StringBuilder();

        foreach (var cookie in response.Cookies.Cookies)
        {
            builder.Append(cookie.Key);
            builder.Append('=');
            builder.Append(cookie.Value.Item1);
            cookie.Value.Item2.AppendSettings(builder);

            yield return builder.ToString();

            builder.Clear();
        }
    }
}
