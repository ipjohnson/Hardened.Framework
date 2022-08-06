using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Testing
{
    public class TestExecutionRequest : IExecutionRequest
    {
        public TestExecutionRequest(string method, string path, string? accepts)
        {
            Method = method;
            Path = path;
            Accept = accepts;
        }

        public object Clone()
        {
            throw new NotImplementedException();
        }

        public string Method { get; }

        public string Path { get; }

        public string? ContentType => Headers.Get("Content-Type");

        public string? Accept { get; }

        public IExecutionRequestParameters? Parameters { get; set; }

        public Stream Body { get; set; }
        
        public IHeaderCollection Headers { get; set; }
    }
}
