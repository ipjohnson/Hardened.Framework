using System.Text;
using Hardened.CloudEvents;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.CloudEvents;

/// <summary>
/// The binary HTTP form, as Eventarc delivers it: the payload as the body, the attributes as
/// <c>ce-</c> headers.
/// </summary>
public class BinaryCloudEventTests {
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"id\":\"a-1\"}");

    private static Dictionary<string, StringValues> Headers(bool caseInsensitive = true) =>
        new(caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal) {
            ["ce-specversion"] = "1.0",
            ["ce-id"] = "1234",
            ["ce-source"] = "//storage.googleapis.com/projects/_/buckets/uploads",
            ["ce-type"] = "google.cloud.storage.object.v1.finalized",
            ["ce-subject"] = "objects/a.txt",
            ["ce-time"] = "2026-09-07T10:00:00Z",
            ["ce-dataschema"] = "https://example.test/object.json",
            ["ce-bucket"] = "uploads",
            ["Content-Type"] = "application/json",
            ["X-Unrelated"] = "ignored"
        };

    [Fact]
    public void TheContextAttributesComeFromTheHeaders() {
        var cloudEvent = CloudEventReader.ReadBinary(Headers(), Body);

        Assert.Equal("1.0", cloudEvent.SpecVersion);
        Assert.Equal("1234", cloudEvent.Id);
        Assert.Equal("//storage.googleapis.com/projects/_/buckets/uploads", cloudEvent.Source);
        Assert.Equal("google.cloud.storage.object.v1.finalized", cloudEvent.Type);
        Assert.Equal("objects/a.txt", cloudEvent.Subject);
        Assert.Equal("2026-09-07T10:00:00Z", cloudEvent.Time);
        Assert.Equal("https://example.test/object.json", cloudEvent.DataSchema);
    }

    [Fact]
    public void TheBodyIsTheDataAndContentTypeIsItsType() {
        var cloudEvent = CloudEventReader.ReadBinary(Headers(), Body);

        Assert.Equal(Body, cloudEvent.Data.ToArray());
        Assert.Equal("application/json", cloudEvent.DataContentType);
    }

    [Fact]
    public void OtherPrefixedHeadersAreExtensionsWithoutThePrefix() {
        var cloudEvent = CloudEventReader.ReadBinary(Headers(), Body);

        Assert.Equal("uploads", cloudEvent.Extensions["bucket"]);
        Assert.False(cloudEvent.Extensions.ContainsKey("x-unrelated"));
        Assert.False(cloudEvent.Extensions.ContainsKey("id"));
    }

    /// <summary>A plain dictionary a test built compares by case; a transport's does not. Both read alike.</summary>
    [Fact]
    public void ACaseSensitiveDictionaryReadsTheSame() {
        var headers = Headers(caseInsensitive: false);

        headers.Remove("ce-id");
        headers["CE-ID"] = "upper";

        var cloudEvent = CloudEventReader.ReadBinary(headers, Body);

        Assert.Equal("upper", cloudEvent.Id);
        Assert.True(CloudEventReader.IsBinary(headers));
    }

    [Fact]
    public void AMissingRequiredHeaderIsNamed() {
        var headers = Headers();

        headers.Remove("ce-type");

        var failure = Assert.Throws<CloudEventFormatException>(() => CloudEventReader.ReadBinary(headers, Body));

        Assert.Contains("ce-type", failure.Message);
    }

    [Fact]
    public void TheBinaryFormIsRecognisedBySpecVersion() {
        Assert.True(CloudEventReader.IsBinary(Headers()));
        Assert.False(CloudEventReader.IsBinary(new Dictionary<string, StringValues> { ["Content-Type"] = "application/json" }));
    }

    [Fact]
    public void ReadPicksTheFormFromTheRequest() {
        var binary = CloudEventReader.Read("application/json", Headers(), Body);

        Assert.Equal("1234", binary.Id);

        var structured = CloudEventReader.Read(
            "application/cloudevents+json",
            new Dictionary<string, StringValues>(),
            Encoding.UTF8.GetBytes("""{"specversion":"1.0","id":"s-1","source":"/s","type":"t"}"""));

        Assert.Equal("s-1", structured.Id);

        Assert.Throws<CloudEventFormatException>(
            () => CloudEventReader.Read("application/json", new Dictionary<string, StringValues>(), Body));
    }
}

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
