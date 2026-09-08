using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.CloudEvents.Tests;

/// <summary>
/// An event's attributes as headers, under the binary form's names whichever form it arrived in.
/// </summary>
public class CloudEventHeadersTests {

    [Fact]
    public void TheRequiredAttributesAreAlwaysWritten() {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        CloudEventHeaders.Write(headers, new CloudEvent("1.0", "1", "/s", "t"));

        Assert.Equal("1.0", headers["ce-specversion"].ToString());
        Assert.Equal("1", headers["ce-id"].ToString());
        Assert.Equal("/s", headers["ce-source"].ToString());
        Assert.Equal("t", headers["ce-type"].ToString());
        Assert.False(headers.ContainsKey("ce-subject"));
        Assert.False(headers.ContainsKey("ce-time"));
    }

    [Fact]
    public void TheOptionalAttributesAndExtensionsAreWrittenWhenSet() {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        CloudEventHeaders.Write(headers, new CloudEvent("1.0", "1", "/s", "t") {
            Subject = "orders/1",
            Time = "2026-09-07T10:00:00Z",
            DataSchema = "https://example.test/schema",
            Extensions = new Dictionary<string, string> { ["traceparent"] = "00-abc-def-01" }
        });

        Assert.Equal("orders/1", headers["ce-subject"].ToString());
        Assert.Equal("2026-09-07T10:00:00Z", headers["ce-time"].ToString());
        Assert.Equal("https://example.test/schema", headers["ce-dataschema"].ToString());
        Assert.Equal("00-abc-def-01", headers["ce-traceparent"].ToString());
    }

    /// <summary>Written and read back through the binary reader, the event is the same.</summary>
    [Fact]
    public void WhatIsWrittenIsWhatTheBinaryReaderReads() {
        var original = new CloudEvent("1.0", "1", "/s", "t") { Subject = "orders/1", Extensions = new Dictionary<string, string> { ["bucket"] = "b" } };
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        CloudEventHeaders.Write(headers, original);

        var read = CloudEventReader.ReadBinary(headers, ReadOnlyMemory<byte>.Empty);

        Assert.Equal(original.Id, read.Id);
        Assert.Equal(original.Subject, read.Subject);
        Assert.Equal("b", read.Extensions["bucket"]);
    }
}
