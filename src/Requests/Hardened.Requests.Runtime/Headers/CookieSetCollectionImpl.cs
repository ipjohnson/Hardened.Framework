using Hardened.Requests.Abstract.Headers;

namespace Hardened.Requests.Runtime.Headers;

public class CookieSetCollectionImpl : ICookieSetCollection {
    private readonly Dictionary<string, Tuple<string, CookieSetOptions>> _cookies = new();

    public void Append(string cookieName, string cookieValue, CookieSetOptions? options = null) {
        _cookies[cookieName] =
            new Tuple<string, CookieSetOptions>(cookieValue, options ?? CookieSetOptions.Empty);
    }

    public IReadOnlyDictionary<string, Tuple<string, CookieSetOptions>> Cookies => _cookies;
}