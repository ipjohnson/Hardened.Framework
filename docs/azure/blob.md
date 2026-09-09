# Blobs

A blob handler receives the notification for one blob that was created or updated in a container:

```csharp
using Hardened.Functions.Runtime.Attributes;

public class UploadHandlers {

    [Blob("uploads")]
    public Task OnUpload(BlobNotification blob, IImporter importer) => importer.Import(blob.Container, blob.Name);
}

public record BlobNotification(string Container, string Name, long? Size, string? ContentType, string EventName);
```

## Packages

```xml
<PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="2.1.0" />
<PackageReference Include="Hardened.Azure.Functions.Runtime" Version="0.32.0-rc1000" />
<PackageReference Include="Hardened.Azure.Functions.Blobs" Version="0.32.0-rc1000" />
<PackageReference Include="Hardened.Azure.Functions.SourceGenerator" Version="0.32.0-rc1000" PrivateAssets="all" />
```

`dotnet new hardened-function --host azure --trigger blob` writes this shape with tests.

## The function

`[Blob("uploads")]` becomes `Blob_uploads`, bound by
`[BlobTrigger("uploads/{name}", Source = BlobTriggerSource.EventGrid)]` and routed as
`BLOB /uploads`. The source is Event Grid rather than the container scan: scanning is the
extension's fallback for accounts without events and can take minutes to notice a blob, and the
Flex Consumption plan does not offer it at all. The connection is the extension's default, the
function app's own `AzureWebJobsStorage`; a container in another account names its setting on
the module:

```csharp
[HardenedModule]
[BlobsModule(Connection = "UploadsStorage")]
public partial class Application;
```

## What the handler sees

**The body is the notification, not the blob.** Every other adapter hands a handler something a
publisher wrote; Storage sends a blob's name, and the handler decides whether to fetch it. So the
body is the same projection the S3 adapter binds, as JSON:

```json
{
  "container": "uploads",
  "name": "2026/09/orders.csv",
  "size": 18211,
  "contentType": "text/csv",
  "eTag": "\"0x8DD1F2A3B4C5D6E\"",
  "uri": "https://shopstorage.blob.core.windows.net/uploads/2026/09/orders.csv",
  "eventName": "BlobCreated"
}
```

The same facts as headers, for a handler that binds no body:

| Header | Carries |
|---|---|
| `x-azure-blob-container` | The container |
| `x-azure-blob-name` | The blob's name within it |
| `x-azure-blob-size` | The size in bytes, when the host said |
| `x-azure-blob-uri` | The blob's own URI |

The event is always `BlobCreated`: Storage raises the trigger for a created or updated blob and
never for a deletion. A handler that wants the content fetches it with the `BlobClient` the
application registers, which costs one request, and nothing is downloaded before the handler
asks. That is the one way a blob trigger differs from every other trigger in the framework.

## What a failure means

A thrown exception fails the invocation. The host retries the blob, up to five times by default,
and then records it as poison in the `webjobs-blobtrigger-poison` queue of the function app's
storage account rather than as handled. One blob per invocation, no batch.

## Deploying

The container and the event subscription that feeds the trigger. The subscription's endpoint is
the blob extension's webhook on the function app, with the function's name and the
`blobs_extension` system key:

```bash
az storage container create --account-name shopstorage --name uploads
KEY=$(az functionapp keys list --name shop --resource-group shop --query 'systemKeys.blobs_extension' --output tsv)
az eventgrid event-subscription create --name uploads-to-shop \
    --source-resource-id "$(az storage account show --name shopstorage --resource-group shop --query id --output tsv)" \
    --endpoint "https://shop.azurewebsites.net/runtime/webhooks/blobs?functionName=Host.Functions.Blob_uploads&code=$KEY" \
    --endpoint-type webhook \
    --included-event-types Microsoft.Storage.BlobCreated \
    --subject-begins-with /blobServices/default/containers/uploads/
```

The function has to be deployed before the subscription is created, because Event Grid validates
the endpoint. The storage account has to be general-purpose v2.

Locally there is no Event Grid. `func start` lists the function, and the extension's webhook can
be posted to by hand with a blob's path, which is what the Functions tooling's "execute function"
command does.

## Testing

```csharp
[HardenedTest]
public async Task AnUploadIsImported(Application.Blobs blobs, [Mock] IImporter importer) {
    await blobs.Uploads(new BlobNotification("uploads", "orders.csv", 18211, "text/csv", "BlobCreated"));

    await importer.Received().Import("uploads", "orders.csv");
}
```

Under `[assembly: AzureFunctionsTesting]` the delivery binds a `BlobClient` for the container and
name and hands the invocation handler the properties the host would send beside it, so the
headers and the projected body are there to assert on. See
[Testing Azure handlers](/azure/testing).

## Next

- [Events](/azure/event): Event Grid events that are not blob notifications
- [Triggers](/guide/triggers): the vocabulary
