# DynamoDB Streams

A stream handler is a `[HardenedFunction]` that receives one record at a time. The runtime unpacks
the batch, forks the pipeline per record, and reports which records failed.

**Source:** [`src/Lambda/DynamoDbStream`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/DynamoDbStream)
in [Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz).

## An application

```csharp
using Hardened.Amz.Function.DDB.Runtime;
using Hardened.Shared.Runtime.Attributes;

[HardenedModule]
[DynamoStreamLambda]
public partial class Application { }
```

## A handler

`[NewImage]` and `[OldImage]` bind the record's images:

```csharp
using Amazon.DynamoDBv2.Model;
using Hardened.Amz.Function.DDB.Runtime.Attributes;
using Hardened.Requests.Abstract.Attributes;

public class OrderProjection {
    [HardenedFunction("project-orders")]
    public async Task Project(
        [NewImage] Dictionary<string, AttributeValue> newImage,
        [OldImage] Dictionary<string, AttributeValue> oldImage,
        IProjectionStore store) {

        await store.Apply(oldImage, newImage);
    }
}
```

Both bind as `Dictionary<string, AttributeValue>` and throw an `InvalidCastException` for any other
type — the attributes hand back exactly what the stream record carries, without a mapping layer
guessing at your schema.

Both are also custom binding attributes, which means they are ordinary
[`ICustomBindingAttribute`](/guide/parameter-binding#custom-binding) implementations reading a
record the pipeline put into the request scope. Nothing in the core framework knows about DynamoDB.

## Which records failed

Each record is processed on its own forked execution chain with its own request and response. A
record whose chain completes with a status below 300 — or with no status — succeeded; anything else,
or an exception, failed.

The runtime writes a `StreamsEventResponse` naming the failed records, so the stream retries only
those. A batch of a hundred with one poison record redelivers one record.

::: warning A handler that swallows its own exceptions reports success
The pipeline decides success from the response, so a `try`/`catch` that logs and returns normally
tells the runtime the record was processed. If a record should be retried, let the exception
propagate.
:::

To change what happens around a failure, register an `IBatchProcessorExceptionHandler` — it decides
whether the exception counts as a processed record:

```csharp
[SingletonService(As = typeof(IBatchProcessorExceptionHandler))]
public class DeadLetterOnPoison : IBatchProcessorExceptionHandler {
    public async Task<bool> HandleException(
        IExecutionContext context, ILogger logger, Exception exception) {

        if (exception is MalformedRecordException) {
            await _deadLetters.Send(context);

            return true;   // do not retry a record that will never parse
        }

        logger.LogError(exception, "Record processing failed");

        return false;
    }
}
```

## Testing

```csharp
[assembly: LambdaFunctionTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]
```

`TestDynamoDbStream` takes stream records and returns the response the runtime would have produced:

```csharp
[HardenedTest]
public async Task ProjectsAnInsert(TestDynamoDbStream stream, IProjectionStore store) {
    var response = await stream.ProcessUpdates(
        new DynamoDBEvent.DynamodbStreamRecord {
            EventName = "INSERT",
            Dynamodb = new StreamRecord {
                NewImage = new Dictionary<string, AttributeValue> {
                    ["pk"] = new() { S = "ORDER#1" },
                    ["total"] = new() { N = "42" }
                }
            }
        });

    Assert.Empty(response.BatchItemFailures);
    Assert.Equal(42, (await store.Find("ORDER#1")).Total);
}
```

Asserting on `BatchItemFailures` is the point: it is what determines whether the stream redelivers,
and it is the behaviour most easily broken by a change to error handling.
