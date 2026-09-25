# Blobs

`[Blob("uploads")]` on a method makes it the handler for new and replaced blobs in the Blob Storage
container `uploads`. The handler receives a description of the blob: its container, its name, its
size and its content type. The description does not include the blob's content.

The `hardened-function` template writes this handler to `src/Orders/OrderHandler.cs`:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Blob("uploads")]
    public void OnUpload(Upload upload) => log.Record(upload);
}
```

In the template, `OrderLog` is a `[SingletonService]` that keeps the uploads it is given. `Upload` is
in `src/Orders/Order.cs`:

```csharp
namespace Orders;

public class Upload
{
    public string Bucket { get; set; } = "";

    public string Key { get; set; } = "";

    public long Size { get; set; }
}
```

The application class in `src/Orders/Application.cs` names no module:

```csharp
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
public partial class Application;
```

`Hardened.Azure.Functions.Blobs` makes `[Blob]` mean Blob Storage. When the project references it,
the build registers the package's `BlobsModule` on the application and writes the function. Event
Grid sends a notification to the function app. The host runs the function that the build wrote for
the container. Each notification is one invocation of the function, for one blob. The handler runs
once for it.

[Triggers](/guide/triggers) covers `[Blob]` and the other trigger attributes.

## Packages

This command writes the function app and a test project:

```bash
dotnet new hardened-function -n Orders --host azure --trigger blob
```

A blob function app references `Hardened.Azure.Functions.Blobs` beside
`Hardened.Azure.Functions.Runtime`:

```xml
<PackageReference Include="Hardened.Azure.Functions.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Azure.Functions.Blobs" Version="0.0.0-HARDENED-VERSION" />
```

`Hardened.Azure.Functions.Blobs` brings the worker's Blob Storage extension. The build adds the
host's Blob Storage extension to the function app.

The other packages, `Program.cs`, `host.json` and the project settings are the same for every Azure
function app. Those packages include `Microsoft.Azure.Functions.Worker.Sdk` and
`Hardened.Azure.Functions.SourceGenerator`. The Azure [Overview](/azure/) covers them.

## The function for each container

The build writes one function for each `[Blob]` handler. The function's name is `Blob_` followed by
the container's name. Each character of the container's name that is not a letter or a digit
becomes `_`. `[Blob("order-uploads")]` gives `Blob_order_uploads`.

The build writes this function for `[Blob("uploads")]`:

| | Value |
|---|---|
| Function | `Blob_uploads`, known to the host as `Host.Functions.Blob_uploads` |
| Trigger | A blob trigger on `uploads/{name}`, with Event Grid as its source |
| What the function binds | The blob as a `BlobClient` |
| Connection | `AzureWebJobsStorage`, or the setting `[BlobsModule]` names |
| Route | `BLOB /uploads` |

The `BlobClient` downloads nothing. One app can serve several containers, each through its own
function.

`func start` lists the function after the build output:

```text
Functions:

	Blob_uploads: blobTrigger
```

## Notifications

Event Grid delivers a notification to the host's blob webhook, `/runtime/webhooks/blobs`, with the
function's name in `functionName`. The host then runs that function. The webhook takes the
function's name with the prefix `Host.Functions.`.

This `Upload` has two more properties:

```csharp
namespace Orders;

public class Upload
{
    public string Bucket { get; set; } = "";

    public string Key { get; set; } = "";

    public long Size { get; set; }

    public string ContentType { get; set; } = "";

    public string Uri { get; set; } = "";
}
```

This handler logs what it receives:

```csharp
using Hardened.Functions.Runtime.Attributes;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Blob("uploads")]
    public void OnUpload(Upload upload)
    {
        logger.LogInformation(
            "{Key}, {Size} bytes, {ContentType}, {Uri}",
            upload.Key,
            upload.Size,
            upload.ContentType,
            upload.Uri
        );

        log.Record(upload);
    }
}
```

The app runs under `func start`. The container `uploads` in Azurite holds an 18-byte `text/csv` blob
`2026/09/orders.csv`. This notification has the shape of Azure's sample
`Microsoft.Storage.BlobCreated` event:

```http
POST /runtime/webhooks/blobs?functionName=Host.Functions.Blob_uploads HTTP/1.1
Host: localhost:7071
aeg-event-type: Notification
Content-Type: application/json

