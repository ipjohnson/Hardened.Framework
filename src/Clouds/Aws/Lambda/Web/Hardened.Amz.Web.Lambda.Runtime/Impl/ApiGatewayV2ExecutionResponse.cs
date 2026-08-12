using Amazon.Lambda.APIGatewayEvents;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Amz.Web.Lambda.Runtime.Impl;

public class ApiGatewayV2ExecutionResponse : IExecutionResponse {
    private readonly APIGatewayHttpApiV2ProxyResponse _proxyResponse;
    private HeaderCollectionStringValues? _headerCollection;

    public ApiGatewayV2ExecutionResponse(APIGatewayHttpApiV2ProxyResponse response) {
        _proxyResponse = response;
        Cookies = new ApiGatewayV2CookieSetCollection(response);
    }

    public object Clone() {
        return Clone(null);
    }

    public IExecutionResponse Clone(IHeaderCollection? headerCollection) {
        return new ApiGatewayV2ExecutionResponse(_proxyResponse) {
            ResponseValue = ResponseValue,
            TemplateName = TemplateName,
            ShouldCompress = ShouldCompress,
            IsBinary = IsBinary,
            ShouldSerialize = ShouldSerialize,
            // Carried, as LambdaExecutionResponse.Clone does. A fork whose Body did not come across
            // would write into a buffer nobody reads, and the bytes would vanish silently.
            Body = Body,
        };
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

    /// <summary>
    /// The buffer the response is written into.
    /// </summary>
    /// <remarks>
    /// Declared <c>Stream?</c> until 2026-08-11, which did not match
    /// <c>IExecutionResponse.Body</c>'s non-nullable <c>Stream</c> (CS8766). Every filter in the
    /// pipeline holds the interface, so each of them was promised a stream and could be handed
    /// null — <c>ApiGatewayEventProcessor</c> assigns a real one, but anything reaching the
    /// response before that, or through <see cref="Clone(IHeaderCollection?)"/>, was not covered by
    /// the promise. <see cref="Stream.Null"/> keeps the contract without inventing a buffer.
    /// </remarks>
    public Stream Body { get; set; } = Stream.Null;

    public IHeaderCollection Headers =>
        _headerCollection ??= new HeaderCollectionStringValues();

    IDictionary<string, StringValues> IExecutionResponse.Headers => Headers;

    public Exception? ExceptionValue { get; set; }

    public bool ResponseStarted => Body.Position > 0;

    public bool IsBinary { get; set; }

    public ICookieSetCollection Cookies { get; }

    public bool ShouldSerialize { get; set; } = true;
}