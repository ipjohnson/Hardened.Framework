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
}