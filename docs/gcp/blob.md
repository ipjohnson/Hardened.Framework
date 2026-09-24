# Blobs

`[Blob("uploads")]` on a method makes it the handler for changes to objects in the Cloud Storage
bucket named `uploads`. On Cloud Run and on Cloud Functions 2nd gen, a change reaches the service as
an HTTP request in one of two forms: an Eventarc CloudEvent, or a Pub/Sub push of the bucket's
notification.

The `hardened-function` template writes this handler and its `Upload` model for `--trigger blob`:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Blob("uploads")]
    public void OnUpload(Upload upload) => log.Record(upload);
}
```

```csharp
namespace Orders;

public class Upload
{
    public string Bucket { get; set; } = "";

    public string Key { get; set; } = "";

    public long Size { get; set; }
}
```

In the template, `OrderLog` is a `[SingletonService]` that keeps the uploads it is given. Eventarc
sends this request when `reports/2026-09.pdf` is written to the bucket `uploads`:

```http
POST /
Content-Type: application/json
ce-specversion: 1.0
ce-id: 9147286335406711
ce-source: //storage.googleapis.com/projects/_/buckets/uploads
ce-type: google.cloud.storage.object.v1.finalized
ce-subject: objects/reports/2026-09.pdf
ce-time: 2026-09-24T11:12:11.207Z
ce-bucket: uploads

{
  "kind": "storage#object",
  "id": "uploads/reports/2026-09.pdf/1758712331205187",
  "name": "reports/2026-09.pdf",
  "bucket": "uploads",
  "generation": "1758712331205187",
  "metageneration": "1",
  "contentType": "application/pdf",
  "size": "1305107",
  "etag": "CMOy8vPzsIgDEAE=",
  "timeCreated": "2026-09-24T11:12:11.207Z",
  "updated": "2026-09-24T11:12:11.207Z"
}

