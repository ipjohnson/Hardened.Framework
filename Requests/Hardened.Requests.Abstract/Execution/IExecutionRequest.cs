using Hardened.Requests.Abstract.Headers;

namespace Hardened.Requests.Abstract.Execution
{
    public interface IExecutionRequest : ICloneable
    {
        string Method { get; }

        string Path { get; }

        string? ContentType { get; }

        string? Accepts { get; }

        IExecutionRequestParameters? Parameters { get; set; }
        
        Stream Body { get; set; }

        IHeaderCollection Headers { get; }
    }
}
