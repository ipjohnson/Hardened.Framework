using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;

namespace Hardened.Function.Lambda.Runtime.Execution
{
    public class LambdaExecutionRequest : IExecutionRequest
    {
        public LambdaExecutionRequest(string method, string path, Stream body, IHeaderCollection headers)
        {
            Method = method;
            Path = path;
            Body = body;
            Headers = headers;
        }

        public object Clone()
        {
            throw new NotImplementedException();
        }

        public string Method { get; }

        public string Path { get; }

        public string? ContentType => Headers.Get("Content-Type");

        public string? Accept => Headers.Get("Accept");

        public IExecutionRequestParameters? Parameters { get; set; }

        public Stream Body { get; set; }

        public IHeaderCollection Headers { get; }
    }
}
