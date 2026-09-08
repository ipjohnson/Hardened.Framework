using System.Text.Json;
using Azure.Storage.Blobs;
using Hardened.Azure.Functions.Blobs;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Azure.Functions.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Azure.Functions.Runtime.Tests;

public class BlobsAdapterTests {
    private static readonly BlobsAdapter Adapter = new();

    private static readonly BlobClient Blob =
        new(new Uri("https://devstoreaccount1.blob.core.windows.net/uploads/reports/my%20report.pdf"));

    private static TestFunctionContext Context(string? properties = null) =>
        new("Blob_uploads",
            properties == null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?> { ["Properties"] = properties },
            new ServiceCollection().BuildServiceProvider());

    [Fact]
    public void HandlesABlobClient() {
        Assert.True(Adapter.Handles(new FunctionsTrigger("BLOB", "/uploads", Blob)));
        Assert.False(Adapter.Handles(new FunctionsTrigger("BLOB", "/uploads", "uploads/a.txt")));
    }

    /// <summary>
    /// The body is the notification: what Storage said about the blob, with the name decoded off
    /// the client, and never the blob's content.
    /// </summary>
    [Fact]
    public void TheBodyIsTheNotificationWithTheNameDecoded() {
        var request = Adapter.CreateRequest(
            new FunctionsTrigger("BLOB", "/uploads", Blob),
            Context("""{"Length":1024,"ContentType":"application/pdf","ETag":"\"0x8D\""}"""));

        using var body = JsonDocument.Parse(request.Body);

        Assert.Equal("uploads", body.RootElement.GetProperty("container").GetString());
        Assert.Equal("reports/my report.pdf", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(1024, body.RootElement.GetProperty("size").GetInt64());
        Assert.Equal("application/pdf", body.RootElement.GetProperty("contentType").GetString());
        Assert.Equal(BlobsAdapter.BlobCreated, body.RootElement.GetProperty("eventName").GetString());

        Assert.Equal("uploads", request.Headers[BlobsAdapter.ContainerHeader].ToString());
        Assert.Equal("reports/my report.pdf", request.Headers[BlobsAdapter.NameHeader].ToString());
        Assert.Equal("1024", request.Headers[BlobsAdapter.SizeHeader].ToString());
        Assert.Equal("BLOB", request.Method);
        Assert.Equal("/uploads", request.Path);
    }

    /// <summary>
    /// Older hosts write the size as ContentLength; both spellings are read.
    /// </summary>
    [Fact]
    public void TheSizeIsReadUnderEitherName() {
        var properties = BlobsAdapter.Properties(Context("""{"ContentLength":7}"""));

        Assert.Equal(7, properties.Size);
    }

    /// <summary>
    /// A host that sent no properties still yields a notification, with the size left out rather
    /// than invented.
    /// </summary>
    [Fact]
    public void WithoutPropertiesTheSizeIsAbsent() {
        var request = Adapter.CreateRequest(new FunctionsTrigger("BLOB", "/uploads", Blob), Context());

        using var body = JsonDocument.Parse(request.Body);

        Assert.False(body.RootElement.TryGetProperty("size", out _));
        Assert.False(request.Headers.ContainsKey(BlobsAdapter.SizeHeader));
    }
}
