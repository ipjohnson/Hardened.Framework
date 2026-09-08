using Hardened.Gcp.CloudRun.Eventarc;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Xunit;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Envelopes;

/// <summary>
/// Any CloudEvent, routed on its source and type.
/// </summary>
public class EventarcEnvelopeTests {
    private static readonly EventarcEnvelope Envelope = new();

    [Fact]
    public void ABinaryEventRoutesOnSourceAndTypeAndBindsItsData() {
        var delivery = Deliveries.CloudEvent("OrderPlaced", "com.acme.orders", "orders/o-1", more: ("ce-traceparent", "00-abc-def-01"));

        var request = Deliveries.Unwrap(Envelope, delivery, "{\"id\":\"e-1\"}")!;

        Assert.Equal("EVENT", request.Method);
        Assert.Equal("/com.acme.orders/OrderPlaced", request.Path);
        Assert.Equal("{\"id\":\"e-1\"}", Deliveries.Text(request.Body));
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("orders/o-1", request.Headers["ce-subject"].ToString());
        Assert.Equal("00-abc-def-01", request.Headers["ce-traceparent"].ToString());
    }

    [Fact]
    public void AStructuredEventReadsTheSame() {
        var request = Deliveries.Unwrap(Envelope, Deliveries.Request("POST", "/", "application/cloudevents+json"), """
            {"specversion":"1.0","id":"2","source":"com.acme.orders","type":"OrderPlaced","datacontenttype":"application/json",
             "data":{"id":"e-2"}}
            """)!;

        Assert.Equal("/com.acme.orders/OrderPlaced", request.Path);
        Assert.Equal("{\"id\":\"e-2\"}", Deliveries.Text(request.Body));
        Assert.Equal("2", request.Headers["ce-id"].ToString());
    }

    /// <summary>A source with slashes keeps them: the handler names it whole.</summary>
    [Fact]
    public void ASourceWithSlashesIsTheRouteAsWritten() {
        var delivery = Deliveries.CloudEvent("google.cloud.audit.log.v1.written", "//cloudaudit.googleapis.com/projects/p/logs/activity");

        var request = Deliveries.Unwrap(Envelope, delivery, "{}")!;

        Assert.Equal("///cloudaudit.googleapis.com/projects/p/logs/activity/google.cloud.audit.log.v1.written", request.Path);
    }

    [Fact]
    public void ARequestThatClaimsToBeACloudEventAndIsNotIsRefused() {
        var failure = Assert.Throws<InvalidOperationException>(() => Deliveries.Unwrap(
            Envelope, Deliveries.Request("POST", "/", "application/json", ("ce-specversion", "1.0")), "{}"));

        Assert.Contains("ce-id", failure.Message);
    }

    [Fact]
    public void ItIsAFallbackAskedAfterTheSpecificEnvelopes() {
        Assert.IsAssignableFrom<IFallbackTriggerEnvelope>(Envelope);
    }

    [Theory]
    [InlineData("POST", "application/json", true, true)]
    [InlineData("POST", "application/cloudevents+json", false, true)]
    [InlineData("POST", "application/json", false, false)]
    [InlineData("GET", "application/json", true, false)]
    public void ACloudEventInEitherFormIsRecognised(string method, string contentType, bool binaryHeaders, bool recognised) {
        var request = binaryHeaders
            ? Deliveries.Request(method, "/", contentType, ("ce-specversion", "1.0"))
            : Deliveries.Request(method, "/", contentType);

        Assert.Equal(recognised, Envelope.Recognises(request));
    }
}
