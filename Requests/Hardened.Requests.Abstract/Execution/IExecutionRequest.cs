using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;

namespace Hardened.Requests.Abstract.Execution
{
    public interface IExecutionRequest : ICloneable
    {
        string Method { get; }

        string Path { get; }

        string? ContentType { get; }

        string? Accept { get; }

        IExecutionRequestParameters? Parameters { get; set; }
        
        Stream Body { get; set; }
        
        IHeaderCollection Headers { get; }

        IQueryStringCollection QueryString { get; }

        IPathTokenCollection PathTokens { get; set; }
    }
}
