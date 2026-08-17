using System.Diagnostics;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// The span a real request produces, through the real routing table.
/// </summary>
/// <remarks>
/// <para>
/// The unit tests around <c>RequestLogger</c> drive its methods directly, which proves what it does
/// with a route but not that a route ever reaches it. This goes through the host: an actual request,
/// matched by the generated routing table, dispatched by <c>WebExecutionHandlerService</c> — which is
/// what calls <c>RequestMapped</c>, and therefore the only thing that makes <c>http.route</c> real.
/// </para>
/// <para>
/// Attribute names and the source name are literals, not constants shared with the code under test.
/// They are the contract a trace backend reads, and sharing a constant would let a rename pass here
/// while breaking every consumer.
/// </para>
/// </remarks>
public class RequestTelemetryTests {

    /// <summary>
    /// Runs one request and returns the span it produced.
    /// </summary>
    /// <remarks>
    /// An <c>ActivitySource</c> is process-global and every other test in this assembly runs in
    /// parallel through the same one, so the wanted span is picked out inside the callback by a path
    /// nothing else requests. Filtering here rather than collecting everything and filtering
    /// afterwards is what keeps a shared collection from being written while it is read — which is
    /// exactly how the first version of this file failed.
    /// </remarks>
    private static async Task<Activity> SpanFor(ITestWebApp testWebApp, string path) {
        Activity? captured = null;

        using var listener = new ActivityListener {
            ShouldListenTo = source => source.Name == "Hardened.Requests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = span => {
                if ((string?)span.GetTagItem("url.path") == path) {
                    captured = span;
                }
            }
        };

        ActivitySource.AddActivityListener(listener);

        await testWebApp.Get(path);

        Assert.NotNull(captured);

        return captured;
    }

    [HardenedTest]
    public async Task ARequestProducesOneServerSpanCarryingItsRouteTemplate(ITestWebApp testWebApp) {
        var span = await SpanFor(testWebApp, "/binding/path/telemetry-probe");

        Assert.Equal(ActivityKind.Server, span.Kind);

        // The template the routing table matched, not the path that arrived. This is the assertion
        // the whole design turns on: it is low cardinality, and Hardened knows it before the handler
        // runs rather than having to rename the span at the end of the request.
        Assert.Equal("/binding/path/{id}", span.GetTagItem("http.route"));
        Assert.Equal("GET /binding/path/{id}", span.DisplayName);

        Assert.Equal("GET", span.GetTagItem("http.request.method"));
        Assert.Equal("/binding/path/telemetry-probe", span.GetTagItem("url.path"));

        // Nothing assigns a status on an ordinary success path — null means "handled, no opinion" —
        // so this is the one attribute that only a request through a real host could have caught.
        Assert.Equal(200, span.GetTagItem("http.response.status_code"));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    /// <summary>
    /// A path the routing table does not match never reaches <c>RequestMapped</c>, so the span has
    /// no route to carry and keeps the method as its name — one span per unmatched URL is exactly
    /// the cardinality explosion the conventions exist to prevent.
    /// </summary>
    [HardenedTest]
    public async Task AnUnmatchedRequestGetsASpanWithNoRoute(ITestWebApp testWebApp) {
        var span = await SpanFor(testWebApp, "/telemetry-probe-no-such-route");

        Assert.Null(span.GetTagItem("http.route"));
        Assert.Equal("GET", span.DisplayName);

        // A 404 is the caller asking for something that is not there, not this service failing.
        Assert.Equal(404, span.GetTagItem("http.response.status_code"));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }
}
