using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Templates;
using Hardened.Requests.Runtime.Headers;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Kestrel.Runtime.Impl;

/// <summary>
/// An execution response backed directly by the server's response features.
///
/// Status and headers write through to <see cref="IHttpResponseFeature"/>, so Kestrel sees them
/// without an intervening <c>HttpResponse</c>. The body comes from
/// <see cref="IHttpResponseBodyFeature"/>, which is what Kestrel wants written to and what it
/// needs completed at the end of the request.
/// </summary>
public sealed class FeatureExecutionResponse : IExecutionResponse {
    private readonly IHttpResponseFeature _feature;
    private readonly IHttpResponseBodyFeature _bodyFeature;
    private Stream? _bodyOverride;
    private int? _status;

    /// <summary>
    /// Created on first use. Most responses set no cookie, so this stays null and the per-request
    /// cost is a null check rather than a dictionary.
    /// </summary>
    private ICookieSetCollection? _cookies;

    public FeatureExecutionResponse(
        IHttpResponseFeature feature,
        IHttpResponseBodyFeature bodyFeature) {
        _feature = feature;
        _bodyFeature = bodyFeature;
    }

    private FeatureExecutionResponse(
        IHttpResponseFeature feature,
        IHttpResponseBodyFeature bodyFeature,
        Stream? bodyOverride,
        int? status,
        ICookieSetCollection cookies) {
        _feature = feature;
        _bodyFeature = bodyFeature;
        _bodyOverride = bodyOverride;
        _status = status;
        _cookies = cookies;
    }

    public IExecutionResponse Clone(IHeaderCollection? headerCollection = null) {
        return new FeatureExecutionResponse(
            _feature, _bodyFeature, _bodyOverride, _status, Cookies) {
            ResponseValue = ResponseValue,
            TemplateFactory = TemplateFactory,
            Template = Template,
            ShouldCompress = ShouldCompress,
            IsBinary = IsBinary,
            ShouldSerialize = ShouldSerialize
        };
    }

    public string? ContentType {
        get => _feature.Headers.ContentType;
        set => _feature.Headers.ContentType = value;
    }

    public object? ResponseValue { get; set; }

    public Func<IExecutionContext, IHardenedTemplate>? TemplateFactory { get; set; }

    public IHardenedTemplate? Template { get; set; }

    /// <summary>
    /// Null while the status is still undecided; otherwise what will be, or has been, sent.
    ///
    /// Reading the status back off the feature unconditionally would look equivalent and quietly
    /// break 404 handling: <c>ResourceNotFoundHandler</c> only supplies a 404 when it finds the
    /// status still unset, and a server's response feature starts life at 200 rather than at
    /// nothing, so an unmatched route would return an empty 200.
    ///
    /// The <c>HasStarted</c> fallback covers the other end of the request. Nothing sets a status
    /// on an ordinary success path, so a plain nullable field would still read null at
    /// <c>RequestEnd</c> and log a blank status for every successful request. Once the response
    /// has started the status is settled, so reporting it is accurate rather than a guess — and
    /// every filter that tests for null runs earlier than that, before anything is written.
    /// </summary>
    public int? Status {
        get => _status ?? (_feature.HasStarted ? _feature.StatusCode : null);
        set {
            _status = value;
            _feature.StatusCode = value ?? 200;
        }
    }

    public bool ShouldCompress { get; set; }

    /// <summary>
    /// Defaults to the stream the server supplies. A filter that swaps the stream — the
    /// compression filter does — writes to the override, so reads and writes stay consistent
    /// rather than one of them going back through the feature.
    /// </summary>
    public Stream Body {
        get => _bodyOverride ?? _bodyFeature.Stream;
        set => _bodyOverride = value;
    }

    public IDictionary<string, StringValues> Headers => _feature.Headers;

    public Exception? ExceptionValue { get; set; }

    /// <summary>
    /// True once the server has flushed the response line and headers. Kestrel sets this on the
    /// first body write, which is what the error path relies on to decide whether a status code
    /// can still be changed.
    /// </summary>
    public bool ResponseStarted => _feature.HasStarted;

    public bool IsBinary { get; set; }

    /// <summary>
    /// Header backed: over HTTP a cookie reaches the client as <c>Set-Cookie</c>, and nothing on
    /// this host serialised the recording collection that used to sit here — so
    /// <c>Response.Cookies.Append(...)</c> compiled, ran, and the client never saw it.
    /// </summary>
    public ICookieSetCollection Cookies =>
        _cookies ??= new HeaderCookieSetCollection(_feature.Headers);

    public bool ShouldSerialize { get; set; } = true;

    /// <summary>
    /// Flushes and completes the response.
    ///
    /// Kestrel requires this — without it a response that wrote no body never sends its headers,
    /// and the connection is left waiting on a request the application considers finished.
    /// </summary>
    public ValueTask CompleteAsync() => new(_bodyFeature.CompleteAsync());
}
