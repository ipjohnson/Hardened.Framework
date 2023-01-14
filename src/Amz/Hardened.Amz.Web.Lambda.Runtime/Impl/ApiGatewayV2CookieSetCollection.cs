using Amazon.Lambda.APIGatewayEvents;
using Hardened.Requests.Abstract.Headers;
using System.Xml;

namespace Hardened.Amz.Web.Lambda.Runtime.Impl;

public class ApiGatewayV2CookieSetCollection : ICookieSetCollection
{
    private readonly APIGatewayHttpApiV2ProxyResponse _response;
    private Dictionary<string, Tuple<string, CookieSetOptions>> _cookies;

    public ApiGatewayV2CookieSetCollection(APIGatewayHttpApiV2ProxyResponse response)
    {
        _response = response;
        _cookies = new Dictionary<string, Tuple<string, CookieSetOptions>>();
    }

    public void Append(string cookieName, string cookieValue, CookieSetOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IReadOnlyDictionary<string, Tuple<string, CookieSetOptions>> Cookies => _cookies;
}