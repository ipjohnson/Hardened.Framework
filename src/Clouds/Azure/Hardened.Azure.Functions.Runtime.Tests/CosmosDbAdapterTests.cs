using Hardened.Azure.Functions.CosmosDb;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Azure.Functions.Testing;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Azure.Functions.Runtime.Tests;

public class CosmosDbAdapterTests {
    private static readonly CosmosDbAdapter Adapter = new();

    private static TestFunctionContext Context() =>
        new("Change_orders", new Dictionary<string, object?>(), new ServiceCollection().BuildServiceProvider());

    private static CosmosDbRequest Request(string feed) =>
        (CosmosDbRequest)Adapter.CreateRequest(new FunctionsTrigger("CHANGE", "/orders", feed), Context());

    [Fact]
    public void HandlesAStringOnlyUnderTheChangeScheme() {
        Assert.True(Adapter.Handles(new FunctionsTrigger("CHANGE", "/orders", "[]")));
        Assert.False(Adapter.Handles(new FunctionsTrigger("TIMER", "/nightly", "{}")));
    }

    /// <summary>
    /// The feed is one array; the batch is one request per document, in the feed's order.
    /// </summary>
    [Fact]
    public void TheFeedIsSplitIntoOneDocumentPerChange() {
        var batch = Request("""[{"id":"a","_lsn":10},{"id":"b","_lsn":11},{"id":"c","_lsn":12}]""");

        Assert.Equal(3, batch.Count);
        Assert.Equal(["a", "b", "c"], batch.Documents.Select(document => document.Id));
        Assert.Equal(BatchFailureMode.Checkpoint, batch.FailureMode);
        Assert.False(batch.ReportsItemFailures);
    }

    /// <summary>
    /// The body is the document as it is now, system properties included, and the ones a handler
    /// might want without binding the body are headers.
    /// </summary>
    [Fact]
    public void AForkCarriesTheDocumentAndItsSystemProperties() {
        var batch = Request("""[{"id":"a-1","quantity":2,"_rid":"r","_etag":"\"00000001\"","_ts":1767225600,"_lsn":42}]""");

        var fork = batch.ForItem(0);

        Assert.Equal("CHANGE", fork.Method);
        Assert.Equal("/orders", fork.Path);
        Assert.Equal("application/json", fork.ContentType);
        Assert.Equal("a-1", fork.Headers[CosmosDbRequest.IdHeader].ToString());
        Assert.Equal("42", fork.Headers[CosmosDbRequest.LsnHeader].ToString());
        Assert.Equal("1767225600", fork.Headers[CosmosDbRequest.TimestampHeader].ToString());
        Assert.Equal("\"00000001\"", fork.Headers[CosmosDbRequest.ETagHeader].ToString());

        Assert.Equal(
            """{"id":"a-1","quantity":2,"_rid":"r","_etag":"\"00000001\"","_ts":1767225600,"_lsn":42}""",
            new StreamReader(fork.Body).ReadToEnd());
    }

    /// <summary>
    /// Each fork owns its bytes, so reading one does not disturb another and nothing outlives the
    /// parse of the feed.
    /// </summary>
    [Fact]
    public void ForksAreIndependent() {
        var batch = Request("""[{"id":"a"},{"id":"b"}]""");

        var first = batch.ForItem(0);
        var second = batch.ForItem(1);

        Assert.Equal("""{"id":"b"}""", new StreamReader(second.Body).ReadToEnd());
        Assert.Equal("""{"id":"a"}""", new StreamReader(first.Body).ReadToEnd());
    }

    [Fact]
    public void AnEmptyFeedIsAnEmptyBatch() {
        Assert.Equal(0, Request("").Count);
        Assert.Equal(0, Request("[]").Count);
    }

    /// <summary>
    /// Nothing reports a position back to the extension, so recording a failure has nowhere to go
    /// and says so rather than pretending.
    /// </summary>
    [Fact]
    public void RecordingAFailureIsRefused() {
        var batch = Request("""[{"id":"a"}]""");

        Assert.Throws<NotSupportedException>(() => batch.RecordFailure(0, new InvalidOperationException()));
    }
}