[{
  "topic": "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/orders/providers/Microsoft.Storage/storageAccounts/ordersstorage",
  "subject": "/blobServices/default/containers/uploads/blobs/2026/09/orders.csv",
  "eventType": "Microsoft.Storage.BlobCreated",
  "eventTime": "2026-09-24T13:45:07.9584103Z",
  "id": "831e1650-001e-001b-66ab-eeb76e069631",
  "data": {
    "api": "PutBlob",
    "clientRequestId": "6d79dbfb-0e37-4fc4-981f-442c9ca65760",
    "requestId": "831e1650-001e-001b-66ab-eeb76e000000",
    "eTag": "0x8DD1F2A3B4C5D6E",
    "contentType": "text/csv",
    "contentLength": 18,
    "blobType": "BlockBlob",
    "url": "https://ordersstorage.blob.core.windows.net/uploads/2026/09/orders.csv",
    "sequencer": "00000000000004420000000000028963",
    "storageDiagnostics": {
      "batchId": "b68529f3-68cd-4744-baa4-3c0498ec19f0"
    }
  },
  "dataVersion": "",
  "metadataVersion": "1"
}]

HTTP/1.1 202 Accepted
```

The host answers 202 with no body, and then runs the function. The handler logs this line:

```text
2026/09/orders.csv, 18 bytes, text/csv, http://127.0.0.1:10000/devstoreaccount1/uploads/2026/09/orders.csv
```

A POST needs the header `aeg-event-type: Notification`. Without it, the host answers 400. A POST can
carry several events. Each event runs the function. Under `func start`, the webhook needs no key.
In Azure, the webhook's URL carries the `blobs_extension` system key in `code`, as
[Deploying](#deploying) shows. The host takes a notification in the Event Grid schema or in the
CloudEvents schema.

The host reads the container and the blob's name from the notification's `url`. It reads the blob
from the storage account that the function's connection names, whatever account the notification
came from. The host does not compare the notification's container with the function's. The function
named in `functionName` runs for whatever blob the notification names. A notification for a blob in
the container `raw`, sent to `Host.Functions.Blob_uploads`, runs the `uploads` handler with `bucket`
set to `raw`. The subscription's filter keeps other containers out.

The host reads no event type. A notification of any type that names an existing blob runs the
function. A notification for a blob that no longer exists, as after a delete, runs nothing.
`func start` logs nothing about it.

Under `func start`, a blob uploaded to Azurite runs nothing. The function runs only when a
notification reaches the webhook. The Azure [Overview](/azure/) covers running the app locally, and
running a function through the host's admin endpoint.

Azure documents the Blob Storage events and their fields in
[Azure Blob Storage as Event Grid source](https://learn.microsoft.com/en-us/azure/event-grid/event-schema-blob-storage).
A subscription can also send a storage account's events to the app's `Event` function, where an
`[Event]` handler receives the event itself. [Events](/azure/event) covers it.

## The request body and headers

For each blob, the adapter writes a JSON object with these fields. That object is the request body.

| Field | Holds |
|---|---|
| `container`, `bucket` | The container's name |
| `name`, `key` | The blob's name, decoded |
| `size` | The blob's size in bytes |
| `contentType` | The blob's content type |
| `eTag` | `null` |
| `uri` | The blob's URI in the storage account of the function's connection, with its escapes decoded |
| `eventName` | `BlobCreated` |

`size` and `contentType` are the blob's own. The host reads them from the storage account when the
function runs. The notification's `contentLength` and `contentType` do not reach the handler. `uri`
has its escapes decoded, so `%20` becomes a space. `eTag` is `null` in a delivery from the host.
`eventName` is always `BlobCreated`. The notification's own fields, such as `api`, `sequencer` and
`eventTime`, do not reach the handler.

[Triggers](/guide/triggers) covers how the body binds to the handler's parameter. It also covers the
fields `bucket`, `key` and `size`, which have the same names on every cloud.

The adapter also sets these headers:

| Header | Value |
|---|---|
| `Content-Type` | `application/json` |
| `x-azure-blob-container` | The container's name |
| `x-azure-blob-name` | The blob's name, decoded |
| `x-azure-blob-size` | The blob's size in bytes |
| `x-azure-blob-uri` | The blob's URI, as in the body |

A handler reads them from the `Headers` of an `IExecutionRequest` parameter, which is in
`Hardened.Requests.Abstract.Execution`. [Queues](/azure/queue) shows a handler that reads headers. Header names match without regard to case. The handler sees
none of the headers of the webhook's request, such as `aeg-event-type`.

## Failures and redelivery

The webhook answers 202 once it has queued the blob for the function, before the function runs. The
answer does not depend on what the handler does. Event Grid counts 200 to 204 as a delivered event
([Azure Event Grid Delivery and Retry Explained](https://learn.microsoft.com/en-us/azure/event-grid/delivery-and-retry)).
A failing handler therefore does not make Event Grid send the notification again.

| Case | The webhook answers | What the host does |
|---|---|---|
| The handler returns | 202 | Runs the function once |
| The handler throws, or the body does not bind | 202 | Runs the function five times in all, then writes a message to `webjobs-blobtrigger-poison` |
| The blob no longer exists | 202 | Nothing |
| `functionName` names no function of the app | 202 | Nothing |
| The request has no `aeg-event-type` header | 400 | Nothing |
| In Azure, `code` is missing | 401 | Nothing |
| In Azure, `code` is a function key | 403 | Nothing |

::: warning
The webhook answers 202 to a notification whose `functionName` does not name a function of the app,
such as `functionName=Blob_uploads` without `Host.Functions.`, and nothing runs. A subscription with
that endpoint drops every blob without an error.
:::

A blob fails when its handler throws, or when its body does not bind to the handler's parameter.
The invocation then fails with that exception. The host runs the function again at once. After five
attempts in all, it writes a message naming the blob to the queue `webjobs-blobtrigger-poison`
([Azure Blob storage trigger for Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-blob-trigger)).
It logs this line:

```text
Message has reached MaxDequeueCount of 5. Moving message to queue 'webjobs-blobtrigger-poison'.
```

The host makes no more attempts for that blob. The poison queue is in the storage account that the
function's connection names. The connection is `AzureWebJobsStorage` unless `[BlobsModule]` names
another. The poison message is JSON with these fields:

| Field | Holds |
|---|---|
| `Type` | `BlobTrigger` |
| `FunctionId` | `Host.Functions.` and the function's name, such as `Host.Functions.Blob_uploads` |
| `BlobType` | `BlockBlob` |
| `ContainerName` | The container |
| `BlobName` | The blob's name |
| `ETag` | The entity tag the notification carried, in quotes |

`poisonBlobThreshold` under `extensions.blobs` in `host.json` sets the number of attempts
([Azure Blob storage trigger and bindings for Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-blob)).
Its default is 5. This `src/Orders/host.json` allows two attempts for each blob:

```json
{
  "version": "2.0",
  "extensions": {
    "blobs": {
      "poisonBlobThreshold": 2
    }
  }
}
```

The host writes no blob receipt for a notification that comes through the webhook. A second
notification for the same blob runs the function again. Event Grid delivers each event at least
once. A handler can therefore receive the same blob more than once.

Event Grid retries a delivery when the webhook fails, such as when the function app is stopped. It
drops an event that the webhook answers with 400, 401 or 403, unless the subscription has a
dead-letter destination.

`BlobsModule` has no retry setting. The function that the build writes declares no retry policy.
Each invocation is one blob, so the function has no batch and no report of failed items.

## The storage connection

`[BlobsModule(Connection = "UploadsStorage")]` on the application names the app setting that holds
the storage account's connection. Without it, the host uses `AzureWebJobsStorage`, the function
app's own storage account. `BlobsModule` is in the namespace `Hardened.Azure.Functions.Blobs`.

This `src/Orders/Application.cs` names the setting `UploadsStorage`:

```csharp
using Hardened.Azure.Functions.Blobs;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[BlobsModule(Connection = "UploadsStorage")]
public partial class Application;
```

The build writes the setting's name into the trigger as `Connection = "UploadsStorage"`, and into
the metadata as `"connection": "UploadsStorage"`. Every blob function of the app uses it. With the
setting, the host reads the blobs from that account. It puts the poison queue there too.

The setting holds a connection string. Under `func start`, it goes in the `Values` of
`local.settings.json`. The setting can instead be the prefix of a group of settings for an
identity-based connection
([Azure Blob storage trigger for Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-blob-trigger)).

A missing setting disables the function. The host starts, and logs these two messages:

```text
Function 'Functions.Blob_uploads' failed indexing and will be disabled.
Storage account connection string 'AzureWebJobsUploadsStorage' does not exist. Make sure that it is a defined App Setting.
```

The webhook then answers 202 and runs nothing.

Write the value as a string literal. A constant or any other expression fails the build with
`HRDAZ004`. With `Connection = Settings.Uploads`, where `Settings.Uploads` is a `const string`, the
build reports this error:

```text
error HRDAZ004: Connection on [BlobsModule] is written as global::Orders.Settings.Uploads, and the function metadata the host indexes needs its value at build. Write it as a string literal, an integer literal, or true or false.
```

## Deploying

An Event Grid event subscription on the storage account sends its blob events to the function app's
blob webhook
([Tutorial: Trigger Azure Functions on blob containers using an event subscription](https://learn.microsoft.com/en-us/azure/azure-functions/functions-event-grid-blob-trigger)).

Storage raises events only in a general-purpose v2, BlockBlobStorage or BlobStorage account
([Azure Blob Storage as Event Grid source](https://learn.microsoft.com/en-us/azure/event-grid/event-schema-blob-storage)).
The blob trigger does not support a premium account
([Azure Blob storage trigger and bindings for Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-blob)).
The storage account must therefore be a standard general-purpose v2 account. The Flex Consumption
plan supports only the event-based blob trigger, which is the one the build writes.

The function app itself deploys the same way for every trigger. The Azure [Overview](/azure/)
covers it. Event Grid validates the endpoint when the subscription is created
([Validate Webhook Endpoints with Event Grid Schema](https://learn.microsoft.com/en-us/azure/event-grid/end-point-validation-event-grid-events-schema)).
The host answers the validation itself. Deploy and start the function app before you create the
subscription.

The subscription's endpoint is
`https://{function-app}.azurewebsites.net/runtime/webhooks/blobs?functionName=Host.Functions.Blob_uploads&code={key}`,
of the endpoint type `webhook`. `code` is the function app's system key `blobs_extension`, which
`az functionapp keys list --query systemKeys.blobs_extension` reads. A subscription's endpoint URL
cannot be changed after it is created.

