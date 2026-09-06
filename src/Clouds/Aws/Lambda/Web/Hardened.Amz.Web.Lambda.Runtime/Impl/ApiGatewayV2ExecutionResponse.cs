using Amazon.Lambda.APIGatewayEvents;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Requests.Runtime.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Amz.Web.Lambda.Runtime.Impl;

public class ApiGatewayV2ExecutionResponse : IExecutionResponse {
    private readonly APIGatewayHttpApiV2ProxyResponse _proxyResponse;
    private HeaderCollectionStringValues? _headerCollection;
    private int? _status;

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
            OutputFactory = OutputFactory,
            Output = Output,
            IsBinary = IsBinary,
            ShouldSerialize = ShouldSerialize,
            // Carried, as LambdaExecutionResponse.Clone does. A fork whose Body did not come across
            // would write into a buffer nobody reads, and the bytes would vanish silently.
            Body = Body,
            // Copied rather than shared, matching LambdaExecutionResponse and the framework's
            // FeatureExecutionResponse: a clone starts where the original stands and diverges from
            // there. It used to be shared, but only because the getter read straight off the proxy
            // response — the accident that cost this transport its 404s.
            Status = Status,
        };
    }

    public string? ContentType {
        get => Headers.Get(KnownHeaders.ContentType);
        set => Headers.Set(KnownHeaders.ContentType, value);
    }

    public object? ResponseValue { get; set; }

    /// <summary>
    /// Built from <see cref="OutputFactory"/> on first use and kept, because it is asked whether it
    /// answers the request before it is asked to write.
    /// </summary>
    /// <remarks>
    /// Replaces <c>TemplateName</c>, which named a view by string and is gone: a view is a type
    /// now, and setting an output takes the response out of negotiation rather than feeding a
    /// name-based lookup.
    /// </remarks>
    public IHardenedResponseOutput? Output { get; set; }

    /// <summary>
    /// Builds what writes this response, or null when it is serialized like any other. A factory
    /// rather than an instance so nothing is allocated for a response that is never written.
    /// </summary>
    public Func<IExecutionContext, IHardenedResponseOutput>? OutputFactory { get; set; }

    /// <summary>
    /// Null while the status is still undecided; otherwise what will be sent.
    ///
    /// Reading it back off the proxy response looks equivalent and is not.
    /// <see cref="APIGatewayHttpApiV2ProxyResponse.StatusCode"/> is a non-nullable <c>int</c>
    /// starting at 0, so widening it to <c>int?</c> can never produce null — and
    /// <c>ResourceNotFoundHandler</c> supplies a 404 only when it finds the status still unset.
    /// It therefore never fired on this transport: an unmatched route left the status at 0, which
    /// <see cref="ApiGatewayEventProcessor"/> normalises to 200, so every path the routing table
    /// did not match came back as an empty 200.
    ///
    /// The streaming transport, which backs the status with its own field, has always returned
    /// 404 here. The two hosts disagreed on the same application until this was fixed on
    /// 2026-08-15; <c>FeatureExecutionResponse</c> and <c>AspNetExecutionContext</c> in the
    /// framework carry the same field for the same reason.
    /// </summary>
    public int? Status {
        get => _status;
        set {
            _status = value;
            _proxyResponse.StatusCode = value.GetValueOrDefault(200);
        }
    }

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