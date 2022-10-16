﻿using Amazon.Lambda.APIGatewayEvents;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Hardened.Requests.Runtime.Headers;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Requests.Runtime.QueryString;

namespace Hardened.Amz.Web.Lambda.Runtime.Impl;

internal class ApiGatewayV2ExecutionRequest : IExecutionRequest
{
    private readonly APIGatewayHttpApiV2ProxyRequest _proxyRequest;
    private IPathTokenCollection? _pathTokens;
    private IQueryStringCollection? _queryStringCollection;
    private IHeaderCollection? _headerCollection;

    public ApiGatewayV2ExecutionRequest(APIGatewayHttpApiV2ProxyRequest request)
    {
        _proxyRequest = request;
        Path = request.RawPath.Replace("/Beta", "");
    }

    public object Clone()
    {
        throw new NotImplementedException();
    }

    public string Method => _proxyRequest.RequestContext.Http.Method;

    public string Path { get; }

    public string? ContentType => "application/json";

    public string? Accept => "application/json";

    public IExecutionRequestParameters? Parameters { get; set; }

    public Stream Body { get; set; }

    public IHeaderCollection Headers =>
        _headerCollection ??= new HeaderCollectionStringDictionary(_proxyRequest.Headers);

    public IQueryStringCollection QueryString => _queryStringCollection ??=
        new SimpleQueryStringCollection(_proxyRequest.QueryStringParameters);

    public IPathTokenCollection PathTokens
    {
        get => _pathTokens ?? PathTokenCollection.Empty;
        set => _pathTokens = value;
    }
    
}