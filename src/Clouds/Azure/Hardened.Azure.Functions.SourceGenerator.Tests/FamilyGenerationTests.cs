using Hardened.Azure.Functions.SourceGenerator.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;
using static Hardened.Azure.Functions.SourceGenerator.Tests.Infrastructure.AzureGeneratorHarness;

namespace Hardened.Azure.Functions.SourceGenerator.Tests;

/// <summary>
/// One function per family, each compiled against its extension and its provider run.
///
/// <para>
/// The queue tests hold the mechanism; these hold the table: that every row in
/// <c>AzureBinding.All</c> writes a shim the worker can bind and a binding the host can index.
/// A row whose parameter type the executor cannot cast, or whose attribute the extension does not
/// declare, fails here rather than in a fixture's build.
/// </para>
/// </summary>
public class FamilyGenerationTests {

    /// <summary>
    /// The schedule is an app setting named after the trigger, not an expression in the code, and
    /// the timer arrives as the host's JSON in a string.
    /// </summary>
    [Fact]
    public async Task ATimerHandlerCompilesToATimerFunctionScheduledByAnAppSetting() {
        var result = Generate(
            """
                [Timer("nightly-rollup")]
                public void Nightly() { }
            """,
            ("HardenedTimerModule", TimerModule)).AssertNoErrors();

        Assert.Contains("TimerTrigger(\"%Hardened:Timers:nightly-rollup%\")] string timer", FunctionsSource(result));

        var timer = Assert.Single(await Provider(result).GetFunctionMetadataAsync(""));

        Assert.Equal("Timer_nightly_rollup", timer.Name);

        var binding = Assert.Single(timer.RawBindings!);

        Assert.Contains("\"type\":\"timerTrigger\"", binding);
        Assert.Contains("\"schedule\":\"%Hardened:Timers:nightly-rollup%\"", binding);
    }

    /// <summary>
    /// Event Hubs binds the SDK's own event, batched, with the consumer group and connection the
    /// application named.
    /// </summary>
    [Fact]
    public async Task AStreamHandlerCompilesToABatchedEventHubsFunction() {
        var result = Generate(
            """
                [Stream("clickstream")]
                public void OnClick(Order order) { }
            """,
            [("HardenedStreamModule", EventHubsModule)],
            application: "[global::Hardened.Azure.Functions.EventHubs.EventHubsModule(ConsumerGroup = \"analytics\")]")
            .AssertNoErrors();

        Assert.Contains("global::Azure.Messaging.EventHubs.EventData[] events", FunctionsSource(result));

        var stream = Assert.Single(await Provider(result).GetFunctionMetadataAsync(""));

        Assert.Equal("Stream_clickstream", stream.Name);

        var binding = Assert.Single(stream.RawBindings!);

        Assert.Contains("\"type\":\"eventHubTrigger\"", binding);
        Assert.Contains("\"eventHubName\":\"clickstream\"", binding);
        Assert.Contains("\"consumerGroup\":\"analytics\"", binding);
        Assert.Contains("\"cardinality\":\"Many\"", binding);
    }

