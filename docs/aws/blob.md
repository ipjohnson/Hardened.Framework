# Blobs

`[Blob("uploads")]` on a method makes it the handler for Amazon S3 notifications about objects in
the bucket named `uploads`. S3 invokes the function with an event that describes the object that
changed.

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Blob("uploads")]
    public void OnUpload(Upload upload) => log.Record(upload);
}
```

In the template, `OrderLog` is a `[SingletonService]` that keeps the uploads it is given. `Upload`
has a string `Bucket`, a string `Key` and a long `Size`:

```csharp
namespace Orders;

public class Upload
{
    public string Bucket { get; set; } = "";

    public string Key { get; set; } = "";

    public long Size { get; set; }
}
```

The object itself is not in the event. The handler runs once for each record in the event, in the
event's order. S3 puts one record in each event it sends. [Triggers](/guide/triggers) covers
`[Blob]` and the other trigger attributes.

## Packages

This command writes the function above and a test project:

```bash
dotnet new hardened-function -n Orders --trigger blob
```

`--host aws` is the template's default. A blob function references `Hardened.Aws.Lambda.S3` beside
`Hardened.Aws.Lambda.Runtime`:

```xml
<PackageReference Include="Hardened.Aws.Lambda.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Aws.Lambda.S3" Version="0.0.0-HARDENED-VERSION" />
```

The other packages, `Program.cs` and the project settings are the same for every Lambda function.
The AWS [Overview](/aws/) covers them.

The reference to `Hardened.Aws.Lambda.S3` makes `[Blob]` mean S3. The build registers the package's
`S3Module` on the application, so the application does not need to declare it. The module has no
settings. The template's application class names no module:

```csharp
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
public partial class Application;
```

## Buckets and routes

The adapter in `Hardened.Aws.Lambda.S3` takes the bucket's name from `s3.bucket.name` in the event's
first record. A record from the bucket `uploads` reaches `[Blob("uploads")]` under the route
`BLOB /uploads`. The name matches exactly, including case. S3 bucket names contain only lowercase
letters, numbers, dots and hyphens. A `[Blob]` name with an uppercase letter matches no bucket.

An event from a bucket that no handler names fails the invocation with `InvalidOperationException`:

```text
No handler is registered for BLOB /payments. An event source is wired to this function that no trigger attribute declared.
```

One function can serve several buckets, with a handler and a notification configuration for each.

S3 can also send its events to Amazon EventBridge. An `[Event("aws.s3", "Object Created")]` handler
receives those events, under the route `EVENT /aws.s3/Object Created`. That handler binds the
EventBridge event's `detail`. In `detail`, `bucket` and `object` are nested objects, so the
template's `Upload` does not bind from it. [Events](/aws/event) covers `[Event]`. AWS's
documentation covers
[the events S3 sends to EventBridge](https://docs.aws.amazon.com/AmazonS3/latest/userguide/ev-events.html).

## Body and headers

For each record, the adapter writes a JSON object with the fields `bucket`, `key`, `size`, `eTag`,
`sequencer`, `eventName` and `eventTime`. That object is the request body. Each field except `size`
also becomes a header:

| From the S3 record | Body field | Header |
|---|---|---|
| `s3.bucket.name` | `bucket` | `x-amz-s3-bucket`, and the route |
| `s3.object.key` | `key`, URL-decoded | `x-amz-s3-key`, URL-decoded |
| `s3.object.size` | `size`. Left out when the record has none | None |
| `s3.object.eTag` | `eTag`. `null` when the record has none | `x-amz-s3-etag`. Left out when the record has none |
| `s3.object.sequencer` | `sequencer` | `x-amz-s3-sequencer` |
| `eventName`, such as `ObjectCreated:Put` or `ObjectRemoved:Delete` | `eventName` | `x-amz-s3-event-name` |
| `eventTime` | `eventTime` | `x-amz-s3-event-time` |
| `awsRegion`, `userIdentity`, `requestParameters`, `responseElements`, `s3.configurationId`, `s3.bucket.arn`, `s3.object.versionId` and the rest | Nothing | Nothing |

[Triggers](/guide/triggers) covers how the body binds to the handler's parameter, and which blob
fields have the same names on every cloud.

S3 URL-encodes the object's key in the event. The adapter decodes the key:

| Key in the event | Key the handler receives |
|---|---|
| `reports/my+report.pdf` | `reports/my report.pdf` |
| `c%2B%2Bnotes.txt` | `c++notes.txt` |
| `caf%C3%A9.txt` | `café.txt` |

When a record has no `size`, a `long` property stays 0. A `long?` property is then null. AWS's
sample delete event has neither `size` nor `eTag`.

The adapter leaves out a header whose value is missing or empty. The request looks up a header name
without regard to case. [Triggers](/guide/triggers) covers reading headers through an
`IExecutionRequest` parameter.

## Event name and sequencer

A handler that needs the event name or the sequencer declares them as properties of its payload
type:

```csharp
namespace Orders;

