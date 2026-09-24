# Changes

`[Change("orders")]` on a method makes it the handler for changes to the DynamoDB table named `orders`. Lambda reads the table's stream and invokes the function with a batch of stream records.

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Change("orders")]
    public void OnOrderChanged(Order order) => log.Record(order);
}
```

The application class names no module:

```csharp
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
public partial class Application;
```

The handler runs once for each record, in the batch's order. In the `hardened-function` template, `Order` has a string `Id` and an int `Quantity`. `OrderLog` is a `[SingletonService]` that keeps the orders it is given.

::: v-pre
A stream record carries the item in DynamoDB's form, with a type tag on each value, such as `{"id":{"S":"A-1"},"quantity":{"N":"2"}}`. The handler's parameter binds the item without the tags, so `Order` gets an `Id` of `A-1` and a `Quantity` of 2.
:::

The project's reference to `Hardened.Aws.Lambda.DynamoDb` makes `[Change]` mean DynamoDB Streams. The build registers the package's `DynamoDbStreamsModule` on the application. [Triggers](/guide/triggers) covers `[Change]` and compares the change feeds of the three clouds.

## Packages

A change function references `Hardened.Aws.Lambda.DynamoDb` beside `Hardened.Aws.Lambda.Runtime`:

```xml
<PackageReference Include="Hardened.Aws.Lambda.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Aws.Lambda.DynamoDb" Version="0.0.0-HARDENED-VERSION" />
```

`Hardened.Aws.Lambda.DynamoDb` brings `Amazon.Lambda.DynamoDBEvents` 3.1.1, the package that defines `DynamoDBEvent`. A handler that names `DynamoDBEvent` types, as in [The item before and after the change](#the-item-before-and-after-the-change), needs no package reference of its own. The other packages, `Program.cs` and the project settings are the same for every Lambda function. The AWS [Overview](/aws/) covers them.

This command writes the function and a test project:

```bash
dotnet new hardened-function -n Orders --trigger change
```

`--host aws` is the template's default.

`Hardened.Aws.Lambda.DynamoDb` serves the table's stream and does not read or write the table. `Hardened.Aws.DynamoDbClient` supplies DynamoDB clients, and [DynamoDB client](/aws/dynamodb) covers it.

## The table name

The adapter in `Hardened.Aws.Lambda.DynamoDb` takes the table's name from the batch's `eventSourceARN`, which is the stream's ARN. The name is the part between `table/` and `/stream`. A batch from `arn:aws:dynamodb:us-east-1:123456789012:table/orders/stream/2026-09-23T10:00:00.000` reaches `[Change("orders")]`, under the route `CHANGE /orders`.

The adapter reads the route once for each batch, from the first record. Every record in the batch goes to the same handler. The name matches exactly, including case.

A batch from a table that no handler names fails the invocation with `InvalidOperationException`. For a table named `payments`, the message is "No handler is registered for CHANGE /payments. An event source is wired to this function that no trigger attribute declared." This happens with and without `ReportBatchItemFailures`.

One function can serve several tables, with a handler and an event source mapping for each.

## The request body

The request body is the record's new image, as JSON without DynamoDB's type tags. A record with no new image gives its old image instead, so a `REMOVE` binds the item as it was before it was deleted. A record with neither image gives the body `{}`, and the parameter binds with its default values, with no error.

The stream's view type decides which images a record carries. The view type is chosen when the stream is turned on, as [Deploying](#deploying) shows. This table gives the body for each view type and event name:

| View type | `INSERT` | `MODIFY` | `REMOVE` |
|---|---|---|---|
| `NEW_AND_OLD_IMAGES` | The item after the change | The item after the change | The item before the change |
| `NEW_IMAGE` | The item after the change | The item after the change | `{}` |
| `OLD_IMAGE` | `{}` | The item before the change | The item before the change |
| `KEYS_ONLY` | `{}` | `{}` | `{}` |

This table gives what each of DynamoDB's type tags becomes in the request body:

| Type tag | In the request body |
|---|---|
| `S` | A JSON string |
| `N` | A JSON number, with the digits DynamoDB stored |
| `BOOL` | `true` or `false` |
| `NULL` | `null` |
| `M` | A JSON object, each value converted the same way |
| `L` | A JSON array, each member converted the same way |
| `SS`, `NS` | A JSON array of strings, or of numbers |
| `B`, `BS` | Nothing. The whole invocation fails before any handler runs |

A number attribute binds a numeric property. Bound to a `string` property, it fails the record with `JsonException`: "The JSON value could not be converted to System.String."

A record with a binary attribute, `B` or `BS`, anywhere in it fails the whole invocation before any handler runs. The exception is a `JsonException`, with a message such as "The JSON value could not be converted to System.IO.MemoryStream. Path: $.Records[0].dynamodb.NewImage.blob.B". This happens with and without `ReportBatchItemFailures`. A table whose key is binary therefore fails every invocation.

[Triggers](/guide/triggers) covers how the body binds to the handler's parameter.

## Headers and the stream record

The record's event name, sequence number, stream ARN and view type become four headers. This table maps each field of the stream record to the way it reaches the handler:

| From the stream record | Reaches the handler as |
|---|---|
| `dynamodb.NewImage` | The request body, without the type tags. `[NewImage]`, with them |
| `dynamodb.OldImage` | The request body when the record has no new image. `[OldImage]` |
| `eventName` | `x-amz-ddb-event-name`: `INSERT`, `MODIFY` or `REMOVE` |
| `dynamodb.SequenceNumber` | `x-amz-ddb-sequence-number` |
| `eventSourceARN` | `x-amz-ddb-stream-arn`, and the route |
| `dynamodb.StreamViewType` | `x-amz-ddb-stream-view-type`, such as `NEW_AND_OLD_IMAGES` |
| `dynamodb.Keys`, `dynamodb.ApproximateCreationDateTime`, `dynamodb.SizeBytes`, `eventID`, `awsRegion`, `userIdentity` | Not a header. `DynamoDbChange.Record` |

Header names match without regard to case. A handler reads the headers through an `IExecutionRequest` parameter, which is in namespace `Hardened.Requests.Abstract.Execution`. Under `[FunctionTesting]` alone the request has no headers, so this handler reads them with `TryGetValue`:

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
        request.Headers.TryGetValue("x-amz-ddb-event-name", out var eventName);
        request.Headers.TryGetValue("x-amz-ddb-sequence-number", out var sequenceNumber);

        logger.LogInformation(
            "{EventName} of order {Id} at {SequenceNumber}",
            eventName.ToString(),
            order.Id,
            sequenceNumber.ToString()
        );

        log.Record(order);
    }
}
```

