﻿using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Execution;

public interface IExecutionRequest {
    IExecutionRequest Clone(
        string? method = null,
        string? path = null,
        IDictionary<string, StringValues>? headers = null,
        IQueryStringCollection? queryString = null,
        IReadOnlyList<string>? cookies = null
    );

    string Method { get; }

    string Path { get; }

    string? ContentType { get; }

    string? Accept { get; }

    IExecutionRequestParameters? Parameters { get; set; }

    Stream Body { get; set; }

    IDictionary<string, StringValues> Headers { get; }

    IQueryStringCollection QueryString { get; }

    IPathTokenCollection PathTokens { get; set; }

    IReadOnlyList<string> Cookies { get; }
}