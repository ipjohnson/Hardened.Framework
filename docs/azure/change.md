# Changes

`[Change("orders")]` on a method makes it the handler for changes to the documents in the Cosmos DB
container `orders`. The Azure Functions host reads those changes from the container's change feed.

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Change("orders")]
    public void OnOrderChanged(Order order) => log.Record(order);
}
```

A container lives in a database. The application names the database on `[CosmosDbModule]`:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Azure.Functions.CosmosDb;

namespace Orders;

[HardenedModule]
[CosmosDbModule(Database = "Orders")]
public partial class Application;
```

The `hardened-function` template writes this handler and this application. In the template, `Order`
has a string `Id` and an int `Quantity`. `OrderLog` is a `[SingletonService]` that keeps the orders
it is given.

The host invokes the function with a batch of changed documents. The handler runs once for each
document, in the batch's order.

The handler's parameter binds the changed document. The feed carries the document as plain JSON.
For a document written with the id `A-1` and the quantity 2, `Order` gets `Id` `A-1` and
`Quantity` 2.

[Triggers](/guide/triggers) covers `[Change]` and compares the change feeds of the three clouds.

## Packages

A change function app references `Hardened.Azure.Functions.CosmosDb` beside
`Hardened.Azure.Functions.Runtime`:

```xml
<PackageReference Include="Hardened.Azure.Functions.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Azure.Functions.CosmosDb" Version="0.0.0-HARDENED-VERSION" />
```

The reference to the Cosmos DB package makes `[Change]` mean the Cosmos DB change feed. The
package's build file names `CosmosDbModule` as the module for `[Change]`. The package also brings the
worker's Cosmos DB extension, `Microsoft.Azure.Functions.Worker.Extensions.CosmosDB` 4.16.1. The
build adds the host's Cosmos DB extension 4.16.1 to the function app.

The Worker SDK, the generator package, `Program.cs` and the project settings are the same for every
Azure function app. The Azure [Overview](/azure/) covers them.

This command writes the function app and a test project:

```bash
dotnet new hardened-function -n Orders --host azure --trigger change
```

## Functions and routes

The build writes an Azure function for each `[Change]` handler, with the module's settings. It names
the function for the handler's container, so `[Change("orders")]` gives `Change_orders`. The Azure
[Overview](/azure/) covers how functions are named. `func start` lists the function as
`Change_orders: cosmosDBTrigger`.

The function carries the Cosmos DB extension's trigger for the module's database and the handler's
container. The trigger asks the extension to create the lease container when it is missing. The
build describes the trigger's binding to the host in `src/Orders/bin/Debug/net8.0/functions.metadata`:

```json
{
  "name": "documents",
  "direction": "In",
  "type": "cosmosDBTrigger",
  "dataType": "String",
  "databaseName": "Orders",
  "containerName": "orders",
  "createLeaseContainerIfNotExists": true,
  "properties": {}
}
```

The host binds each function to one container. The route comes from the function: every document
that `Change_orders` receives goes to `[Change("orders")]`, under the route `CHANGE /orders`.

One function app can serve several containers of its database, with a `[Change]` handler for each.
Each handler gets its own function. Every `[Change]` handler of an application reads the one
database that the module names.

## The document and its headers

The request body is the changed document as the feed carries it. The body holds the document's own
properties, then the system properties that Cosmos DB adds, such as `_etag`, `_ts` and `_lsn`. The
handler's parameter binds the properties its type declares. [Triggers](/guide/triggers) covers how
the body binds to the handler's parameter.

The body is the document after the change. The feed carries no earlier version of it. The feed does
not say whether a change was an insert or an update. A deleted document does not reach the handler.
Several writes to one document between two reads of the feed reach the handler once, as the latest
version.

Changes to documents with the same partition key value arrive in the order they were made. Changes
with different values have no guaranteed order.

The Cosmos DB adapter adds a `Content-Type` header and four headers from the document's system
properties. Header names are matched without regard to case.

| Header | Carries |
|---|---|
| `x-azure-cosmos-id` | The document's `id` |
| `x-azure-cosmos-lsn` | The document's `_lsn`, a batch ID the change feed adds. Many documents can have the same one |
| `x-azure-cosmos-ts` | The document's `_ts`, the time stamp of the change, in epoch seconds |
| `x-azure-cosmos-etag` | The document's `_etag`, the version Cosmos DB uses for concurrency control |
| `Content-Type` | `application/json` |

