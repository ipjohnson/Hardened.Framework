using Hardened.Requests.Abstract.Headers;

namespace Hardened.Web.Testing;

/// <summary>
/// Represents a web request
/// </summary>
public class TestWebRequest
{
    /// <summary>
    /// Headers for request
    /// </summary>
    public IHeaderCollection Headers { get; set; } = default!;
    
    /// <summary>
    /// CancellationToken for the request
    /// </summary>
    public CancellationToken? Token { get; set; }
}