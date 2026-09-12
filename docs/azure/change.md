# Changes

A change handler receives one changed document at a time, from the Cosmos DB change feed of one
container:

```csharp
using Hardened.Functions.Runtime.Attributes;

public class OrderProjection {

    [Change("orders")]
    public void OnOrderChanged(Order order, ISearchIndex index) => index.Upsert(order);
}
```

## Packages

```xml
<PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="2.1.0" />
<PackageReference Include="Hardened.Azure.Functions.Runtime" Version="0.34.0-rc1000" />
<PackageReference Include="Hardened.Azure.Functions.CosmosDb" Version="0.34.0-rc1000" />
<PackageReference Include="Hardened.Azure.Functions.SourceGenerator" Version="0.34.0-rc1000" PrivateAssets="all" />
```

`dotnet new hardened-function --host azure --trigger change` writes this shape with tests.

## The database is the module's

A container lives in a database, and the trigger names only the container, so the database is
written once on the application:

```csharp
[HardenedModule]
[CosmosDbModule(Database = "shop")]
public partial class Application;
```

Leaving it out is `HRDAZ003` at build. `[Change("orders")]` becomes `Change_orders`, bound by
`[CosmosDBTrigger("shop", "orders", CreateLeaseContainerIfNotExists = true)]` and routed as
`CHANGE /orders`. The connection is the extension's default, the app setting `CosmosDB`, and the
lease container is the extension's default, `leases`; both are module properties:

```csharp
[CosmosDbModule(Database = "shop", Connection = "ShopCosmos", LeaseContainer = "orders-leases")]
```

The lease container is created if it is missing, because it is the extension's own bookkeeping
and creating it is what lets a fresh deployment start reading. Two function apps reading the same
container need lease containers of their own, or the same container with different lease
prefixes, which is a setting the extension has and this line does not expose.

## What the handler sees

The body is the document as it is now, system properties included, so a handler binds its own
type against it and sees the properties it declares. `Content-Type` is `application/json`. Beside
it:

| Header | Carries |
|---|---|
| `x-azure-cosmos-id` | The document's `id` |
| `x-azure-cosmos-lsn` | The change's log sequence number, which is the position the feed is read by |
| `x-azure-cosmos-ts` | The change's `_ts`, epoch seconds |
| `x-azure-cosmos-etag` | The document's `_etag` after the change |

**The current document only.** The change feed carries what a document is now, not what it was.
There is no old image, so `[OldImage]` on DynamoDB and the old value Firestore carries have no
counterpart here. A handler that needs to know what changed keeps its own last-seen copy or
reads the previous version from a store of its own. Deletions are not in the feed either, unless
the container has the all-versions-and-deletes mode on, which this line does not bind.

## What a failure means

The batch is ordered per partition, and the pipeline stops at the first failure rather than
applying a newer version of a document before the replay of an older one. The failed invocation
is what the host logs and counts.

**What the extension does next is not what DynamoDB Streams does.** The extension checkpoints
the lease after each function call, failed or not, so a thrown batch is not delivered again
unless the function app declares a retry policy, and then only until that policy is spent.
DynamoDB Streams on Lambda retries a failed batch until it succeeds or expires. A `[Change]`
handler that has to see every change therefore needs a retry policy on Azure and a dead-letter
path of its own; Microsoft's own page on the trigger lists a retry policy as its only retry.

That is `Checkpoint` in the framework's
[vocabulary](/guide/triggers#batches-and-what-a-failure-means) with nothing to report to, the
same as [Streams](/azure/stream).

## Retrying a failed batch

The retry policy is written on the module, beside the database:

```csharp
[HardenedModule]
[CosmosDbModule(Database = "shop", RetryCount = 5, RetryDelay = "00:00:10")]
public partial class Application;
```

The generator writes it as the worker's `[FixedDelayRetry(5, "00:00:10")]` on every change
function and into the metadata the host indexes, so the host invokes a thrown batch again up to
five times, ten seconds apart, before the extension checkpoints the lease past it. The two
properties go together; one without the other is `HRDAZ003`. A batch still failing when the
policy is spent is not delivered again.

## Deploying

```bash
az cosmosdb sql database create --account-name shop-cosmos --resource-group shop --name shop
az cosmosdb sql container create --account-name shop-cosmos --resource-group shop \
    --database-name shop --name orders --partition-key-path /id
az functionapp config appsettings set --name shop --resource-group shop \
    --settings "CosmosDB=$(az cosmosdb keys list --name shop-cosmos --resource-group shop \
        --type connection-strings --query 'connectionStrings[0].connectionString' --output tsv)"
```

Locally the Cosmos DB emulator answers the same setting.

## Testing

```csharp
[HardenedTest]
public async Task AChangedOrderIsIndexed(Application.Changes changes, ISearchIndex index) {
    await changes.Orders(new Order { Id = "A-1", Status = "shipped" });

    Assert.Equal("shipped", index.Get("A-1").Status);
}
```

Several payloads are one batch. Under `[assembly: AzureFunctionsTesting]` the batch is the JSON
array the host sends, with `_lsn`, `_ts` and `_etag` stamped on each document, so the headers are
there to assert on. See [Testing Azure handlers](/azure/testing).

## Next

- [Streams](/azure/stream): the other ordered log, with the same failure rule
- [Triggers](/guide/triggers): the vocabulary