public class Upload
{
    public string Bucket { get; set; } = "";

    public string Key { get; set; } = "";

    public long Size { get; set; }

    public string EventName { get; set; } = "";

    public string Sequencer { get; set; } = "";
}
```

This handler logs each notification and skips deletes:

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
            "{EventName} {Key}, sequencer {Sequencer}",
            upload.EventName,
            upload.Key,
            upload.Sequencer
        );

        if (upload.EventName.StartsWith("ObjectRemoved:", StringComparison.Ordinal))
        {
            return;
        }

        log.Record(upload);
    }
}
```

This event has the shape of the example in AWS's documentation:

```json
{
  "Records": [
    {
      "eventVersion": "2.1",
      "eventSource": "aws:s3",
      "awsRegion": "us-east-1",
      "eventTime": "2026-09-23T12:45:07.192Z",
      "eventName": "ObjectCreated:Put",
      "userIdentity": {
        "principalId": "AWS:AIDAINPONIXQXHT3IKHL2"
      },
      "requestParameters": {
        "sourceIPAddress": "205.255.255.255"
      },
      "responseElements": {
        "x-amz-request-id": "D82B88E5F771F645",
        "x-amz-id-2": "vlR7PnpV2Ce81l0PRw6jlUpck7Jo5ZsQjryTjKlc5aLWGVHPZLj5NeC6qMa0emYBDXOo6QBU0Wo="
      },
      "s3": {
        "s3SchemaVersion": "1.0",
        "configurationId": "uploads-created",
        "bucket": {
          "name": "uploads",
          "ownerIdentity": {
            "principalId": "A3I5XTEXAMAI3E"
          },
          "arn": "arn:aws:s3:::uploads"
        },
        "object": {
          "key": "reports/my+report.pdf",
          "size": 1305107,
          "eTag": "b21b84d653bb07b05b1e6b33684dc11b",
          "sequencer": "0C0F6F405D6ED209E1"
        }
      }
    }
  ]
}
```

For this event, the handler logs
`ObjectCreated:Put reports/my report.pdf, sequencer 0C0F6F405D6ED209E1`. A delete event for
`reports/old+report.pdf`, with only `key` and `sequencer` in its `object`, makes the handler log
`ObjectRemoved:Delete reports/old report.pdf, sequencer 0C0F6F405D6ED209F2`. The handler then
returns without recording it. The AWS [Overview](/aws/) covers running the function locally and
sending it an event.