These commands create the container, read the key and create the subscription.
`az storage container-rm create` creates the container through Resource Manager. `orders` is the
function app and the resource group, `ordersstorage` the storage account, and `uploads-to-orders` a
subscription name of your choosing.

```bash
az storage container-rm create --storage-account ordersstorage --resource-group orders --name uploads

KEY=$(az functionapp keys list --name orders --resource-group orders --query systemKeys.blobs_extension --output tsv)

az eventgrid event-subscription create --name uploads-to-orders \
    --source-resource-id "$(az storage account show --name ordersstorage --resource-group orders --query id --output tsv)" \
    --endpoint "https://orders.azurewebsites.net/runtime/webhooks/blobs?functionName=Host.Functions.Blob_uploads&code=$KEY" \
    --endpoint-type webhook \
    --included-event-types Microsoft.Storage.BlobCreated \
    --subject-begins-with /blobServices/default/containers/uploads/
```

The two filters limit what the subscription sends:

| Option | Lets through |
|---|---|
| `--included-event-types Microsoft.Storage.BlobCreated` | Only created and replaced blobs |
| `--subject-begins-with /blobServices/default/containers/uploads/` | Only the blobs of the container `uploads` |

The host runs the function for any event that names an existing blob, in any container. The
subscription delivers in the Event Grid schema unless it names another. The CLI sends the default
retry policy, `"maxDeliveryAttempts": 30` and `"eventTimeToLiveInMinutes": 1440`.

