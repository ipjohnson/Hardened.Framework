using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;

namespace Hardened.Requests.Testing
{
    public class TestExecutionRequest : IExecutionRequest
    {
        public TestExecutionRequest(string method, string path, string? contentType, string? accepts)
        {
            Method = method;
            Path = path;
            ContentType = contentType;
            Accepts = accepts;
        }

        public object Clone()
        {
            throw new NotImplementedException();
        }

        public string Method { get; }

        public string Path { get; }

        public string? ContentType { get; }

        public string? Accepts { get; }

        public IExecutionRequestParameters? Parameters { get; set; }

        public Stream Body { get; set; }
        
        public IHeaderCollection Headers { get; }
    }
}
