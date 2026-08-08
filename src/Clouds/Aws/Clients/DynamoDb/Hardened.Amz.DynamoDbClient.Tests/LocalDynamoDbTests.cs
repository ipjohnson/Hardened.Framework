using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Hardened.Amz.DynamoDbClient.Testing;
using Xunit;

namespace Hardened.Amz.DynamoDbClient.Tests;

/// <summary>
/// The container is the whole value of the Testing package, and until now it was only exercised
/// incidentally, by another repository's data tests.
///
/// <para>
/// Every test here goes through <see cref="LocalDynamoDb"/> alone — no service collection, no
/// module, no attribute — which is also the claim the class makes about itself: that a project
/// wiring its clients some other way can still use it.
/// </para>
/// </summary>
[Collection(LocalDynamoDbCollection.Name)]
public class LocalDynamoDbTests {

    /// <summary>A real tag rather than <c>latest</c>, so the container is a different one.</summary>
    private const string PinnedImage = "amazon/dynamodb-local:3.3.1";

    [RequiresDockerFact]
    public void TheContainerStartsOnceAndKeepsItsEndpoint() {
        var first = LocalDynamoDb.Endpoint;
        var second = LocalDynamoDb.Endpoint;

        Assert.StartsWith("http://", first);
        Assert.Equal(first, second);
    }

    /// <summary>
    /// The point of a container rather than a fake: the assertion is about what DynamoDB did with
    /// the item, not about what a mock was told.
    /// </summary>
    [RequiresDockerFact]
    public async Task AnItemRoundTripsThroughARealTable() {
        using var client = LocalDynamoDb.CreateClient();

        await CreateTable(client, "round-trip");

        await client.PutItemAsync("round-trip", new Dictionary<string, AttributeValue> {
            ["id"] = new("the-key"),
            ["value"] = new("kept"),
        });

        var response = await client.GetItemAsync("round-trip", new Dictionary<string, AttributeValue> {
            ["id"] = new("the-key"),
        });

        Assert.Equal("kept", response.Item["value"].S);
    }

    /// <summary>
    /// The image used to be a mutable static, so a suite could only ever have one and which one
    /// depended on what ran first. Two named images now mean two containers.
    /// </summary>
    [RequiresDockerFact]
    public void DifferentImagesGetDifferentContainers() {
        var byDefault = LocalDynamoDb.EndpointFor(LocalDynamoDb.DefaultImage);
        var pinned = LocalDynamoDb.EndpointFor(PinnedImage);

        Assert.NotEqual(byDefault, pinned);
    }

    /// <summary>A pinned image is a working DynamoDB, not just a second endpoint.</summary>
    [RequiresDockerFact]
    public async Task APinnedImageServesRequestsToo() {
        using var client = LocalDynamoDb.CreateClient(PinnedImage);

        await CreateTable(client, "pinned");

        var tables = await client.ListTablesAsync();

        Assert.Contains("pinned", tables.TableNames);
    }

    private static Task CreateTable(IAmazonDynamoDB client, string name) =>
        client.CreateTableAsync(new CreateTableRequest {
            TableName = name,
            KeySchema = [new KeySchemaElement("id", KeyType.HASH)],
            AttributeDefinitions = [new AttributeDefinition("id", ScalarAttributeType.S)],

            // DynamoDB Local ignores throughput, but the API still requires it unless the table is
            // on-demand, and provisioned works on every version worth testing against.
            ProvisionedThroughput = new ProvisionedThroughput(1, 1),
        });
}

/// <summary>
/// Holds every container-backed test in one collection so they share the containers rather than
/// racing to start them, and so there is a single point at which the containers are stopped.
/// </summary>
[CollectionDefinition(Name)]
public sealed class LocalDynamoDbCollection : ICollectionFixture<LocalDynamoDbLifetime> {
    public const string Name = "local-dynamodb";
}

/// <summary>
/// Stops the containers when the collection is finished with them. Testcontainers' Ryuk would reap
/// them at the end of the run regardless; ending them here is what makes the lifetime something the
/// suite states rather than something a sidecar decides.
/// </summary>
public sealed class LocalDynamoDbLifetime : IDisposable {
    public void Dispose() => LocalDynamoDb.StopAll();
}
