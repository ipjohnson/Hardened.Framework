using Hardened.Requests.Abstract.Headers;

namespace Hardened.Web.Testing
{
    /// <summary>
    /// Represents a web request
    /// </summary>
    public class TestWebRequest
    {
        /// <summary>
        /// Headers for request
        /// </summary>
        public IHeaderCollection Headers { get; set; }
    }
}