The commands send these requests:

| Command | Request |
|---|---|
| `az storage container-rm create` | `PUT /subscriptions/{subscription}/resourceGroups/orders/providers/Microsoft.Storage/storageAccounts/ordersstorage/blobServices/default/containers/uploads?api-version=2025-08-01` |
| `az functionapp keys list` | `POST /subscriptions/{subscription}/resourceGroups/orders/providers/Microsoft.Web/sites/orders/host/default/listkeys?api-version=2025-05-01` |
| `az eventgrid event-subscription create` | `GET .../storageAccounts/ordersstorage` for the account's ID, then `PUT .../storageAccounts/ordersstorage/providers/Microsoft.EventGrid/eventSubscriptions/uploads-to-orders` |

`az functionapp keys list` prints the `blobs_extension` value of the answer. The `PUT` of
`az eventgrid event-subscription create` carries the destination, the two filters and the retry
policy. That command prints a warning that a subscription on an Azure topic validates its webhook
endpoint.

## Testing

The template writes this test to `tests/Orders.Tests/OrderHandlerTests.cs`. It sends a blob through
`Application.Blobs`.

```csharp
using DependencyModules.xUnit.Attributes;
using Xunit;

namespace Orders.Tests;

public class OrderHandlerTests
{
    [ModuleTest]
    public async Task ANotificationReachesTheHandler(Application.Blobs blobs, OrderLog log)
    {
        await blobs.Uploads(new Upload { Key = "report.pdf", Size = 1024 });

        Assert.Equal("report.pdf", Assert.Single(log.Uploads).Key);
    }
}
```

