using Xunit;

namespace Hardened.CloudEvents.Tests;

public class CloudEventRoutesTests {

    [Fact]
    public void AnEventRoutesOnSourceAndType() {
        var cloudEvent = new CloudEvent("1.0", "1", "com.acme.orders", "OrderPlaced");

        Assert.Equal("/com.acme.orders/OrderPlaced", CloudEventRoutes.Event(cloudEvent));
        Assert.Equal("EVENT", CloudEventRoutes.EventScheme);
    }

    [Theory]
    [InlineData("//pubsub.googleapis.com/projects/p/topics/orders", "orders")]
    [InlineData("projects/p/topics/orders/", "orders")]
    [InlineData("orders", "orders")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void TheLastSegmentIsTheResourcesOwnName(string? value, string expected) {
        Assert.Equal(expected, CloudEventRoutes.LastSegment(value));
    }
}
