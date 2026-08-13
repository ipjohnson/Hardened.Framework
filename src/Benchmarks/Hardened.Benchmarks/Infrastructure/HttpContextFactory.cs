using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Hardened.Benchmarks.Infrastructure;

/// <summary>
/// Builds the <c>HttpContext</c> that both ASP.NET and Hardened-behind-ASP.NET are driven from.
///
/// This is the seam that makes the comparison possible without a server. ASP.NET's request
/// pipeline is a <c>RequestDelegate</c> over an <c>HttpContext</c>; Kestrel is only one of the
/// things that can produce one. Constructing it here runs everything above the server boundary —
/// route matching, endpoint selection, parameter binding, deserialization, handler invocation,
/// serialization — while excluding socket I/O, HTTP parsing, header serialization and framing.
///
/// Excluding those is not a convenience. Hardened targets any transport, and on Lambda it never
/// pays Kestrel's costs at all, so folding them in would measure one particular deployment
/// rather than the pipeline. They are also large enough to bury the signal: protocol handling is
/// tens of microseconds against single-digit microseconds of pipeline work, and its run-to-run
/// variance alone would exceed the differences being measured.
///
/// The features are populated explicitly rather than left to <c>DefaultHttpContext</c>'s
/// built-in defaults, because a server populates them too and two of the defaults are wrong for
/// this purpose. See <see cref="TrackingResponseFeature"/>.
/// </summary>
public static class HttpContextFactory {

    public static DefaultHttpContext Create(
        RequestScenario scenario,
        IServiceProvider requestServices,
        MemoryStream responseBody) {
        return new DefaultHttpContext(CreateFeatures(scenario, responseBody)) {
            // Normally set by HostingApplication, which sits above the RequestDelegate and so is
            // not in play here. AspNetExecutionContext reads it, and so does [FromServices]
            // binding on both ASP.NET flavors.
            RequestServices = requestServices
        };
    }

    /// <summary>
    /// The feature collection a server would hand to <c>IHttpApplication.CreateContext</c>.
    ///
    /// Shared by every ASP.NET-shaped pipeline here — the two ASP.NET flavors and Hardened behind
    /// its adapter get it wrapped in a <c>DefaultHttpContext</c>, while
    /// <see cref="HardenedHttpApplication"/> consumes it directly. Building it in one place is
    /// what keeps that last comparison honest: the feature-based path must not be measured
    /// against cheaper inputs than the paths it is being compared to.
    /// </summary>
    public static FeatureCollection CreateFeatures(
        RequestScenario scenario,
        MemoryStream responseBody) {
        var features = new FeatureCollection();

        var requestFeature = new HttpRequestFeature {
            Method = scenario.Method,
            Path = scenario.Path,
            QueryString = scenario.QueryString is null ? "" : "?" + scenario.QueryString,
            Body = scenario.Body is null ? Stream.Null : new MemoryStream(scenario.Body, false)
        };

        foreach (var header in scenario.Headers) {
            requestFeature.Headers[header.Key] = header.Value;
        }

        if (scenario.Body is not null) {
            requestFeature.Headers.ContentType = scenario.ContentType;
            requestFeature.Headers.ContentLength = scenario.Body.Length;
        }

        features.Set<IHttpRequestFeature>(requestFeature);
        features.Set<IHttpResponseFeature>(new TrackingResponseFeature(responseBody));

        // Response.Body defaults to Stream.Null, which would let every framework serialize into
        // a black hole and charge none of them for the write.
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(responseBody));

        // Minimal API and MVC body binding consult this before reading. Absent, a POST can bind
        // an empty model and still return 200 — a passing benchmark that skipped deserialization.
        features.Set<IHttpRequestBodyDetectionFeature>(
            new RequestBodyDetectionFeature(scenario.Body is not null));

        return features;
    }

    /// <summary>
    /// A response feature whose <c>HasStarted</c> reflects whether anything has been written.
    ///
    /// The stock <c>HttpResponseFeature</c> hardcodes it to <c>false</c>, which breaks Hardened
    /// specifically: <c>AspNetCoreRequestHandler.HandleRequest</c> only falls through to the next
    /// middleware when the response has not started, so against the stock feature it writes a
    /// correct body and then hands the request to the terminal delegate anyway, which overwrites
    /// the status with 404. Under Kestrel the first body write flushes the headers and
    /// <c>HasStarted</c> becomes true, so this tracks the same signal rather than inventing one.
    /// </summary>
    private sealed class TrackingResponseFeature : IHttpResponseFeature {
        private readonly MemoryStream _body;

        public TrackingResponseFeature(MemoryStream body) {
            _body = body;
            Body = body;
        }

        public int StatusCode { get; set; } = 200;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; }

        public bool HasStarted => _body.Length > 0;

        public void OnStarting(Func<object, Task> callback, object state) { }

        public void OnCompleted(Func<object, Task> callback, object state) { }
    }

    private sealed class RequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature {
        public RequestBodyDetectionFeature(bool canHaveBody) => CanHaveBody = canHaveBody;

        public bool CanHaveBody { get; }
    }
}
