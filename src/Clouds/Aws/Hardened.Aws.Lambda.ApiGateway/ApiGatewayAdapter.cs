using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Core.ResponseStreaming;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Aws.Lambda.ApiGateway;

/// <summary>
/// API Gateway payload format 2.0, which is also what a function URL and an ALB deliver.
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
/// request is declared in <see cref="ApiGatewaySerializerContext"/> rather than reflected over.
/// Only the request needs one - the response is written field by field.
/// </para>
/// <para>
/// On its own function it is the only adapter, so <see cref="Handles"/> is never called and the
/// deserialize below is the only pass over the payload. The lookup exists for the case where a
/// function does serve several sources and something has to choose.
/// </para>
/// </remarks>
public sealed class ApiGatewayAdapter : IStreamingPayloadAdapter {
    /// <summary>
    /// The field that says this is payload format 2.0, and the whole of how it is told from 1.0.
    /// </summary>
    /// <remarks>
    /// Format 1.0 puts the method at <c>httpMethod</c> on the root and its <c>requestContext</c>
    /// carries no <c>http</c> object at all. That is not a hypothetical difference: selecting
    /// format 1.0 used to be accepted and ignored, the generator emitted a v2 handler anyway, and
    /// the function failed in production on a null <c>RequestContext.Http</c>.
    /// </remarks>
    private const string RequestContext = "requestContext";

    private const string Http = "http";

    /// <remarks>
    /// A direct property of <c>requestContext</c>, so a caller's own payload carrying an <c>http</c>
    /// object somewhere further down inside its own context is not a match. Reading the parsed
    /// document is what makes that free: the byte-level peek this replaced had to track depth by
    /// hand to say the same thing, and got it wrong.
    /// </remarks>
    public bool Handles(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object &&
        payload.TryGetProperty(RequestContext, out var context) &&
        context.ValueKind == JsonValueKind.Object &&
        context.TryGetProperty(Http, out var http) &&
        http.ValueKind == JsonValueKind.Object;

    /// <remarks>
    /// From <see cref="LambdaPayload.Raw"/> rather than from the element the peek read.
    /// <c>JsonElement.Deserialize</c> does not read an element in place - it writes the element back
    /// out to a pooled buffer and parses that - so binding from the bytes skips a whole re-encode.
    /// It also leaves the proxy request owning its own strings, so nothing here outlives the
    /// document.
    /// </remarks>
    public IExecutionRequest CreateRequest(LambdaPayload payload, ILambdaContext context) {
        var proxy = JsonSerializer.Deserialize(
                        payload.Raw.Span, ApiGatewaySerializerContext.Default.APIGatewayHttpApiV2ProxyRequest)
                    ?? throw new InvalidOperationException(
                        "The API Gateway adapter was given a payload that deserialized to null. " +
                        "The peek identified it as payload format 2.0 by its requestContext.http " +
                        "object, so this is a malformed event rather than a different source.");

        return new ApiGatewayRequest(proxy, RequestBody(proxy));
    }

    /// <summary>
    /// Answered rather than rethrown. A failed invocation gives the caller a 502 with nothing in it,
    /// where answering gives them the status and body the application chose.
    /// </summary>
    public HostFailurePolicy FailurePolicy => HostFailurePolicy.Answer500;

    public IExecutionResponse CreateResponse(Stream output) => new ApiGatewayResponse(output);

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
    public HttpResponseStreamPrelude CreatePrelude(IExecutionResponse response) {
        var prelude = new HttpResponseStreamPrelude {
            StatusCode = (HttpStatusCode)(response.Status is null or 0 ? 200 : response.Status.Value)
        };

        foreach (var header in response.Headers) {
            prelude.Headers[header.Key] = header.Value.ToString();
        }

        foreach (var cookie in SetCookies((ApiGatewayResponse)response)) {
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
    public async ValueTask WriteResponse(IExecutionContext context, Stream output) {
        var response = (ApiGatewayResponse)context.Response;

        var body = response.Body as MemoryStream
                   ?? throw new InvalidOperationException(
                       "The API Gateway adapter buffers its response, so the body has to be a " +
                       "MemoryStream it can read back. Stream mode is a different response mode, " +
                       "not a different body type here.");

        await using var writer = new Utf8JsonWriter(output);

        Write(writer, response, body);

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
    private static void Write(Utf8JsonWriter writer, ApiGatewayResponse response, MemoryStream body) {
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

        if (response.IsBinary) {
            writer.WriteBase64String("body", bytes);
        }
        else {
            // Already UTF-8, and the writer wants UTF-8, so this escapes in place.
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
    /// The request body as a stream, decoded from base64 when the gateway says so.
    /// </summary>
    private static Stream RequestBody(APIGatewayHttpApiV2ProxyRequest request) {
        if (string.IsNullOrEmpty(request.Body)) {
            return Stream.Null;
        }

        var bytes = request.IsBase64Encoded
            ? Convert.FromBase64String(request.Body)
            : Encoding.UTF8.GetBytes(request.Body);

        return new MemoryStream(bytes, writable: false);
    }

    /// <remarks>
    /// <c>ToString()</c> rather than the implicit <c>StringValues</c> conversion, which is nullable
    /// and would put a JSON null in the map. A multi-valued header joins on "," either way.
    /// </remarks>
    private static void WriteHeaders(Utf8JsonWriter writer, IExecutionResponse response) {
        writer.WriteStartObject("headers");

        foreach (var header in response.Headers) {
            writer.WriteString(header.Key, header.Value.ToString());
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Set-Cookie strings, which payload format 2.0 carries in its own array rather than as
    /// repeated headers.
    /// </summary>
    private static void WriteCookies(Utf8JsonWriter writer, ApiGatewayResponse response) {
        writer.WriteStartArray("cookies");

        foreach (var cookie in SetCookies(response)) {
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
    private static IEnumerable<string> SetCookies(ApiGatewayResponse response) {
        var builder = new StringBuilder();

        foreach (var cookie in response.Cookies.Cookies) {
            builder.Append(cookie.Key);
            builder.Append('=');
            builder.Append(cookie.Value.Item1);
            cookie.Value.Item2.AppendSettings(builder);

            yield return builder.ToString();

            builder.Clear();
        }
    }
}