    /// <summary>
    /// A change feed function needs the database its container lives in, which the neutral
    /// trigger has no slot for; without it the build fails naming the module property to write.
    /// </summary>
    [Fact]
    public void AChangeHandlerWithoutADatabaseIsReported() {
        var result = Generate(
            """
                [Change("orders")]
                public void OnChange(Order order) { }
            """,
            ("HardenedChangeModule", CosmosDbModule));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, one => one.Id == "HRDAZ003");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Database", diagnostic.GetMessage());
        Assert.Contains("[CosmosDbModule(Database = \"...\")]", diagnostic.GetMessage());
    }

    /// <summary>
    /// With the database named, the feed arrives as the host's JSON array in a string, and the
    /// binding carries the database, the container and the lease container's creation.
    /// </summary>
    [Fact]
    public async Task AChangeHandlerWithADatabaseCompilesToAChangeFeedFunction() {
        var result = Generate(
            """
                [Change("orders")]
                public void OnChange(Order order) { }
            """,
            [("HardenedChangeModule", CosmosDbModule)],
            application: "[global::Hardened.Azure.Functions.CosmosDb.CosmosDbModule(Database = \"orders-db\", LeaseContainer = \"feed-leases\")]")
            .AssertNoErrors();

        Assert.Contains("CosmosDBTrigger(\"orders-db\", \"orders\", LeaseContainerName = \"feed-leases\", CreateLeaseContainerIfNotExists = true)] string documents", FunctionsSource(result));

        var change = Assert.Single(await Provider(result).GetFunctionMetadataAsync(""));

        Assert.Equal("Change_orders", change.Name);

        var binding = Assert.Single(change.RawBindings!);

        Assert.Contains("\"type\":\"cosmosDBTrigger\"", binding);
        Assert.Contains("\"databaseName\":\"orders-db\"", binding);
        Assert.Contains("\"containerName\":\"orders\"", binding);
        Assert.Contains("\"leaseContainerName\":\"feed-leases\"", binding);
        Assert.Contains("\"createLeaseContainerIfNotExists\":true", binding);
    }

    /// <summary>
    /// A blob function binds the client rather than the content, fed by Event Grid rather than by
    /// scanning the container.
    /// </summary>
    [Fact]
    public async Task ABlobHandlerCompilesToAnEventGridFedBlobFunction() {
        var result = Generate(
            """
                [Blob("uploads")]
                public void OnUpload(Order order) { }
            """,
            ("HardenedBlobModule", BlobsModule)).AssertNoErrors();

        Assert.Contains("global::Azure.Storage.Blobs.BlobClient blob", FunctionsSource(result));

        var blob = Assert.Single(await Provider(result).GetFunctionMetadataAsync(""));

        Assert.Equal("Blob_uploads", blob.Name);

        var binding = Assert.Single(blob.RawBindings!);

        Assert.Contains("\"type\":\"blobTrigger\"", binding);
        Assert.Contains("\"path\":\"uploads/{name}\"", binding);
        Assert.Contains("\"source\":\"EventGrid\"", binding);
    }

    /// <summary>
    /// Every event handler shares one function, because an Event Grid subscription delivers to
    /// one function and which handler runs is a question about the event.
    /// </summary>
    [Fact]
    public async Task EventHandlersCompileToOneEventGridFunction() {
        var result = Generate(
            """
                [Event("com.acme.orders", "OrderPlaced")]
                public void OnPlaced(Order order) { }

                [Event("com.acme.orders", "OrderCancelled")]
                public void OnCancelled(Order order) { }
            """,
            ("HardenedEventModule", EventGridModule)).AssertNoErrors();

        Assert.Contains("EventGridTrigger] string cloudEvent", FunctionsSource(result));

        var function = Assert.Single(await Provider(result).GetFunctionMetadataAsync(""));

        Assert.Equal("Event", function.Name);

        var binding = Assert.Single(function.RawBindings!);

        Assert.Contains("\"type\":\"eventGridTrigger\"", binding);
    }

    /// <summary>
    /// The web verbs compile to one anonymous function catching every method under every path,
    /// answering with the worker's response data, and the provider lists its return binding.
    /// </summary>
    [Fact]
    public async Task WebVerbsCompileToOneCatchAllHttpFunction() {
        var result = Generate(
            """
                [Get("/orders/{id}")]
                public Order Get(string id) => new();

                [Post("/orders")]
                public Order Place(Order order) => order;
            """,
            ("HardenedHttpModule", HttpModule)).AssertNoErrors();

        var functions = FunctionsSource(result);

        Assert.Contains("Route = \"{*path}\")] global::Microsoft.Azure.Functions.Worker.Http.HttpRequestData request", functions);
        Assert.Contains("global::System.Threading.Tasks.Task<global::Microsoft.Azure.Functions.Worker.Http.HttpResponseData> Http(", functions);

        var function = Assert.Single(await Provider(result).GetFunctionMetadataAsync(""));

        Assert.Equal("Http", function.Name);
        Assert.Equal(2, function.RawBindings!.Count);

        var trigger = function.RawBindings[0];

        Assert.Contains("\"type\":\"httpTrigger\"", trigger);
        Assert.Contains("\"authLevel\":\"Anonymous\"", trigger);
        Assert.Contains("\"route\":\"{*path}\"", trigger);
        Assert.Contains("\"methods\":[\"get\",\"post\",\"put\",\"patch\",\"delete\",\"head\",\"options\"]", trigger);
        Assert.Contains("\"name\":\"$return\"", function.RawBindings[1]);
    }
}
