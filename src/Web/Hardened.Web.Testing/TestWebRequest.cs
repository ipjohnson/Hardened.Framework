using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Testing;

/// <summary>
/// Represents a web request
/// </summary>
public class TestWebRequest {
    /// <summary>
    /// Headers for request
    /// </summary>
    public IDictionary<string, StringValues> Headers { get; set; } = default!;

    /// <summary>
    /// CancellationToken for the request
    /// </summary>
    public CancellationToken? Token { get; set; }
}