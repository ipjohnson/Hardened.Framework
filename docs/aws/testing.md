# Testing AWS handlers

Every AWS runtime has a test harness that invokes the real pipeline in-process. There is no
`sam local`, no emulator for Lambda itself, and no deployment in the loop.

```csharp
[HardenedTest]
public async Task ProcessesAnOrder(LambdaTestApp app) {
    var response = await app.Invoke<OrderResponse>(
        "process-order", new OrderRequest { Sku = "SKU-1" });

    Assert.NotNull(response.OrderId);
}
```

The shape is the same as [any Hardened test](/guide/testing): an assembly attribute installs the
harness, another names the application, and the test method takes what it needs.

## Setup

```csharp
// Bootstrap.cs
using Hardened.Amz.Function.Lambda.Testing;
using Hardened.Shared.Testing.Attributes;

[assembly: LambdaFunctionTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]
```

`[LambdaFunctionTesting]` registers the test app and swaps in a filter provider that runs the
function pipeline without the Lambda bootstrap. The function, SQS and stream harnesses all sit on
it.

## Functions

`LambdaTestApp.Invoke` serializes the payload, invokes by function name, and deserializes the
response. The context is configurable, which is how a test drives timeout-sensitive behaviour:

```csharp
var response = await app.Invoke<OrderResponse>(
    "process-order",
    new OrderRequest { Sku = "SKU-1" },
    context => context.RemainingTime = TimeSpan.FromSeconds(2));
```

`InvokeRaw` takes the payload as a string, bypassing .NET serialization on the way in:

```csharp
[HardenedTest]
public async Task AcceptsTheWireFormat(LambdaTestApp app) {
    var response = await app.InvokeRaw<OrderResponse>(
        "process-order", """{"sku":"SKU-1","quantity":2}""");

    Assert.NotNull(response.OrderId);
}
```

`Invoke` serializes your object with the same serializer that reads it back, so the two agree by
construction and a gap in an AOT serializer context stays hidden. `InvokeRaw` starts from the
bytes the caller will send. Both have `Stream`-returning overloads for a response you would
rather inspect than deserialize.

## SQS batches

```csharp
[HardenedTest]
public async Task ProcessesTheBatch(TestSqsApp sqs) {
    var response = await sqs.SendMessage(
        new OrderMessage { OrderId = "A" },
        new OrderMessage { OrderId = "B" });

    Assert.Empty(response.BatchItemFailures);
}
```

Messages are identified by position, so the first is `"0"` and the second `"1"`, and a failure
can be traced back to the message that caused it. See [SQS](/aws/sqs#testing).

## Stream records

```csharp
[HardenedTest]
public async Task ProjectsAnInsert(TestDynamoDbStream stream) {
    var response = await stream.ProcessUpdates(
        new DynamoDBEvent.DynamodbStreamRecord {
            EventName = "INSERT",
            Dynamodb = new StreamRecord {
                NewImage = new Dictionary<string, AttributeValue> {
                    ["pk"] = new() { S = "ORDER#1" }
                }
            }
        });

    Assert.Empty(response.BatchItemFailures);
}
```

## DynamoDB Local

`[LocalDynamoDb]` points the application's `IDynamoDbClientProvider` at a real DynamoDB in a
container, so a test hits an engine that rejects a malformed key, enforces a key schema and fails
a conditional write exactly as the service does. Derive from it and override `DdbSetup` to create
the tables:

```csharp
using Hardened.Amz.DynamoDbClient;
using Hardened.Amz.DynamoDbClient.Testing;

public class OrdersDatabaseAttribute : LocalDynamoDbAttribute {
    protected override async Task DdbSetup(
        AttributeCollection attributes, MethodInfo method,
        IHardenedEnvironment environment, IServiceProvider services) {

        var client = services.GetRequiredService<IDynamoDbClientProvider>().GetClient();

        await client.CreateTableAsync(new CreateTableRequest {
            TableName = "orders",
            KeySchema = [new KeySchemaElement("pk", KeyType.HASH)],
            AttributeDefinitions = [new AttributeDefinition("pk", ScalarAttributeType.S)],
            BillingMode = BillingMode.PAY_PER_REQUEST
        });
    }
}
```

```csharp
[HardenedTest]
[OrdersDatabase]
public async Task StoresAnOrder(IOrderRepository repository) {
    await repository.Save(new Order("ORDER#1", 42));

    Assert.Equal(42, (await repository.Get("ORDER#1")).Total);
}
```

`DdbSetup` runs before every test carrying the attribute.

### Pinning the image

```csharp
[LocalDynamoDb(Image = "amazon/dynamodb-local:3.3.1")]
```

The default is `amazon/dynamodb-local:latest`. One container is started per image and shared by
every test in the process that names it, so tests needing isolation from one another should use
distinct keys rather than distinct databases. Every client name resolves to that same container.

### Without the rest of the package

`LocalDynamoDb` is usable on its own by a project that wires its clients some other way:

```csharp
var endpoint = LocalDynamoDb.Endpoint;                          // starts the default image
var endpoint = LocalDynamoDb.EndpointFor("amazon/dynamodb-local:3.3.1");
var client   = LocalDynamoDb.CreateClient();                    // already pointed at it
```

Nothing there knows what a table or a key looks like.

::: warning Docker has to be running
Testcontainers needs a Docker daemon. On a machine without one, these tests fail at container
startup rather than skipping.
:::

## Next

- [Writing a test](/guide/testing): what every Hardened test boots
- [Writing a test attribute](/guide/testing-attributes): the seams `[LocalDynamoDb]` is built on
- [Steps and retries](/guide/testing-steps): polling an eventually consistent store
