using Hardened.Gcp.CloudRun.Invoke;
using Xunit;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Envelopes;

/// <summary>
/// A direct invocation over HTTP: the operation in the URL, the caller's payload untouched.
/// </summary>
public class InvokeEnvelopeTests {
    private static readonly InvokeEnvelope Envelope = new(InvokeEnvelope.DefaultPrefix);

    [Fact]
    public void TheOperationInTheUrlIsTheRouteAndTheBodyIsTheCallers() {
        var delivery = Deliveries.Post("/_triggers/invoke/place-order", ("Authorization", "Bearer token"));

        var request = Deliveries.Unwrap(Envelope, delivery, "{\"Records\":[\"a\"],\"id\":\"o-1\"}")!;

        Assert.Equal("INVOKE", request.Method);
        Assert.Equal("/place-order", request.Path);
        Assert.Equal("{\"Records\":[\"a\"],\"id\":\"o-1\"}", Deliveries.Text(request.Body));
        Assert.Equal("Bearer token", request.Headers["Authorization"].ToString());
        Assert.Equal("application/json", request.ContentType);
    }

    [Theory]
    [InlineData("POST", "/_triggers/invoke/place-order", true)]
    [InlineData("GET", "/_triggers/invoke/place-order", false)]
    [InlineData("POST", "/_triggers/invoke/", false)]
    [InlineData("POST", "/orders", false)]
    public void OnlyAPostToThePrefixedPathIsRecognised(string method, string path, bool recognised) {
        Assert.Equal(recognised, Envelope.Recognises(Deliveries.Request(method, path, "application/json")));
    }

    [Fact]
    public void AConfiguredPrefixIsWhatIsRead() {
        var envelope = new InvokeEnvelope("rpc");

        var request = Deliveries.Unwrap(envelope, Deliveries.Post("/rpc/place-order"), "{}")!;

        Assert.Equal("/place-order", request.Path);
        Assert.Equal("/rpc/", envelope.Prefix);
    }
}
