using System.Text;
using Amazon.Lambda.APIGatewayEvents;
using Hardened.Requests.Abstract.Headers;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.Primitives;

namespace Hardened.Amz.Web.Lambda.Runtime.Impl;

/// <summary>
/// The parts of an API Gateway event that both response modes read and write the same way. Kept
/// in one place so the buffered payload and the streamed prelude cannot drift apart on a header or
/// a cookie.
/// </summary>
internal static class ApiGatewayEventMapping {
    private static readonly MemoryStream EmptyBody = new(Array.Empty<byte>());

    /// <summary>
    /// The request body as a stream, decoded from base64 when the gateway says so. An absent body
    /// is a shared empty stream that nothing writes to.
    /// </summary>
    public static Stream RequestBody(APIGatewayHttpApiV2ProxyRequest request, MemoryStream memoryStream) {
        if (string.IsNullOrEmpty(request.Body)) {
            return EmptyBody;
        }

        var bytes = request.IsBase64Encoded
            ? Convert.FromBase64String(request.Body)
            : Encoding.UTF8.GetBytes(request.Body);

        memoryStream.Write(bytes, 0, bytes.Length);
        memoryStream.Position = 0;

        return memoryStream;
    }

    /// <summary>
    /// Response headers as the single-valued map both the payload and the prelude carry.
    /// </summary>
    /// <remarks>
    /// ToString() rather than the implicit StringValues conversion, which is nullable (CS8601) and
    /// would put a JSON null in the map. Multi-valued headers join on "," either way, matching
    /// IHeaderCollection.ToStringDictionary.
    /// </remarks>
    public static Dictionary<string, string> Headers(IDictionary<string, StringValues> headers) {
        var result = new Dictionary<string, string>();

        foreach (var kvp in headers) {
            result[kvp.Key] = kvp.Value.ToString();
        }

        return result;
    }

    /// <summary>
    /// Response cookies as the Set-Cookie strings payload format 2.0 carries in its cookies array.
    /// </summary>
    public static string[] Cookies(ICookieSetCollection cookieCollection, IStringBuilderPool stringBuilderPool) {
        var cookies = cookieCollection.Cookies;

        if (cookies.Count == 0) {
            return Array.Empty<string>();
        }

        using var stringBuilderReservation = stringBuilderPool.Get();
        var stringBuilder = stringBuilderReservation.Item;
        var cookieArray = new string[cookies.Count];
        var i = 0;

        foreach (var cookiePair in cookies) {
            stringBuilder.Append(cookiePair.Key);
            stringBuilder.Append('=');
            // Item1 - the value. Appending the Tuple itself, which is what this did until
            // 2026-08-11, resolves to StringBuilder.Append(object) and emits the tuple's
            // ToString(), so every Set-Cookie read
            // "name=(value, CookieSetOptions { Expires = , ... })".
            stringBuilder.Append(cookiePair.Value.Item1);
            cookiePair.Value.Item2.AppendSettings(stringBuilder);
            cookieArray[i] = stringBuilder.ToString();
            i++;
            stringBuilder.Clear();
        }

        return cookieArray;
    }
}
