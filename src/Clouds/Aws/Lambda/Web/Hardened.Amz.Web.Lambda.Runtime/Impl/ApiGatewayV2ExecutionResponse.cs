using Amazon.Lambda.APIGatewayEvents;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Amz.Web.Lambda.Runtime.Impl;

public class ApiGatewayV2ExecutionResponse : IExecutionResponse {
    private readonly APIGatewayHttpApiV2ProxyResponse _proxyResponse;
    private IHeaderCollection? _headerCollection;
    private IDictionary<string, StringValues> _headers;

    public ApiGatewayV2ExecutionResponse(APIGatewayHttpApiV2ProxyResponse response) {
        _proxyResponse = response;
        Cookies = new ApiGatewayV2CookieSetCollection(response);
    }

    public object Clone() {
        throw new NotImplementedException();
    }

    public IExecutionResponse Clone(IHeaderCollection? headerCollection) {
        throw new NotImplementedException();
    }

    public string? ContentType {
        get => Headers.Get(KnownHeaders.ContentType);
        set => Headers.Set(KnownHeaders.ContentType, value);
    }

    public object? ResponseValue { get; set; }

    public string? TemplateName { get; set; }

    public int? Status {
        get => _proxyResponse.StatusCode;
        set => _proxyResponse.StatusCode = value.GetValueOrDefault(200);
    }

    public bool ShouldCompress { get; set; }

    public Stream? Body { get; set; }

    public IHeaderCollection Headers =>
        _headerCollection ??= new HeaderCollectionStringValues();

    IDictionary<string, StringValues> IExecutionResponse.Headers => _headers;

    public Exception? ExceptionValue { get; set; }

    public bool ResponseStarted => Body?.Position > 0;

    public bool IsBinary { get; set; }

    public ICookieSetCollection Cookies { get; }

    public bool ShouldSerialize { get; set; } = true;
}