For this event, the handler logs `INSERT of order A-1 at 111`, `MODIFY of order A-1 at 222` and `REMOVE of order A-1 at 333`:

```json
{
  "Records": [
    {
      "eventID": "c4ca4238a0b923820dcc509a6f75849b",
      "eventName": "INSERT",
      "eventVersion": "1.1",
      "eventSource": "aws:dynamodb",
      "awsRegion": "us-east-1",
      "dynamodb": {
        "ApproximateCreationDateTime": 1790150400,
        "Keys": {"id": {"S": "A-1"}},
        "NewImage": {"id": {"S": "A-1"}, "quantity": {"N": "2"}},
        "SequenceNumber": "111",
        "SizeBytes": 26,
        "StreamViewType": "NEW_AND_OLD_IMAGES"
      },
      "eventSourceARN": "arn:aws:dynamodb:us-east-1:123456789012:table/orders/stream/2026-09-23T10:00:00.000"
    },
    {
      "eventID": "c81e728d9d4c2f636f067f89cc14862c",
      "eventName": "MODIFY",
      "eventVersion": "1.1",
      "eventSource": "aws:dynamodb",
      "awsRegion": "us-east-1",
      "dynamodb": {
        "ApproximateCreationDateTime": 1790150460,
        "Keys": {"id": {"S": "A-1"}},
        "NewImage": {"id": {"S": "A-1"}, "quantity": {"N": "3"}},
        "OldImage": {"id": {"S": "A-1"}, "quantity": {"N": "2"}},
        "SequenceNumber": "222",
        "SizeBytes": 52,
        "StreamViewType": "NEW_AND_OLD_IMAGES"
      },
      "eventSourceARN": "arn:aws:dynamodb:us-east-1:123456789012:table/orders/stream/2026-09-23T10:00:00.000"
    },
    {
      "eventID": "eccbc87e4b5ce2fe28308fd9f2a7baf3",
      "eventName": "REMOVE",
      "eventVersion": "1.1",
      "eventSource": "aws:dynamodb",
      "awsRegion": "us-east-1",
      "dynamodb": {
        "ApproximateCreationDateTime": 1790150520,
        "Keys": {"id": {"S": "A-1"}},
        "OldImage": {"id": {"S": "A-1"}, "quantity": {"N": "3"}},
        "SequenceNumber": "333",
        "SizeBytes": 26,
        "StreamViewType": "NEW_AND_OLD_IMAGES"
      },
      "eventSourceARN": "arn:aws:dynamodb:us-east-1:123456789012:table/orders/stream/2026-09-23T10:00:00.000"
    }
  ]
}
```

