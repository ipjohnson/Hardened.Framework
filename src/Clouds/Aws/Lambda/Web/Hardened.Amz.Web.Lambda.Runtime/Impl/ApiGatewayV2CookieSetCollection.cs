using Amazon.Lambda.APIGatewayEvents;
using Hardened.Requests.Abstract.Headers;

namespace Hardened.Amz.Web.Lambda.Runtime.Impl;

public class ApiGatewayV2CookieSetCollection : ICookieSetCollection {
    private readonly APIGatewayHttpApiV2ProxyResponse _response;
    private readonly Dictionary<string, Tuple<string, CookieSetOptions>> _cookies;

    public ApiGatewayV2CookieSetCollection(APIGatewayHttpApiV2ProxyResponse response) {
        _response = response;
        _cookies = new Dictionary<string, Tuple<string, CookieSetOptions>>();
    }

    /// <summary>
    /// Records a cookie for <c>ApiGatewayEventProcessor</c> to serialise into the response's
    /// <c>cookies</c> array.
    /// </summary>
    /// <remarks>
    /// Fixed 2026-08-11. This threw <see cref="NotImplementedException"/> from the first commit, so
    /// no Hardened application hosted on API Gateway could ever set a cookie, and the cookie branch
    /// of <c>ApiGatewayEventProcessor.CopyHeadersAndCookies</c> was unreachable. Same semantics as
    /// the framework's <c>CookieSetCollectionImpl</c>: last write for a name wins.
    /// </remarks>
    public void Append(string cookieName, string cookieValue, CookieSetOptions? options = null) {
        _cookies[cookieName] =
            new Tuple<string, CookieSetOptions>(cookieValue, options ?? CookieSetOptions.Empty);
    }

    public IReadOnlyDictionary<string, Tuple<string, CookieSetOptions>> Cookies => _cookies;
}
