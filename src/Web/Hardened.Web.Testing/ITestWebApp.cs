using Hardened.Shared.Testing;

namespace Hardened.Web.Testing;

public interface ITestWebApp : ITestContext {
    IServiceProvider RootServiceProvider { get; }

    /// <summary>
    /// Get method
    /// </summary>
    /// <param name="path">
    /// The request path, percent-encoded as a client would send it. It is decoded before the
    /// pipeline sees it, the way a transport decodes one - so <c>"/events/%20"</c> reaches the
    /// handler as a space, and <c>%2F</c> alone stays as written because decoding it would put a
    /// separator inside a segment.
    /// </param>
    /// <param name="webRequest">web request configuration</param>
    /// <returns></returns>
    Task<TestWebResponse> Get(string path, Action<TestWebRequest>? webRequest = null);

    /// <summary>
    /// Post value to path
    /// </summary>
    /// <param name="value">
    /// The body. A <c>string</c> or a <c>byte[]</c> goes on the wire as itself; anything else is
    /// serialized as JSON. That is how a malformed body is sent - <c>Post("{", path)</c> is a
    /// request the deserializer refuses - and how a body that is not text at all is.
    /// </param>
    /// <param name="path">
    /// The request path, percent-encoded as a client would send it. It is decoded before the
    /// pipeline sees it, the way a transport decodes one.
    /// </param>
    /// <param name="webRequest">Headers and cancellation for the request.</param>
    /// <returns></returns>
    Task<TestWebResponse> Post(object value, string path, Action<TestWebRequest>? webRequest = null);

    /// <summary>
    /// Put value to path
    /// </summary>
    /// <param name="value"></param>
    /// <param name="path"></param>
    /// <param name="webRequest"></param>
    /// <returns></returns>
    Task<TestWebResponse> Put(object value, string path, Action<TestWebRequest>? webRequest = null);

    /// <summary>
    /// Patch value to path
    /// </summary>
    /// <param name="value"></param>
    /// <param name="path"></param>
    /// <param name="webRequest"></param>
    /// <returns></returns>
    Task<TestWebResponse> Patch(object value, string path, Action<TestWebRequest>? webRequest = null);

    /// <summary>
    /// Delete path
    /// </summary>
    /// <param name="path"></param>
    /// <param name="webRequest"></param>
    /// <returns></returns>
    Task<TestWebResponse> Delete(string path, Action<TestWebRequest>? webRequest = null);

    /// <summary>
    /// Send HTTP request
    /// </summary>
    /// <param name="method"></param>
    /// <param name="value"></param>
    /// <param name="path"></param>
    /// <param name="webRequest"></param>
    /// <returns></returns>
    Task<TestWebResponse> Request(string method, object? value, string path, Action<TestWebRequest>? webRequest = null);
}