HTTP/1.1 200 OK
```

Eventarc sends a CloudEvent in binary content mode. The event's attributes are `ce-` headers.
`ce-source` names the bucket. `ce-subject` is `objects/` followed by the object's name. The extension
attribute `ce-bucket` is the bucket's name again.

The JSON body is Cloud Storage's metadata for the object that changed. The object itself is not in the
delivery. The metadata's field names and values are those of Cloud Storage's JSON API object resource.
When the handler returns, the service answers 200 with no body.

Each request carries one change. The handler runs once for it. The handler binds the same body from
both forms.

[Triggers](/guide/triggers) covers `[Blob]` and the other trigger attributes. The Google Cloud
[Overview](/gcp/) covers running the service locally and posting a delivery to it.

## Packages

This command writes the service and a test project:

```bash
dotnet new hardened-function -n Orders --host gcp --trigger blob
```

A blob service references the adapter package `Hardened.Gcp.CloudRun.Storage` beside
`Hardened.Gcp.CloudRun.Runtime`:

```xml
<PackageReference Include="Hardened.Gcp.CloudRun.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Gcp.CloudRun.Storage" Version="0.0.0-HARDENED-VERSION" />
```

A Cloud Functions 2nd gen function references the same adapter package. The Google Cloud
[Overview](/gcp/) covers the other packages, the application class and the entry point.

The adapter package makes `[Blob]` mean Cloud Storage. The build registers the package's
`StorageModule` on the application. The application does not name the module. The module has no
settings.

## Routing by bucket name

The adapter reads a delivery in either form. It takes the bucket's name from the delivery:

| Form | Read as a Cloud Storage change when | Bucket name from |
|---|---|---|
| Eventarc CloudEvent | Its type starts with `google.cloud.storage.object.v1.` | `ce-source` |
| Pub/Sub push | Its attributes include `eventType` and `bucketId`, whatever its subscription | The `bucketId` attribute |

Both forms from the bucket `uploads` reach `[Blob("uploads")]` under the route `BLOB /uploads`. The
name matches exactly, including case. Cloud Storage bucket names contain only lowercase letters,
numbers, dashes, underscores and dots. A `[Blob]` name with an uppercase letter matches no bucket.

The adapter reads a structured-mode event, with the content type `application/cloudevents+json`, the
same way as a binary-mode one. The path a delivery is posted to does not matter. One service can serve
several buckets, with a handler for each.

The service answers 500 with no body to a change from a bucket that no handler names. For the bucket
`payments`, the service logs an `InvalidOperationException` with this message:

```text
No handler is registered for BLOB /payments. An event source is wired to this function that no trigger attribute declared.
```

A service whose handler is named like its method runs that handler for such a change, instead of
answering 500. The change is then acknowledged. [Triggers](/guide/triggers) covers the rule. The
Google Cloud [Overview](/gcp/) covers its effect on Cloud Run.

A service that also references `Hardened.Gcp.CloudRun.PubSub` serves `[Queue]` handlers beside
`[Blob]` handlers. A push with `eventType` and `bucketId` attributes reaches `[Blob]`, even when a
`[Queue]` handler names its subscription. Every other push reaches `[Queue]`. [Queues](/gcp/queue)
covers the push.

Without `Hardened.Gcp.CloudRun.Storage`, the Eventarc form of a change reaches an `[Event]` handler
instead. The Google Cloud [Overview](/gcp/) covers it.

## The request body

For each change, the adapter writes a JSON object with the fields in the table below. That object is
the request body. [Triggers](/guide/triggers) covers how the body binds to the handler's parameter. It
also covers the fields `bucket`, `key` and `size`, which have the same names on every cloud.

A notification is a Pub/Sub push whose attributes are Cloud Storage's. With the payload format
`JSON_API_V1`, the push's `data` is the object's metadata, base64-encoded. With `NONE`, the push has
no `data`.

| Body field | From an Eventarc event | From a notification |
|---|---|---|
| `bucket` | The metadata's `bucket`, else the bucket in `ce-source` | The metadata's `bucket`, else `bucketId` |
| `name`, `key` | The metadata's `name`, else `ce-subject` after `objects/` | The metadata's `name`, else `objectId` |
| `size` | The metadata's `size`, as a number | The metadata's `size`, as a number. Left out with the payload format `NONE` |
| `contentType`, `etag`, `timeCreated`, `updated` | The metadata's fields | The metadata's fields. `null` with the payload format `NONE` |
| `generation` | The metadata's `generation`, as a string | `objectGeneration`, as a string |
| `eventType` | The event's type, in Cloud Storage's notification words | `eventType` |
| `eventTime` | `ce-time` | `eventTime` |

`key` and `name` both hold the object's name as Cloud Storage writes it in the metadata, with no
encoding.

Cloud Storage writes `size` and `generation` as strings. The body carries `size` as a JSON number and
`generation` as a string. A `long` `Size` binds. A `string` `Size` fails to bind. The request then
answers 400. `Generation` binds as a `string` or a `long`.

A field that the delivery does not carry is `null` in the body. A missing `size` is left out instead.
A `long` `Size` then stays 0. A `long?` `Size` is null.

The metadata's other fields, such as `md5Hash`, `crc32c` and `metageneration`, do not reach the
handler.

## Event types

`eventType` names the change in Cloud Storage's notification words, whichever form it came in:

| Eventarc type | `eventType` | Cloud Storage sends it when |
|---|---|---|
| `google.cloud.storage.object.v1.finalized` | `OBJECT_FINALIZE` | An object, or a new generation of an existing object, is created |
| `google.cloud.storage.object.v1.deleted` | `OBJECT_DELETE` | An object is deleted |
| `google.cloud.storage.object.v1.archived` | `OBJECT_ARCHIVE` | A live version of an object becomes a noncurrent version. Only in a bucket with object versioning |
| `google.cloud.storage.object.v1.metadataUpdated` | `OBJECT_METADATA_UPDATE` | The metadata of an existing object changes |

Cloud Storage sends two changes when an object is overwritten: `OBJECT_FINALIZE` for the new
generation, and `OBJECT_DELETE` for the replaced one. In a bucket that keeps versions, the change for
the replaced one is `OBJECT_ARCHIVE`. Each change carries its own object's `generation`. The two
changes of an overwrite have different generations. A notification also carries `overwroteGeneration`
on the new object's finalize, and `overwrittenByGeneration` on the replaced object's delete or
archive.

Cloud Storage does not publish notifications in the order the changes happened. Cloud Storage triggers
through Eventarc are built on the same notifications. Google's
[Pub/Sub notifications for Cloud Storage](https://docs.cloud.google.com/storage/docs/pubsub-notifications)
and
[Create triggers from Cloud Storage events](https://docs.cloud.google.com/run/docs/triggering/storage-triggers)
pages cover the notification's attributes and event types.

## Recording only new objects

This version of the model has two more properties:

```csharp
namespace Orders;

public class Upload
{
    public string Bucket { get; set; } = "";

    public string Key { get; set; } = "";

    public long Size { get; set; }

    public string EventType { get; set; } = "";

    public string Generation { get; set; } = "";
}
```

This handler records only new objects:

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
            "{EventType} {Key}, generation {Generation}",
            upload.EventType,
            upload.Key,
            upload.Generation
        );

        if (upload.EventType is "OBJECT_DELETE" or "OBJECT_ARCHIVE")
        {
            return;
        }

        log.Record(upload);
    }
}
```