S3 does not deliver notifications in the order the changes happened. Of two notifications about the
same key, the one with the greater `sequencer` is the later change. To compare two sequencers of
different lengths, pad the shorter one with zeros on the left, then compare them as strings. A
sequencer orders changes to one key only. AWS's documentation covers
[the S3 event's fields and the sequencer](https://docs.aws.amazon.com/AmazonS3/latest/userguide/notification-content-structure.html).

## Failures and retries

A record fails when its handler throws. A body that fails binding or validation fails its record the
same way. [Triggers](/guide/triggers) covers both.

| Case | What happens |
|---|---|
| A handler throws | The invocation fails with that exception. The records after that record in the event do not run. Lambda runs the invocation two more times |
| No handler names the bucket | The invocation fails with `InvalidOperationException`. Lambda runs it two more times |
| Every record succeeds | The function answers with an empty body |

S3 reads no answer from the function, so the function does not report which records failed. The
module has no `ReportBatchItemFailures` setting.

S3 invokes the function asynchronously. When an asynchronous invocation fails, Lambda runs it two
more times by default. The second attempt comes one minute after the first. The third comes two
minutes after the second. After the last attempt, Lambda discards the event, unless the function has
a dead-letter queue or an on-failure destination.

::: warning
A function with no dead-letter queue and no on-failure destination loses a notification whose
handler fails three times.
:::

S3 delivers each notification at least once. On rare occasions it sends a duplicate. Lambda can also
run the function more than once for one event, even when no invocation failed.

AWS's documentation covers
[S3 events in Lambda](https://docs.aws.amazon.com/lambda/latest/dg/with-s3.html),
[S3's event types and destinations](https://docs.aws.amazon.com/AmazonS3/latest/userguide/notification-how-to-event-types-and-destinations.html),
[asynchronous invocation and its retries](https://docs.aws.amazon.com/lambda/latest/dg/invocation-async-error-handling.html),
and
[the function's dead-letter queue and on-failure destination](https://docs.aws.amazon.com/lambda/latest/dg/invocation-async-retain-records.html).

## Connecting a bucket

A permission and a notification configuration connect a bucket to the function. The permission lets
S3 invoke the function. The notification configuration on the bucket names the function. The
bucket's name is the name in `[Blob]`. S3 checks the function's permission when it saves the
notification configuration, so the permission comes first.

`notification.json` holds the notification configuration:

```json
{
  "LambdaFunctionConfigurations": [
    {
      "Id": "uploads-to-orders",
      "LambdaFunctionArn": "arn:aws:lambda:us-east-1:123456789012:function:Orders",
      "Events": ["s3:ObjectCreated:*", "s3:ObjectRemoved:*"]
    }
  ]
}
```

`Events` names the changes that invoke the function. `s3:ObjectCreated:*` covers every API that
creates an object. `s3:ObjectRemoved:*` covers deletes, but not the deletes a lifecycle
configuration makes.

These commands add the permission and then save the notification configuration. `Orders` is the
deployed function's name. `uploads-bucket` is a statement id that you choose.

```bash
aws lambda add-permission --function-name Orders \
    --principal s3.amazonaws.com --statement-id uploads-bucket \
    --action lambda:InvokeFunction \
    --source-arn arn:aws:s3:::uploads \
    --source-account 123456789012

aws s3api put-bucket-notification-configuration --bucket uploads \
    --notification-configuration file://notification.json
```

`--source-account` makes the permission hold only for a bucket that belongs to that account.
`put-bucket-notification-configuration` replaces the bucket's whole notification configuration.

The function has to be in the same Region as the bucket. A function that writes objects to the
bucket that triggers it invokes itself again. AWS's documentation advises two buckets, or a
notification limited to a prefix that only incoming objects use.

Lambda's retries, the dead-letter queue and the on-failure destination are settings of the
function's asynchronous invocation. The function itself deploys the same way for every trigger. The
AWS [Overview](/aws/) covers it. AWS's documentation covers
[the permission](https://docs.aws.amazon.com/lambda/latest/dg/with-s3-tutorial.html) and
[the notification configuration and its filters](https://docs.aws.amazon.com/cli/latest/reference/s3api/put-bucket-notification-configuration.html).

## Testing

The template writes this test in `tests/Orders.Tests/OrderHandlerTests.cs`. It sends a notification
through the `Application.Blobs` façade.

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

The template's test project declares `[assembly: LambdaTesting]`. `[LambdaTesting]` delivers each
façade call as one S3 event, with a record per message. It builds each record this way:

| Record field | Value |
|---|---|
| Bucket | The name in `[Blob]`, whatever the message's `Bucket` says |
| Key | The message's `key`, URL-encoded with a space as `+`, so the adapter's decoding runs. A message type with no `key` property gets `object-0`, `object-1` in order |
| Size | The message's `size`, or 0 when it has none |
| Event name | `ObjectCreated:Put` |
| Event time | `2026-01-01T00:00:00.000Z` |
| Entity tag | The record's position in 32 digits |
| Sequencer | `44215845000000000174504390` followed by the record's position in two digits |

A message's event name does not reach the handler under `[LambdaTesting]`.

Under `[FunctionTesting]` alone, the handler binds the message as the test wrote it. The request has
no headers. `Bucket` is empty unless the test sets it.

The template's test passes with the handler that skips deletes, under `[LambdaTesting]` and under
`[FunctionTesting]` alone. Under both, a failed record fails the façade call with the handler's
exception. The messages after it in the same call do not run.
[Testing functions](/guide/testing-functions) covers the façades and the deliveries.

## Next

| Page | Covers |
|---|---|
| [Triggers](/guide/triggers) | The trigger attributes, payload binding and the blob fields every cloud shares |
| [Overview](/aws/) | The packages, `Program.cs`, running locally and deploying a function |
| [Events](/aws/event) | EventBridge events, including the S3 events a bucket sends to EventBridge |
| [Testing functions](/guide/testing-functions) | Testing a trigger handler |
| [Testing](/aws/testing) | What `[LambdaTesting]` builds |
