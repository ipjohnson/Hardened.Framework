using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Aws.Lambda.Runtime.Serialization;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Aws.Lambda.Runtime.Adapters;

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
/// honest: an adapter cannot reach a serializer the application did not declare, and the two proxy
/// types are declared in <see cref="LambdaEventSerializerContext"/> rather than reflected over.
/// </para>
/// <para>
/// On its own function it is the only adapter, so <see cref="Handles"/> is never called and the
/// deserialize below is the only pass over the payload. The lookup exists for the case where a
/// function does serve several sources and something has to choose.
/// </para>
/// </remarks>
public sealed class ApiGatewayAdapter : IPayloadAdapter {
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
                        payload.Raw.Span, LambdaEventSerializerContext.Default.APIGatewayHttpApiV2ProxyRequest)
                    ?? throw new InvalidOperationException(
                        "The API Gateway adapter was given a payload that deserialized to null. " +
                        "The peek identified it as payload format 2.0 by its requestContext.http " +
                        "object, so this is a malformed event rather than a different source.");

        return new ApiGatewayRequest(proxy, RequestBody(proxy));
    }

    public IExecutionResponse CreateResponse(Stream output) => new ApiGatewayResponse(output);

    public async ValueTask WriteResponse(IExecutionContext context, Stream output) {
        var response = (ApiGatewayResponse)context.Response;

        var body = response.Body as MemoryStream
                   ?? throw new InvalidOperationException(
                       "The API Gateway adapter buffers its response, so the body has to be a " +
                       "MemoryStream it can read back. Stream mode is a different response mode, " +
                       "not a different body type here.");

        var bytes = body.ToArray();

        var proxy = new APIGatewayHttpApiV2ProxyResponse {
            // Null means "handled, no opinion" - nothing sets a status on an ordinary success path
            // - and becomes 200. It no longer means "unmatched": the not-found handler has run by
            // now and set a 404 if the routing table did not match, which it could not do while the
            // status was backed by a non-nullable int.
            StatusCode = response.Status ?? 200,
            Headers = Headers(response),
            Cookies = Cookies(response),
            IsBase64Encoded = response.IsBinary,
            Body = response.IsBinary
                ? Convert.ToBase64String(bytes)
                : Encoding.UTF8.GetString(bytes)
        };

        await JsonSerializer.SerializeAsync(
            output, proxy, LambdaEventSerializerContext.Default.APIGatewayHttpApiV2ProxyResponse);
    }

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
    private static Dictionary<string, string> Headers(IExecutionResponse response) {
        var headers = new Dictionary<string, string>();

        foreach (var header in response.Headers) {
            headers[header.Key] = header.Value.ToString();
        }

        return headers;
    }

    /// <summary>
    /// Set-Cookie strings, which payload format 2.0 carries in its own array rather than as
    /// repeated headers.
    /// </summary>
    private static string[] Cookies(ApiGatewayResponse response) {
        var cookies = response.Cookies.Cookies;

        if (cookies.Count == 0) {
            return Array.Empty<string>();
        }

        var result = new string[cookies.Count];
        var index = 0;
        var builder = new StringBuilder();

        foreach (var cookie in cookies) {
            builder.Append(cookie.Key);
            builder.Append('=');
            // Item1 is the value. Appending the tuple itself resolves to Append(object) and emits
            // its ToString(), which is how every Set-Cookie once read
            // "name=(value, CookieSetOptions { Expires = , ... })".
            builder.Append(cookie.Value.Item1);
            cookie.Value.Item2.AppendSettings(builder);

            result[index++] = builder.ToString();
            builder.Clear();
        }

        return result;
    }
}