The `_etag` format is internal to Cosmos DB and can change.

A handler reads the headers through a parameter of type `IExecutionRequest`, from the namespace
`Hardened.Requests.Abstract.Execution`. This handler logs the time stamp and the LSN of each change:

```csharp
using Hardened.Functions.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Change("orders")]
    public void OnOrderChanged(Order order, IExecutionRequest request)
    {
        request.Headers.TryGetValue("x-azure-cosmos-ts", out var timestamp);
        request.Headers.TryGetValue("x-azure-cosmos-lsn", out var lsn);

        logger.LogInformation(
            "Order {Id} has quantity {Quantity} at {Timestamp}, LSN {Lsn}",
            order.Id,
            order.Quantity,
            timestamp.ToString(),
            lsn.ToString()
        );

        log.Record(order);
    }
}
```

The handler reads the headers with `TryGetValue`, because under `[FunctionTesting]` alone the
request has none.

## Failed documents

A document fails when its handler throws or when the document does not bind. The first failed
document fails the whole invocation with its exception. The documents after it in the batch do not
run. The request log names the failed route, such as `CHANGE /payments request failed`.

The function reports no single document back to the host. `ReportsItemFailures` is false and cannot
be turned on. A change batch has the `Checkpoint` failure mode. [Triggers](/guide/triggers) covers
`BatchFailureMode`.

`RetryCount` and `RetryDelay` on `[CosmosDbModule]` give the change functions a retry policy. The
Azure [Overview](/azure/) covers setting it. The host handles a failed batch in one of two ways:

| Case | No retry policy | `RetryCount` and `RetryDelay` set |
|---|---|---|
| A handler throws, or a document does not bind | The invocation fails, the later documents do not run, and the feed moves past the batch | The host runs the whole batch again, up to `RetryCount` times, `RetryDelay` apart, then moves past it |

Without a retry policy, the host does not deliver a failed batch again. The failed document and
every document after it in the batch are never handled.

With a retry policy, the documents that ran before the failure run again. Later changes wait until
the retries end. The same batch can also reach the function again after a function run passes its
time limit.

