# Streams

`[Stream("orders")]` on a method makes it the handler for records written to the Kinesis data
stream named `orders`. Lambda invokes the function with a batch of records from one shard. The
handler runs once for each record, in the batch's order.

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Stream("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

The handler's parameter binds the record's data, which Kinesis carries base64-encoded. In the
`hardened-function` template, `Order` has a string `Id` and an int `Quantity`. `OrderLog` is a
`[SingletonService]` that keeps the orders it is given. [Triggers](/guide/triggers) covers
`[Stream]` and the other trigger attributes.

## Creating the function

The `hardened-function` template writes the function above and a test project:

```bash
dotnet new hardened-function -n Orders --trigger stream
```

`--host aws` is the template's default.

A stream function references `Hardened.Aws.Lambda.Kinesis` beside `Hardened.Aws.Lambda.Runtime`:

```xml
<PackageReference Include="Hardened.Aws.Lambda.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Aws.Lambda.Kinesis" Version="0.0.0-HARDENED-VERSION" />
```

Referencing `Hardened.Aws.Lambda.Kinesis` makes `[Stream]` mean Kinesis. The build registers the
package's `KinesisModule` on the application. The application class names no module:

```csharp
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
public partial class Application;
```

The other packages, `Program.cs` and the project settings are the same for every Lambda function.
The AWS [Overview](/aws/) covers them.

## Routing

`KinesisAdapter` takes the stream's name from the last segment of the batch's `eventSourceARN`. A
batch from `arn:aws:kinesis:us-east-1:123456789012:stream/orders` reaches `[Stream("orders")]`,
under the route `STREAM /orders`. The adapter reads the route once for the batch, from the first
record. Every record in the batch goes to the same handler. The name in `[Stream]` has to match the
stream's name exactly, including case.

A batch from a stream that no handler names fails the invocation with `InvalidOperationException`.
For a stream named `payments`, the message is "No handler is registered for STREAM /payments. An
event source is wired to this function that no trigger attribute declared." The invocation fails
with `ReportBatchItemFailures` on or off.

One function can serve several streams, with a handler and an event source mapping for each.

## Data and headers

The request body is the record's `data`, base64-decoded: the bytes the producer wrote. Kinesis does
not say what the data is. The handler's parameter type decides how it binds. A JSON object binds a
class such as `Order`. A `byte[]` parameter receives the data as the producer wrote it, JSON or not.
[Triggers](/guide/triggers) covers how the body binds to the handler's parameter.

The record's partition key, sequence number, event id and arrival time, and the stream's ARN, become
five headers:

| From the Kinesis record | Reaches the handler as |
|---|---|
| `kinesis.data` | The request body, base64-decoded |
| `kinesis.partitionKey` | `x-amz-kinesis-partition-key` |
| `kinesis.sequenceNumber` | `x-amz-kinesis-sequence-number` |
| `eventID` | `x-amz-kinesis-event-id` |
| `kinesis.approximateArrivalTimestamp` | `x-amz-kinesis-arrival-time`, the number's text, in epoch seconds |
| `eventSourceARN` | `x-amz-kinesis-stream-arn`, and the route |
| `kinesis.kinesisSchemaVersion`, `eventName`, `eventVersion`, `invokeIdentityArn`, `awsRegion` | Nothing |

The arrival time is the text of the number the record carries, such as `1545084650.987`. The event
id is the shard's id and the sequence number, joined by a colon, such as
`shardId-000000000006:49590338271490256608559692538361571095921575989136588898`. The request
matches header names without regard to case.

Lambda processes each shard's records in order. When it reads a shard with more than one invocation
at a time, it keeps the records with one partition key in order.

A handler reads the headers through an `IExecutionRequest` parameter, from namespace
`Hardened.Requests.Abstract.Execution`. This handler reads two of them:

```csharp
using Hardened.Functions.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Stream("orders")]
    public void OnOrder(Order order, IExecutionRequest request)
    {
        request.Headers.TryGetValue("x-amz-kinesis-partition-key", out var partitionKey);
        request.Headers.TryGetValue("x-amz-kinesis-sequence-number", out var sequenceNumber);

        logger.LogInformation(
            "Order {Id} from partition {PartitionKey} at {SequenceNumber}",
            order.Id,
            partitionKey.ToString(),
            sequenceNumber.ToString()
        );

        log.Record(order);
    }
}
```

The example reads the headers with `TryGetValue`, because under `[FunctionTesting]` alone the
request has no headers.

This event, in the shape of the AWS documentation's example, carries two records from `orders`:

```json
{
  "Records": [
    {
      "kinesis": {
        "kinesisSchemaVersion": "1.0",
        "partitionKey": "A-1",
        "sequenceNumber": "49590338271490256608559692538361571095921575989136588898",
        "data": "eyJpZCI6IkEtMSIsInF1YW50aXR5IjoyfQ==",
        "approximateArrivalTimestamp": 1545084650.987
      },
      "eventSource": "aws:kinesis",
      "eventVersion": "1.0",
      "eventID": "shardId-000000000006:49590338271490256608559692538361571095921575989136588898",
      "eventName": "aws:kinesis:record",
      "invokeIdentityArn": "arn:aws:iam::123456789012:role/lambda-role",
      "awsRegion": "us-east-1",
      "eventSourceARN": "arn:aws:kinesis:us-east-1:123456789012:stream/orders"
    },
    {
      "kinesis": {
        "kinesisSchemaVersion": "1.0",
        "partitionKey": "A-2",
        "sequenceNumber": "49590338271490256608559692540925702759324208523137515618",
        "data": "eyJpZCI6IkEtMiIsInF1YW50aXR5IjoxfQ==",
        "approximateArrivalTimestamp": 1545084711.166
      },
      "eventSource": "aws:kinesis",
      "eventVersion": "1.0",
      "eventID": "shardId-000000000006:49590338271490256608559692540925702759324208523137515618",
      "eventName": "aws:kinesis:record",
      "invokeIdentityArn": "arn:aws:iam::123456789012:role/lambda-role",
      "awsRegion": "us-east-1",
      "eventSourceARN": "arn:aws:kinesis:us-east-1:123456789012:stream/orders"
    }
  ]
}
```

The two `data` values are the base64 of `{"id":"A-1","quantity":2}` and
`{"id":"A-2","quantity":1}`. The AWS [Overview](/aws/) covers running the function locally and
sending it an event.

## When a record fails

A record fails when its handler throws or its data does not bind. `ReportBatchItemFailures` decides
what a failed record does:

| Case | `ReportBatchItemFailures` off | `ReportBatchItemFailures` on |
|---|---|---|
| A handler throws, or the data does not bind | The invocation fails, the later records do not run, and Lambda retries the whole batch | The invocation succeeds, the later records do not run, the report names the failed record, and Lambda retries from it |
| No handler names the stream | The invocation fails | The invocation fails |

Without reporting, the first failed record fails the whole invocation with its exception. Lambda
then retries the whole batch, including the records that ran. While it retries, Lambda sends the
function no later records from that shard. By default Lambda retries until the records expire. A
Kinesis data stream keeps records for 24 hours by default. Its retention can be raised to 365 days.

An invocation that does not fail answers with a `batchItemFailures` report. Without reporting, the
report is always empty. With reporting on in the application and in the event source mapping, the
report names the failed record by its sequence number. This is the answer to a batch of three whose
second record failed:

```json
{"batchItemFailures":[{"itemIdentifier":"49590338271490256608559692540925702759324208523137515618"}]}
```

Lambda then retries the batch starting from the named record. A batch whose first record fails is
still an invocation that succeeds, with the first record in the report. A Kinesis batch runs in the
`Checkpoint` failure mode. [Triggers](/guide/triggers) covers `BatchFailureMode`.

The records of one invocation run one after another. They share the invocation's cancellation
token. The function's timeout has to cover the whole batch. The AWS [Overview](/aws/) covers when that token is
cancelled.

## Retries and discarded batches

The event source mapping's settings in this table limit the retries and split a failed batch:

| Setting | CLI option | Default | What Lambda does |
|---|---|---|---|
| `MaximumRetryAttempts` | `--maximum-retry-attempts` | -1: until the records expire | Retries a failed batch up to this many times, then discards it |
| `MaximumRecordAgeInSeconds` | `--maximum-record-age-in-seconds` | -1: until the records expire | Discards records older than this many seconds. The most is 604,800 |
| `BisectBatchOnFunctionError` | `--bisect-batch-on-function-error` | Off | Splits a failed batch in two before retrying. A split does not count as a retry |
| `DestinationConfig` | `--destination-config` | None | Sends the details of each discarded batch to the destination |

When an invocation fails and `BisectBatchOnFunctionError` is on, Lambda splits the batch in two and
retries each half, whatever `ReportBatchItemFailures` says. With both `BisectBatchOnFunctionError`
and `ReportBatchItemFailures` on, Lambda splits a reported batch at the named sequence number. It
retries only the records from there.

When retries run out, Lambda discards the batch's records and goes on with the shard. A destination
on an SQS queue or an SNS topic receives the discarded batch's shard, first and last sequence numbers
and stream ARN. It does not receive the records. The records have to be read from the stream before
they expire. A destination on an S3 bucket also receives the batch's records.

This mapping gives up after ten retries, splits a failed batch, and sends the details of a discarded
batch to an SQS queue:

```bash
aws lambda create-event-source-mapping \
    --function-name Orders \
    --event-source-arn arn:aws:kinesis:us-east-1:123456789012:stream/orders \
    --starting-position TRIM_HORIZON \
    --function-response-types ReportBatchItemFailures \
    --maximum-retry-attempts 10 \
    --bisect-batch-on-function-error \
    --destination-config '{"OnFailure": {"Destination": "arn:aws:sqs:us-east-1:123456789012:orders-failed"}}'
```

Lambda can deliver a record more than once, even when no invocation failed.

AWS's documentation covers
[the stream's event source mapping](https://docs.aws.amazon.com/lambda/latest/dg/with-kinesis.html),
[partial batch responses](https://docs.aws.amazon.com/lambda/latest/dg/services-kinesis-batchfailurereporting.html)
and [discarded records](https://docs.aws.amazon.com/lambda/latest/dg/kinesis-on-failure-destination.html).

## Reporting failed records

`KinesisModule` has one setting:

| Property | Default | When `true` |
|---|---|---|
| `ReportBatchItemFailures` | `false` | A failed record does not fail the invocation. The later records do not run, and the function answers with the failed record's sequence number |

To turn it on, put `[KinesisModule(ReportBatchItemFailures = true)]` on the application class. The
module is in namespace `Hardened.Aws.Lambda.Kinesis`.

```csharp
using Hardened.Aws.Lambda.Kinesis;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[KinesisModule(ReportBatchItemFailures = true)]
public partial class Application;
```

With the attribute on the application class, the build registers only the module that the
application declares. The setting has to match `ReportBatchItemFailures` on the stream's event
source mapping. With it on in the mapping and off in the application, the invocation fails at the
first failed record. Lambda then retries the whole batch.

::: warning
With `ReportBatchItemFailures` on in the application and off in the event source mapping, Lambda
ignores the report. It treats an invocation that did not fail as a complete success. It moves past
the whole batch. The failed record and every record after it are never handled. Turn the setting on
in both places or in neither.
:::

## Connecting the stream

An event source mapping connects the stream to the function. Its event source is the stream's ARN.
The stream's name is the name in `[Stream]`.

```bash
aws lambda create-event-source-mapping \
    --function-name Orders \
    --event-source-arn arn:aws:kinesis:us-east-1:123456789012:stream/orders \
    --starting-position TRIM_HORIZON \
    --function-response-types ReportBatchItemFailures
```

A mapping on a stream needs a starting position:

| Starting position | Reads |
|---|---|
| `TRIM_HORIZON` | Every record the stream holds |
| `LATEST` | Only new records |
| `AT_TIMESTAMP` | From a time |

A new mapping can take several minutes to start reading. With `LATEST`, it can miss the records
written in that time. AWS's advice is `TRIM_HORIZON` or `AT_TIMESTAMP`.

`--function-response-types ReportBatchItemFailures` turns reporting on in the mapping. It matches
`ReportBatchItemFailures = true` in the application. Leave the option out when the application
leaves the setting off.

The mapping's batch size sets how many records one invocation carries. The default is 100. The most
is 10,000.

The function's execution role needs the permissions in the AWS managed policy
`AWSLambdaKinesisExecutionRole` to read the stream.

A mapping with a tumbling window fails every invocation. Lambda treats an answer without a `state`
property as a failed invocation. The function answers only `batchItemFailures`.

Deploying the function itself is the same for every trigger. The AWS [Overview](/aws/) covers it.

## Testing the handler

`dotnet new hardened-function --trigger stream` writes this test. It sends a record through the
`Application.Streams` façade:

```csharp
using DependencyModules.xUnit.Attributes;
using Xunit;

namespace Orders.Tests;

public class OrderHandlerTests
{
    [ModuleTest]
    public async Task ARecordReachesTheHandler(Application.Streams streams, OrderLog log)
    {
        await streams.Orders(new Order { Id = "A-1", Quantity = 2 });

        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

The template's test project declares `[assembly: LambdaTesting]`. It delivers each façade call as
one Kinesis event, with a record for each message. Each record carries these values:

| Record field | Value |
|---|---|
| Data | The message as camelCase JSON, base64-encoded |
| Partition key | The stream's name and the message's position, such as `orders-0` and `orders-1` |
| Sequence number | `4421584500000000017450439000`, `4421584500000000017450439001` and on |
| Event id | `shardId-000000000000:` and the sequence number |
| Arrival time | `1767225600.0` |
| Stream ARN | `arn:aws:kinesis:us-east-1:123456789012:stream/orders` |

Under `[FunctionTesting]` alone the request has no headers. The handler that reads headers passes
its tests under both.

What a failed message does to the façade call depends on the delivery and on
`ReportBatchItemFailures`. [Testing functions](/guide/testing-functions) covers it. The façade call
does not return the `batchItemFailures` report. The AWS [Testing](/aws/testing) page covers reading
it.

## Next

- [Triggers](/guide/triggers): the trigger attributes, payload binding and `BatchFailureMode`
- [Changes](/aws/change): DynamoDB Streams, the other ordered source
- [Overview](/aws/): the packages, `Program.cs`, running locally and deploying a function
- [Testing](/aws/testing): what `[LambdaTesting]` builds, and reading the batch report
- [Testing functions](/guide/testing-functions): testing a trigger handler