The AWS [Overview](/aws/) covers running the function locally and sending it an event.

Under `[Change]` the request is a `DynamoDbChange`, which is in namespace `Hardened.Aws.Lambda.DynamoDb`. Its `Record` property is the whole stream record, as a `DynamoDBEvent.DynamodbStreamRecord`. `Record` holds the fields that are not headers, such as `Keys`, `ApproximateCreationDateTime` and `UserIdentity`. Under `[FunctionTesting]` alone the request is not a `DynamoDbChange`.

A record for an item that Time to Live deleted carries a `userIdentity` whose `type` is `Service` and whose `principalId` is `dynamodb.amazonaws.com`. This handler tells a delete by Time to Live from the others:

```csharp
using Hardened.Aws.Lambda.DynamoDb;
using Hardened.Functions.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Change("orders")]
    public void OnOrderChanged(Order order, IExecutionRequest request)
    {
        if (request is DynamoDbChange { Record.UserIdentity.PrincipalId: "dynamodb.amazonaws.com" })
        {
            logger.LogInformation("Order {Id} expired", order.Id);
        }

        log.Record(order);
    }
}
```

## The item before and after the change

`[NewImage]` on a parameter binds the item after the change, and `[OldImage]` binds the item before it. Both bind the item in DynamoDB's own form, with the type tags on. Both attributes are in namespace `Hardened.Aws.Lambda.DynamoDb`. The parameter's type is `IDictionary<string, DynamoDBEvent.AttributeValue>`, from namespace `Amazon.Lambda.DynamoDBEvents`. Each `AttributeValue` has one member set, named for its type tag, such as `S`, `N` or `M`.

```csharp
using Amazon.Lambda.DynamoDBEvents;
using Hardened.Aws.Lambda.DynamoDb;
using Hardened.Functions.Runtime.Attributes;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Change("orders")]
    public void OnOrderChanged(
        Order order,
        [OldImage] IDictionary<string, DynamoDBEvent.AttributeValue>? before,
        [NewImage] IDictionary<string, DynamoDBEvent.AttributeValue>? after
    )
    {
        if (before != null && after != null)
        {
            logger.LogInformation(
                "Order {Id} quantity changed from {Before} to {After}",
                order.Id,
                before["quantity"].N,
                after["quantity"].N
            );
        }

        log.Record(order);
    }
}
```