Microsoft's documentation covers
[the trigger](https://learn.microsoft.com/azure/azure-functions/functions-bindings-cosmosdb-v2-trigger),
[its failures](https://learn.microsoft.com/azure/cosmos-db/troubleshoot-changefeed-functions) and
[retry policies](https://learn.microsoft.com/azure/azure-functions/functions-bindings-error-pages).

## Module settings

`CosmosDbModule` is in the namespace `Hardened.Azure.Functions.CosmosDb`. Its settings apply to every
`[Change]` function of the application.

| Property | Default | What it sets |
|---|---|---|
| `Database` | None. The build fails with `HRDAZ003` | The database the containers are in |
| `Connection` | `CosmosDB` | The app setting that holds the account's connection string |
| `LeaseContainer` | `leases` | The container, in the same database, where the function keeps its place in the feed |
| `RetryCount`, `RetryDelay` | None | The retry policy in [Failed documents](#failed-documents). The Azure [Overview](/azure/) covers it |

`Database` is the one required setting. Without it, the build fails:

```text
error HRDAZ003: The [Change] handlers need Database on [CosmosDbModule], and this application does not set it. Write [CosmosDbModule(Database = "...")] on the application, beside [HardenedModule].
```

The build writes `Connection` and `LeaseContainer` into each function's trigger. One lease container
serves every `[Change]` function of the application. This application names its own connection
setting and lease container:

```csharp
using Hardened.Azure.Functions.CosmosDb;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[CosmosDbModule(Database = "Orders", Connection = "ShopCosmos", LeaseContainer = "orders-leases")]
public partial class Application;
```

Its build writes this trigger binding to `functions.metadata`:

```json
{
  "name": "documents",
  "direction": "In",
  "type": "cosmosDBTrigger",
  "dataType": "String",
  "databaseName": "Orders",
  "containerName": "orders",
  "connection": "ShopCosmos",
  "leaseContainerName": "orders-leases",
  "createLeaseContainerIfNotExists": true,
  "properties": {}
}
```

## Deploying

The container named in `[Change]` has to exist before the function starts. Without it, the host
logs a message such as `Collection 'orders' not found in database 'Shop'`, then
`The listener for function 'Functions.Change_orders' was unable to start.`

On its first start, the function creates the lease container, and the database when that is
missing. A lease container created by hand needs the partition key `/id`.

The app setting that `Connection` names holds the account's connection string. That setting is
`CosmosDB` by default. Without it, the host logs `Error indexing method 'Functions.Change_orders'`
with this message, and disables the function:

```text
Cosmos DB connection configuration 'CosmosDB' does not exist. Make sure that it is a defined App Setting.
```

These commands create the database and the container. The last command stores the account's
connection string in the function app's `CosmosDB` setting. `orders-cosmos` is the account. `orders`
is the function app and its resource group.

```bash
az cosmosdb sql database create --account-name orders-cosmos --resource-group orders --name Orders

az cosmosdb sql container create --account-name orders-cosmos --resource-group orders \
    --database-name Orders --name orders --partition-key-path /id

az functionapp config appsettings set --name orders --resource-group orders \
    --settings "CosmosDB=$(az cosmosdb keys list --name orders-cosmos --resource-group orders \
        --type connection-strings --query 'connectionStrings[0].connectionString' --output tsv)"
```

The generated function always asks the extension to create its lease container. A connection
through a Microsoft Entra identity cannot create containers. On such a connection, a function that
asks fails to start with `Forbidden (403); Substatus: 5300`.

Locally, `CosmosDB` can name the Linux emulator,
`mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview`. It serves HTTP on port 8081
by default, and its connection string is:

```text
AccountEndpoint=http://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;
```

Microsoft's documentation covers [the emulator](https://learn.microsoft.com/azure/cosmos-db/emulator).
The Azure [Overview](/azure/) covers running the function app locally and deploying it. Both are the
same for every trigger.

## Reading the feed

On its first start, the function reads the changes made from then on. Documents written earlier do
not reach the handler. The module has no setting to start from the beginning of the feed. After that
first start, a restarted function continues from the place its lease container records.

When the feed has no new changes, the function checks it again after 5 seconds. A change can take
that long to reach the handler.

Two function apps with the same settings share the lease container. Each change reaches only one of
them. The other function app logs nothing. The function sets no lease prefix. Function apps with
different `LeaseContainer` values each receive every change.

::: warning
A copy of the function started with `func start` against the deployed account, with the deployed
settings, shares the deployed function's lease container. The copy takes changes from the deployed
function. The deployed function never sees those changes. Neither copy logs an error. Point a local
copy at the emulator, or give it its own `LeaseContainer` on `[CosmosDbModule]`.
:::

Microsoft's documentation covers [the change feed](https://learn.microsoft.com/azure/cosmos-db/change-feed)
and [change feed modes](https://learn.microsoft.com/azure/cosmos-db/change-feed-modes).

## Testing the handler

The template writes this test in `tests/Orders.Tests/OrderHandlerTests.cs`. The test sends a change
through the façade `Application.Changes`:

```csharp
using DependencyModules.xUnit.Attributes;
using Xunit;

namespace Orders.Tests;

public class OrderHandlerTests
{
    [ModuleTest]
    public async Task AChangedRowReachesTheHandler(Application.Changes changes, OrderLog log)
    {
        await changes.Orders(new Order { Id = "A-1", Quantity = 2 });

        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

The template's test project declares `[assembly: AzureFunctionsTesting]`. The attribute delivers each
façade call to the function as one change feed batch. The batch is a JSON array with a document for
each message. Each document has `_rid`, `_etag`, `_ts` and `_lsn` added. Each request carries the
headers listed in [The document and its headers](#the-document-and-its-headers).

Under `[FunctionTesting]` alone, the request has no headers, and the body has no system properties.
The handler that reads the headers passes the template's test under both.

The delivery decides what a failed message does to the façade call.
[Testing functions](/guide/testing-functions) covers it. The Azure [Testing](/azure/testing) page
covers what `[AzureFunctionsTesting]` builds.

## Next

- [Triggers](/guide/triggers): the trigger attributes, and the change feeds of the three clouds
- [Streams](/azure/stream): Event Hubs, the other source Azure delivers in order
- [Overview](/azure/): the packages, the entry point, running locally and the retry policy
- [Testing functions](/guide/testing-functions): testing a trigger handler
- [Testing](/azure/testing): testing on Azure
