﻿using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.PathTokens;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Testing
{
    public class TestExecutionRequest : IExecutionRequest
    {
        private IPathTokenCollection? _pathTokens;

        public TestExecutionRequest(
            string method,
            string path, 
            string? accepts, IQueryStringCollection queryString)
        {
            Method = method;
            Path = path;
            Accept = accepts;
            QueryString = queryString;
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

        public IQueryStringCollection QueryString { get; }

        public IPathTokenCollection PathTokens
        {
            get => _pathTokens ?? PathTokenCollection.Empty;
            set => _pathTokens = value;
        }
    }
}