The template's test project declares `[assembly: AzureFunctionsTesting]` beside
`[assembly: FunctionTesting]`. Under `[AzureFunctionsTesting]`, each message of a call is one
invocation of the blob's function. The invocation carries a `BlobClient` for the blob, and the
blob's properties beside it. The request body holds these values:

| Field | Value |
|---|---|
| `container`, `bucket` | The name in `[Blob]`, whatever the message's `Bucket` says |
| `name`, `key` | The message's `name`, else the message's `key`, else `blob-0`, `blob-1` in order. An empty `name` counts as none |
| `size` | The message's `size`, or 0 |
| `contentType` | `application/octet-stream` |
| `eTag` | The message's position in 16 hexadecimal digits, in quotes, such as `"0x0000000000000000"` |
| `uri` | `https://devstoreaccount1.blob.core.windows.net/` followed by the container and the name |
| `eventName` | `BlobCreated`, whatever the message's `eventName` |

Under `[FunctionTesting]` alone, the handler binds the message as the test wrote it. The request has
no headers. `Bucket` is empty unless the test sets it.

A failed blob fails the call with the handler's exception, under both. The messages after it in the
call do not run. The template's test passes with the logging handler in
[Notifications](#notifications), under `[AzureFunctionsTesting]` and under `[FunctionTesting]` alone.

[Testing functions](/guide/testing-functions) covers the façades. [Testing](/azure/testing) covers
`[AzureFunctionsTesting]`.

## Next

| Page | Covers |
|---|---|
| [Triggers](/guide/triggers) | The trigger attributes, payload binding and the blob fields every cloud shares |
| [Overview](/azure/) | The packages, the application class, the entry point, running locally and deploying |
| [Events](/azure/event) | Event Grid events, including a storage account's own events through `[Event]` |
| [Testing functions](/guide/testing-functions) | Testing a trigger handler |
| [Testing](/azure/testing) | What `[AzureFunctionsTesting]` builds |
