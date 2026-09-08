# Blobs

`[Blob]` serves an object changing in a bucket. The notification is the message and the object is
not in it:

```csharp
using Hardened.Functions.Runtime.Attributes;

public class UploadHandlers {

    [Blob("uploads")]
    public Task OnUpload(Upload upload, IImporter importer) =>
        importer.Import(upload.Bucket, upload.Name);
}

public class Upload {
    public string Bucket { get; set; } = "";
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string EventType { get; set; } = "";
}
```

Cloud Storage sends the object's metadata and leaves fetching the object to the handler. The
handler declares a type with the properties it wants from the notification, the way it would
declare one for a queue message.

## Packages

```xml
<PackageReference Include="Hardened.Gcp.CloudRun.Runtime" Version="0.30.0-rc1000" />
<PackageReference Include="Hardened.Gcp.CloudRun.Storage" Version="0.30.0-rc1000" />
```

`dotnet new hardened-function --host gcp --trigger blob` writes this shape with tests.

## Two deliveries, one route

An object change reaches a service in either of two forms, and both are production paths. The
adapter reads both, and `[Blob("uploads")]` routes as `BLOB /uploads` either way.

**Through Eventarc**, as a CloudEvent typed `google.cloud.storage.object.v1.finalized`, `deleted`,
`archived` or `metadataUpdated`, with the bucket in `ce-source` and the object in `ce-subject`:

```bash
gcloud eventarc triggers create uploads-finalized \
    --location=us-central1 \
    --destination-run-service=importer \
    --event-filters="type=google.cloud.storage.object.v1.finalized" \
    --event-filters="bucket=uploads" \
    --service-account=eventarc-invoker@my-project.iam.gserviceaccount.com
```

**Through a Pub/Sub notification**, which the bucket publishes to a topic and a push subscription
delivers. The message's attributes carry `eventType`, `bucketId`, `objectId`, `objectGeneration`
and `eventTime`, and its data is the object's metadata:

```bash
gcloud storage buckets notifications create gs://uploads \
    --topic=uploads-notifications \
    --event-types=OBJECT_FINALIZE \
    --payload-format=json

gcloud pubsub subscriptions create uploads-to-importer \
    --topic=uploads-notifications \
    --push-endpoint="https://importer-abc123-uc.a.run.app/" \
    --push-auth-service-account=pubsub-pusher@my-project.iam.gserviceaccount.com
```

The notification form is also the one a local Pub/Sub emulator can deliver, which is why the
container tier of the tests uses it.

## What the handler sees

One notification, whichever form it arrived in, with Storage's own words for the fields:

| Field | |
|---|---|
| `bucket` | The bucket |
| `name` | The object's name |
| `size` | Bytes, when the metadata carried it |
| `contentType` | When the metadata carried it |
| `generation` | A string, because it is an int64 on the wire |
| `etag` | When the metadata carried it |
| `eventType` | `OBJECT_FINALIZE`, `OBJECT_DELETE`, `OBJECT_ARCHIVE` or `OBJECT_METADATA_UPDATE` |
| `eventTime` | When the change happened |
| `timeCreated`, `updated` | The object's timestamps, when carried |

The event type is spelled the notification's way in both forms, so a handler reads
`OBJECT_FINALIZE` whether Eventarc said `finalized` or the notification said `OBJECT_FINALIZE`.
The same five values are on the request as headers, `eventType`, `bucketId`, `objectId`,
`objectGeneration` and `eventTime`, for a filter that wants them without binding a body.

## What a failure means

A success answer acknowledges the delivery. A thrown exception answers 500, and Eventarc or the
subscription redelivers it. One object per delivery, no batch.

## Testing

```csharp
[HardenedTest]
public async Task ANotificationReachesTheHandler(Application.Blobs blobs, ImportLog log) {
    await blobs.Uploads(new Upload { Name = "report.pdf", Size = 1024 });

    Assert.Equal("report.pdf", Assert.Single(log.Imports).Name);
}
```

`Blobs` for `[Blob]`, with a method per bucket. Under `[assembly: CloudRunTesting]` the payload's
`name`, or `key`, and `size` are written into the object metadata of a `finalized` CloudEvent, so
the projection is exercised too. See [Testing Cloud Run handlers](/gcp/testing).

## Next

- [Events](/gcp/event): the Storage event without the adapter
- [Queues](/gcp/queue): what the notification's push subscription is
- [Triggers](/guide/triggers): the vocabulary