For the Eventarc event at the start of this page, the handler logs this line and records the upload:

```text
OBJECT_FINALIZE reports/2026-09.pdf, generation 1758712331205187
```

The subscription `uploads-notifications` pushes this delete notification from a bucket whose
notification configuration sends no payload:

```http
POST /
Content-Type: application/json

{
  "message": {
    "attributes": {
      "bucketId": "uploads",
      "eventTime": "2026-09-24T11:20:43.518Z",
      "eventType": "OBJECT_DELETE",
      "notificationConfig": "projects/_/buckets/uploads/notificationConfigs/1",
      "objectGeneration": "1755000000000001",
      "objectId": "reports/2026-08.pdf",
      "payloadFormat": "NONE"
    },
    "messageId": "16135740127478428",
    "message_id": "16135740127478428",
    "publishTime": "2026-09-24T11:20:43.702Z",
    "publish_time": "2026-09-24T11:20:43.702Z"
  },
  "subscription": "projects/my-project/subscriptions/uploads-notifications"
}

HTTP/1.1 200 OK
```

The adapter writes this body for the change:

```json
{"bucket":"uploads","name":"reports/2026-08.pdf","key":"reports/2026-08.pdf","contentType":null,"generation":"1755000000000001","etag":null,"eventType":"OBJECT_DELETE","eventTime":"2026-09-24T11:20:43.518Z","timeCreated":null,"updated":null}
```

## Headers

The adapter also sets the headers in this table. A handler reads them through an `IExecutionRequest`
parameter, which is in `Hardened.Requests.Abstract.Execution`. Header names match without regard to
case.

| Header | From an Eventarc event | From a notification |
|---|---|---|
| `eventType`, `bucketId`, `objectId`, `objectGeneration`, `eventTime` | Written by the adapter, from the event | Cloud Storage's attributes |
| `notificationConfig`, `payloadFormat` | None | Cloud Storage's attributes |
| `overwroteGeneration`, `overwrittenByGeneration` | None | Cloud Storage's attributes, on an overwrite |
| `ce-specversion`, `ce-id`, `ce-source`, `ce-type`, `ce-subject`, `ce-time` and `ce-bucket` | The event's attributes | None |
| `x-goog-pubsub-subscription-name`, `x-goog-pubsub-message-id`, `x-goog-pubsub-publish-time` | None | The push's metadata. [Queues](/gcp/queue) covers them |

The handler sees none of the headers of the HTTP request itself, such as `Authorization`.
[Queues](/gcp/queue) covers reading them through `CloudRunTriggerRequest.Delivery`.

## Failures and redelivery

The service answers each delivery with the status for its case:

| Case | Status |
|---|---|
| The handler returns | 200, empty |
| The handler throws | 500, `{"type":"ServerError","message":"The server could not complete this request.","details":""}` |
| The body does not bind, or fails a constraint | 400, a `ValidationError` naming the field |
| No handler names the bucket | 500, empty |

Eventarc delivers each event at least once. A handler can receive the same change more than once.
When the service does not acknowledge an event, Eventarc sends it again with an exponential backoff.
The backoff starts at 10 seconds and grows to at most 600 seconds. Eventarc keeps the event for 24
hours, then discards it unless a dead-letter topic is set.
[Events](/gcp/event) covers which statuses Eventarc reads as an acknowledgement.

`--max-retry-attempts=1` on the trigger makes one delivery attempt and no retries. A trigger created
from the Cloud Run page of the Google Cloud console makes a single delivery attempt by default.

A notification is delivered as any Pub/Sub push is. [Queues](/gcp/queue) covers which statuses
Pub/Sub reads as an acknowledgement, its retry policy and dead-letter topics. Cloud Storage delivers
each notification to Pub/Sub at least once. The same change can arrive in several messages with
different message ids.

With `--retry`, the trigger of a function deployed with `gcloud functions deploy` retries a failed
change for up to 7 days.

::: warning
A function deployed with `gcloud functions deploy` and without `--retry` does not get a failed change
again. Its trigger ignores the 500. The change is lost.
:::

