using System.Text;
using Xunit;

namespace Hardened.CloudEvents.Tests;

/// <summary>
/// The structured JSON form, as Eventarc's structured mode and Event Grid's CloudEvents schema
/// send it.
/// </summary>
public class StructuredCloudEventTests {
    private const string Full = """
        {
          "specversion": "1.0",
          "id": "1234-1234-1234",
          "source": "//pubsub.googleapis.com/projects/p/topics/orders",
          "type": "google.cloud.pubsub.topic.v1.messagePublished",
          "subject": "orders/42",
          "time": "2026-09-07T10:00:00Z",
          "datacontenttype": "application/json",
          "dataschema": "https://example.test/order.json",
          "traceparent": "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
          "retries": 3,
          "data": {"id": "a-1"}
        }
        """;

    private static CloudEvent Read(string json) => CloudEventReader.ReadStructured(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void TheContextAttributesAreRead() {
        var cloudEvent = Read(Full);

        Assert.Equal("1.0", cloudEvent.SpecVersion);
        Assert.Equal("1234-1234-1234", cloudEvent.Id);
        Assert.Equal("//pubsub.googleapis.com/projects/p/topics/orders", cloudEvent.Source);
        Assert.Equal("google.cloud.pubsub.topic.v1.messagePublished", cloudEvent.Type);
        Assert.Equal("orders/42", cloudEvent.Subject);
        Assert.Equal("2026-09-07T10:00:00Z", cloudEvent.Time);
        Assert.Equal("application/json", cloudEvent.DataContentType);
        Assert.Equal("https://example.test/order.json", cloudEvent.DataSchema);
    }

    [Fact]
    public void JsonDataIsTheDocumentAsBytes() {
        var cloudEvent = Read(Full);

        Assert.Equal("{\"id\":\"a-1\"}", Encoding.UTF8.GetString(cloudEvent.Data.Span));
    }

    /// <summary>Extensions are strings whatever the JSON carried, so the two forms read alike.</summary>
    [Fact]
    public void ExtensionsAreKeptAsText() {
        var cloudEvent = Read(Full);

        Assert.Equal("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", cloudEvent.Extensions["traceparent"]);
        Assert.Equal("3", cloudEvent.Extensions["retries"]);
        Assert.False(cloudEvent.Extensions.ContainsKey("data"));
    }

    [Fact]
    public void Base64DataIsDecoded() {
        var cloudEvent = Read("""
            {"specversion":"1.0","id":"1","source":"/s","type":"t","datacontenttype":"application/octet-stream",
             "data_base64":"AQID"}
            """);

        Assert.Equal(new byte[] { 1, 2, 3 }, cloudEvent.Data.ToArray());
    }

    /// <summary>A string under a non-JSON content type is the text itself, not a JSON document about a string.</summary>
    [Fact]
    public void TextDataUnderATextContentTypeIsTheTextItself() {
        var cloudEvent = Read("""
            {"specversion":"1.0","id":"1","source":"/s","type":"t","datacontenttype":"text/plain","data":"hello"}
            """);

        Assert.Equal("hello", Encoding.UTF8.GetString(cloudEvent.Data.Span));
    }

    [Fact]
    public void AStringUnderAJsonContentTypeStaysQuoted() {
        var cloudEvent = Read("""
            {"specversion":"1.0","id":"1","source":"/s","type":"t","data":"hello"}
            """);

        Assert.Equal("\"hello\"", Encoding.UTF8.GetString(cloudEvent.Data.Span));
    }

    [Fact]
    public void AnEventWithoutDataHasEmptyData() {
        var cloudEvent = Read("""{"specversion":"1.0","id":"1","source":"/s","type":"t"}""");

        Assert.True(cloudEvent.Data.IsEmpty);
        Assert.Null(cloudEvent.Subject);
        Assert.Empty(cloudEvent.Extensions);
    }

    [Theory]
    [InlineData("""{"id":"1","source":"/s","type":"t"}""", "specversion")]
    [InlineData("""{"specversion":"1.0","source":"/s","type":"t"}""", "id")]
    [InlineData("""{"specversion":"1.0","id":"1","type":"t"}""", "source")]
    [InlineData("""{"specversion":"1.0","id":"1","source":"/s"}""", "type")]
    public void AMissingRequiredAttributeIsNamed(string json, string attribute) {
        var failure = Assert.Throws<CloudEventFormatException>(() => Read(json));

        Assert.Contains(attribute, failure.Message);
    }

    [Fact]
    public void ABodyThatIsNotJsonIsRefused() {
        Assert.Throws<CloudEventFormatException>(() => Read("not json"));
    }

    [Theory]
    [InlineData("application/cloudevents+json", true)]
    [InlineData("application/cloudevents+json; charset=utf-8", true)]
    [InlineData("APPLICATION/CLOUDEVENTS+JSON", true)]
    [InlineData("application/json", false)]
    [InlineData(null, false)]
    public void TheStructuredFormIsRecognisedByItsContentType(string? contentType, bool structured) {
        Assert.Equal(structured, CloudEventReader.IsStructured(contentType));
    }
}