For the event in [Headers and the stream record](#headers-and-the-stream-record), this handler logs `Order A-1 quantity changed from 2 to 3` for the `MODIFY`. It logs nothing for the `INSERT` and the `REMOVE`. On a stream with `NEW_AND_OLD_IMAGES`, the two attributes bind these items:

| Event name | `[NewImage]` | `[OldImage]` |
|---|---|---|
| `INSERT` | The item after the change | Null |
| `MODIFY` | The item after the change | The item before the change |
| `REMOVE` | Null | The item before the change |

A stream whose view type leaves an image out gives null for it on every record: `[OldImage]` on `NEW_IMAGE`, `[NewImage]` on `OLD_IMAGE`, and both on `KEYS_ONLY`.

The images keep three things that a bound parameter loses: whether an attribute is `NULL` or absent, whether an array was a set or a list, and a number's exact text.

On a parameter of any other class type, such as `Order`, either attribute binds null on every record, with no error.

The attributes read the stream record off the request, so they bind only when DynamoDB Streams delivered it. Otherwise the handler fails with `InvalidOperationException`. For `[OldImage]`, the message is "[OldImage] was bound on a handler that is not serving a DynamoDB change. It reads the stream record off the request, so it only works under [Change]."

## Failed records

A record fails when its handler throws or its item does not bind. An invocation that does not fail answers with a `batchItemFailures` report. `DynamoDbStreamsModule` has one setting, `ReportBatchItemFailures`, and it is off by default. This table gives the result of each failure with the setting off and on:

| Case | `ReportBatchItemFailures` off | `ReportBatchItemFailures` on |
|---|---|---|
| A handler throws, or an item does not bind | The invocation fails, the later records do not run, and Lambda retries the whole batch | The invocation succeeds, the later records do not run, the report names the failed record, and Lambda retries from it |
| No handler names the table | The invocation fails | The invocation fails |
| A record has a binary attribute | The invocation fails before any handler runs | The invocation fails before any handler runs |

With the setting off, the first failed record fails the invocation with its exception. Lambda then retries the whole batch, including the records that ran. While it retries, Lambda sends the function no later records from that shard. With the event source mapping's default settings, Lambda retries until the records expire. DynamoDB Streams keeps a record for 24 hours, so one failing record can hold its shard for up to a day. The report is always empty, `{"batchItemFailures":[]}`.

The records of one invocation run one after another and share its cancellation token, so the function's timeout has to cover the whole batch. The AWS [Overview](/aws/) covers when the token is cancelled.

## Reporting failed records

To turn `ReportBatchItemFailures` on, put `[DynamoDbStreamsModule(ReportBatchItemFailures = true)]` on the application class. The module is in namespace `Hardened.Aws.Lambda.DynamoDb`.

```csharp
using Hardened.Aws.Lambda.DynamoDb;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[DynamoDbStreamsModule(ReportBatchItemFailures = true)]
public partial class Application;
```

The build then registers only the module that the application declares.

With the setting on, the batch stops at the failed record, and the report names that record by its sequence number. This is the `Checkpoint` failure mode. [Triggers](/guide/triggers) covers `BatchFailureMode`. For a batch of three whose second record failed, the function answers:

```json
{"batchItemFailures":[{"itemIdentifier":"200"}]}
```

A batch whose first record fails is still an invocation that succeeds, with the first record in the report.

The setting has to match `ReportBatchItemFailures` on the table's event source mapping. [Deploying](#deploying) shows the mapping's option. With `ReportBatchItemFailures` on the mapping and off in the application, the invocation fails at the first failed record, and Lambda retries the whole batch.

::: warning
With `[DynamoDbStreamsModule(ReportBatchItemFailures = true)]` and an event source mapping without `ReportBatchItemFailures`, Lambda ignores the report. It treats an invocation that did not fail as a complete success and moves past the whole batch. The failed record and every record after it are never handled. Turn the setting on in both places or in neither.
:::

## Retries and discarded records

The event source mapping's settings in this table limit the retries and split a failed batch:

| Setting | CLI option | Default | What Lambda does |
|---|---|---|---|
| `MaximumRetryAttempts` | `--maximum-retry-attempts` | -1: until the records expire | Retries a failed batch up to this many times, then discards it |
| `MaximumRecordAgeInSeconds` | `--maximum-record-age-in-seconds` | -1: until the records expire | Discards records older than this many seconds. The most is 604,800 |
| `BisectBatchOnFunctionError` | `--bisect-batch-on-function-error` | Off | Splits a failed batch in two before retrying. A split does not count as a retry |
| `DestinationConfig` | `--destination-config` | None | Sends a record of each discarded batch to the destination |

When an invocation fails and `BisectBatchOnFunctionError` is on, Lambda splits the batch in two and retries each half. It does this with or without `ReportBatchItemFailures`. With both `BisectBatchOnFunctionError` and `ReportBatchItemFailures` on, Lambda splits a reported batch at the named sequence number and retries only the records from there.

When retries run out, Lambda discards the batch's records and goes on with the shard. A destination on an SQS queue or an SNS topic receives the discarded batch's shard, first and last sequence numbers and stream ARN. It does not receive the records, so the records have to be read from the stream before they expire. A destination on an S3 bucket also receives the batch's records.

This mapping gives up after ten retries, splits a failed batch, and sends a record of a discarded batch to an SQS queue:

```bash
aws lambda create-event-source-mapping \
    --function-name Orders \
    --event-source-arn arn:aws:dynamodb:us-east-1:123456789012:table/orders/stream/2026-09-23T10:00:00.000 \
    --starting-position TRIM_HORIZON \
    --function-response-types ReportBatchItemFailures \
    --maximum-retry-attempts 10 \
    --bisect-batch-on-function-error \
    --destination-config '{"OnFailure": {"Destination": "arn:aws:sqs:us-east-1:123456789012:orders-failed"}}'
```

Lambda can deliver a record more than once, even when no invocation failed.

AWS's documentation covers [the stream's event source mapping](https://docs.aws.amazon.com/lambda/latest/dg/with-ddb.html), [partial batch responses](https://docs.aws.amazon.com/lambda/latest/dg/services-ddb-batchfailurereporting.html) and [discarded records](https://docs.aws.amazon.com/lambda/latest/dg/services-dynamodb-errors.html).

## Deploying

The table's stream has to be turned on, with a view type that says which images each record carries. `NEW_AND_OLD_IMAGES` carries both images. With it, the handler binds the item on every event name, and `[OldImage]` has the previous item on a `MODIFY`. A stream's view type cannot be changed. Changing it means turning the stream off and turning on a new one. A stream that is turned off and on again is a new stream with a new ARN. The handler's route stays `CHANGE /orders`.

An event source mapping connects the stream to the function. Its event source is the stream's ARN, which DynamoDB returns as the table's `LatestStreamArn`. The table's name is the name in `[Change]`.

A mapping on a stream needs a starting position. `TRIM_HORIZON` reads every record the stream holds, and `LATEST` reads only new ones. A new mapping can take several minutes to start reading, and with `LATEST` it can miss the records written in that time. AWS advises `TRIM_HORIZON`.

`--function-response-types ReportBatchItemFailures` turns reporting on in the mapping, and matches `[DynamoDbStreamsModule(ReportBatchItemFailures = true)]`. Leave the option out when the application leaves the setting off. The mapping's batch size sets how many records one invocation carries. It is 100 by default, and up to 10,000.

The function's execution role needs the permissions in the AWS managed policy `AWSLambdaDynamoDBExecutionRole` to read the stream.

These commands turn on the stream, read its ARN and create the mapping. `Orders` is the deployed function's name. The ARN in the third command is the table's `LatestStreamArn`, which the second command asks for.

```bash
aws dynamodb update-table --table-name orders \
    --stream-specification StreamEnabled=true,StreamViewType=NEW_AND_OLD_IMAGES

aws dynamodb describe-table --table-name orders \
    --query Table.LatestStreamArn --output text

aws lambda create-event-source-mapping \
    --function-name Orders \
    --event-source-arn arn:aws:dynamodb:us-east-1:123456789012:table/orders/stream/2026-09-23T10:00:00.000 \
    --starting-position TRIM_HORIZON \
    --function-response-types ReportBatchItemFailures
```

A mapping with a tumbling window fails every invocation. Lambda treats an answer without a `state` property as a failed invocation, and the function answers only `batchItemFailures`.

Deploying the function itself is the same for every trigger. The AWS [Overview](/aws/) covers it.

## Testing

The template writes this test in `tests/Orders.Tests/OrderHandlerTests.cs`. It sends a change through the `Application.Changes` façade:

```csharp
using Hardened.Shared.Testing.Attributes;
using Xunit;

namespace Orders.Tests;

public class OrderHandlerTests
{
    [HardenedTest]
    public async Task AChangedRowReachesTheHandler(Application.Changes changes, OrderLog log)
    {
        await changes.Orders(new Order { Id = "A-1", Quantity = 2 });

        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

The template's test project declares `[assembly: LambdaTesting]`. The attribute delivers each façade call as one DynamoDB Streams event, with a record for each message. Each record is a `MODIFY` whose new and old images are both the message, written with DynamoDB's type tags. An array in the message becomes a list, `L`, and never a set. The records carry these values:

| Field | Value |
|---|---|
| Sequence number | `4421584500000000017450439000`, `4421584500000000017450439001` and on |
| Stream ARN | `arn:aws:dynamodb:us-east-1:123456789012:table/orders/stream/2026-01-01T00:00:00.000` |
| View type | `NEW_AND_OLD_IMAGES` |

Under `[FunctionTesting]` alone the request has no headers. A handler that binds `[NewImage]` or `[OldImage]` fails there with the `InvalidOperationException` from [The item before and after the change](#the-item-before-and-after-the-change). The handlers in [Headers and the stream record](#headers-and-the-stream-record) pass their tests under `[LambdaTesting]` and under `[FunctionTesting]` alone. The handler that compares the two images passes under `[LambdaTesting]` only.

A handler whose only parameters are images gets a façade method with no parameters. Under `[LambdaTesting]` the call fails with "No handler is registered for CHANGE /. An event source is wired to this function that no trigger attribute declared." Under `[FunctionTesting]` alone the handler does not run.

What a failed message does to the façade call depends on the delivery and on `ReportBatchItemFailures`. [Testing functions](/guide/testing-functions) covers it. The façade call does not return the `batchItemFailures` report. The AWS [Testing](/aws/testing) page covers reading it.

## Next

| Page | Covers |
|---|---|
| [Triggers](/guide/triggers) | The trigger attributes, the change feeds of the three clouds, and `BatchFailureMode` |
| [DynamoDB client](/aws/dynamodb) | Reading and writing the table |
| [Streams](/aws/stream) | Kinesis Data Streams, the other ordered source |
| AWS [Overview](/aws/) | The packages, `Program.cs`, running locally and deploying a function |
| [Testing functions](/guide/testing-functions) | Testing a trigger handler |
