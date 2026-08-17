using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Headers;

public class KnownContentType {
    public const string Json = "application/json";
    public static StringValues JsonStringValues = new StringValues(Json);

    public const string Js = "text/js";
    public static StringValues JsStringValues = new StringValues(Js);

    public const string Html = "text/html";
    public static StringValues HtmlStringValues = new StringValues(Html);

    public const string Css = "text/css";
    public static StringValues CssStringValues = new StringValues(Css);

    /// <summary>
    /// Newline-delimited JSON: one document per line, separated by <c>0x0A</c>.
    /// </summary>
    /// <remarks>
    /// <c>application/jsonl</c> is the same wire format under a different name, and OpenAPI 3.2
    /// treats the two as equivalent. Only this spelling is emitted, because it is the one the
    /// streaming filter has always committed to.
    /// </remarks>
    public const string NdJson = "application/x-ndjson";
    public static StringValues NdJsonStringValues = new StringValues(NdJson);

    /// <summary>
    /// Server-sent events.
    /// </summary>
    /// <remarks>
    /// Unlike the others here, a client enforces this one: a browser <c>EventSource</c> refuses a
    /// response whose content type is anything else. That is why a stream's serializer must not
    /// restate the content type per item the way the JSON serializers do.
    /// </remarks>
    public const string EventStream = "text/event-stream";
    public static StringValues EventStreamStringValues = new StringValues(EventStream);
}