Google's [Retry events](https://docs.cloud.google.com/eventarc/docs/retry-events),
[Configure event-driven function retries](https://docs.cloud.google.com/run/docs/tips/function-retries)
and
[REST Resource: projects.locations.functions](https://docs.cloud.google.com/functions/docs/reference/rest/v2/projects.locations.functions)
pages cover Eventarc's retries and a function's retry policy.

## Connecting a bucket

The service or the function deploys the same way for every trigger. The Google Cloud
[Overview](/gcp/) and [Web services](/gcp/web) cover the deployment.

### With an Eventarc trigger

An Eventarc trigger connects a bucket to the service. Its `--event-filters` name the event type and
the bucket. An event must match every filter. Each event type that the handler serves needs a trigger
of its own. The bucket in the filter is the one that `[Blob]` names.

The bucket must be in the trigger's project, and in the trigger's region or multi-region. A bucket can
have up to 10 notification configurations set to trigger for one event type.

The project's Cloud Storage service agent needs the Pub/Sub Publisher role, `roles/pubsub.publisher`.
Its address is `service-` followed by the project's number and
`@gs-project-accounts.iam.gserviceaccount.com`. `gcloud storage service-agent --project=my-project`
prints it.

The trigger calls the service as its service account. That account needs the Cloud Run Invoker role,
`roles/run.invoker`, on the service. Google's quickstart,
[Receive direct events from Cloud Storage (gcloud CLI)](https://docs.cloud.google.com/eventarc/standard/docs/run/create-trigger-storage-gcloud),
also grants the account the Eventarc Event Receiver role, `roles/eventarc.eventReceiver`, on the
project.

In these commands, `orders` is the deployed service, `my-project` the project, `123456789012` its
number and `eventarc-trigger` a service account of your choosing:

```bash
gcloud projects add-iam-policy-binding my-project \
    --member=serviceAccount:service-123456789012@gs-project-accounts.iam.gserviceaccount.com \
    --role=roles/pubsub.publisher

gcloud run services add-iam-policy-binding orders --region=us-central1 \
    --member=serviceAccount:eventarc-trigger@my-project.iam.gserviceaccount.com \
    --role=roles/run.invoker

gcloud eventarc triggers create uploads-finalized \
    --location=us-central1 \
    --destination-run-service=orders \
    --destination-run-region=us-central1 \
    --event-filters="type=google.cloud.storage.object.v1.finalized" \
    --event-filters="bucket=uploads" \
    --service-account=eventarc-trigger@my-project.iam.gserviceaccount.com
```

The commands send these requests to the Google APIs:

| Command | Request |
|---|---|
| `gcloud projects add-iam-policy-binding` | `setIamPolicy` for the project, with `roles/pubsub.publisher` for the service agent |
| `gcloud run services add-iam-policy-binding` | `setIamPolicy` for the service `orders`, with `roles/run.invoker` |
| `gcloud eventarc triggers create` | `POST /v1/projects/my-project/locations/us-central1/triggers?triggerId=uploads-finalized` with the two filters, the destination service `orders` in `us-central1`, the service account and no retry policy |

A new trigger can take up to two minutes to deliver events. `--destination-run-path` sends the events
to a path on the service. Any path works. For a Cloud Functions 2nd gen function,
`--destination-run-service` names the function. [Topics](/gcp/topic) covers it.

### With a Pub/Sub notification

`gcloud storage buckets notifications create` sends a bucket's notifications to a Pub/Sub topic. It
creates the topic when the topic does not exist. It grants the Cloud Storage service agent
`roles/pubsub.publisher` on the topic.

| Option | Effect |
|---|---|
| `--event-types` | Takes `OBJECT_FINALIZE`, `OBJECT_METADATA_UPDATE`, `OBJECT_DELETE` and `OBJECT_ARCHIVE`. Without it, every type is sent |
| `--payload-format=json` | Sends the object's metadata |
| `--payload-format=none` | Sends only the attributes |

A push subscription on the topic delivers the notifications to the service. The subscription's name
does not route them. [Queues](/gcp/queue) covers push subscriptions and the account they call with.

In these commands, `https://orders-abc123-uc.a.run.app/` stands for the service's URL, and
`pubsub-pusher` is a service account of your choosing:

```bash
gcloud storage buckets notifications create gs://uploads \
    --topic=uploads-notifications \
    --event-types=OBJECT_FINALIZE,OBJECT_DELETE \
    --payload-format=json

gcloud pubsub subscriptions create uploads-notifications \
    --topic=uploads-notifications \
    --push-endpoint=https://orders-abc123-uc.a.run.app/ \
    --push-auth-service-account=pubsub-pusher@my-project.iam.gserviceaccount.com
```

### With `gcloud functions deploy`

`--trigger-event-filters` gives the function an Eventarc trigger when it is deployed. `--retry` sets
that trigger to retry a failed change. [Web services](/gcp/web) covers the rest of the command: the
entry point, `GOOGLE_BUILDABLE` and `.gcloudignore`.

This command runs from the solution directory of the function:

```bash
gcloud functions deploy orders --gen2 --runtime dotnet8 --region us-central1 \
    --source . --entry-point Orders.ApplicationCloudFunction \
    --trigger-event-filters="type=google.cloud.storage.object.v1.finalized" \
    --trigger-event-filters="bucket=uploads" \
    --retry --set-build-env-vars GOOGLE_BUILDABLE=src/Orders
```

It sends `POST /v2/projects/my-project/locations/us-central1/functions?functionId=orders` with the
event type `google.cloud.storage.object.v1.finalized`, the filter `bucket=uploads`, the retry policy
`RETRY_POLICY_RETRY` and the entry point `Orders.ApplicationCloudFunction`.

## Testing

The `hardened-function` template writes this test for `--host gcp --trigger blob`. It sends a change
through `Application.Blobs`:

```csharp
using Hardened.Shared.Testing.Attributes;
using Xunit;

namespace Orders.Tests;

public class OrderHandlerTests
{
    [HardenedTest]
    public async Task ANotificationReachesTheHandler(Application.Blobs blobs, OrderLog log)
    {
        await blobs.Uploads(new Upload { Key = "report.pdf", Size = 1024 });

        Assert.Equal("report.pdf", Assert.Single(log.Uploads).Key);
    }
}
```

The template's test project declares `[assembly: CloudRunTesting]` beside `[assembly: WebTesting]`.
Under `[CloudRunTesting]`, each message of a call is one Eventarc CloudEvent of the type
`google.cloud.storage.object.v1.finalized`, posted to `/` on the test's web host. The event takes these
values:

| Part of the event | Value |
|---|---|
| The bucket | The name in `[Blob]`, whatever the message's `Bucket` says |
| The object's name | The message's `name`, else the message's `key`, else `object-0`, `object-1` in order |
| `size` | The message's `size`, or 0 |
| `contentType` | The message's `contentType`, or `application/octet-stream` |
| `generation` | `1` |
| `etag` | `CAE=` |
| `eventTime`, `timeCreated`, `updated` | `2026-01-01T00:00:00.000Z` |
| `eventType` | `OBJECT_FINALIZE`, whatever the message's `eventType` |
| `ce-id` | The bucket's name and the message's position, such as `uploads-0` |

A message whose `name` is an empty string arrives with an empty object name. `key` then binds as an
empty string. A test that sets only `Key` on a type with both a `Name` and a `Key` property sends such
a message.

To test another event type, a test posts the event through `ITestWebApp`, which
[Sending requests](/guide/testing-web) covers. The request goes through the same adapter as a
delivery. This test posts a delete. It needs `[assembly: WebTesting]`:

```csharp
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Xunit;

namespace Orders.Tests;

public class DeleteTests
{
    private const string Metadata = """
        {"bucket":"uploads","name":"reports/2026-08.pdf","generation":"1755000000000001","size":"1024"}
        """;

    [HardenedTest]
    public async Task ADeleteIsNotRecorded(ITestWebApp app, OrderLog log)
    {
        var response = await app.Post(
            Metadata,
            "/",
            request =>
            {
                request.Headers["ce-specversion"] = "1.0";
                request.Headers["ce-id"] = "delete-1";
                request.Headers["ce-source"] = "//storage.googleapis.com/projects/_/buckets/uploads";
                request.Headers["ce-type"] = "google.cloud.storage.object.v1.deleted";
                request.Headers["ce-subject"] = "objects/reports/2026-08.pdf";
            }
        );

        response.Assert.Ok();
        Assert.Empty(log.Uploads);
    }
}
```

`Post` sends a string body as written. `response.Assert.Ok()` fails the test unless the event reached
the handler. An event that the adapter did not read answers 404. An event for a bucket that no handler
names answers 500.

Under `[FunctionTesting]` alone, the handler binds the message as the test wrote it. The request has
no headers. `Bucket` is empty unless the test sets it. Both tests on this page pass with the handler
that records only new objects, under `[CloudRunTesting]` and under `[FunctionTesting]` alone.

[Testing functions](/guide/testing-functions) covers the façades, and what a failed message does to a
call. The Google Cloud [Testing](/gcp/testing) page covers `[CloudRunTesting]`.

## Next

- [Triggers](/guide/triggers): the trigger attributes, payload binding and the blob fields every cloud
  shares
- [Overview](/gcp/): the packages, the entry point, running locally and deploying
- [Queues](/gcp/queue): push subscriptions, which deliver a bucket's notifications
- [Testing](/gcp/testing): what `[CloudRunTesting]` builds
- [Testing functions](/guide/testing-functions): testing a trigger handler
