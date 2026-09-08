# Testing AWS handlers

Every AWS handler is tested in-process, through the real pipeline. There is no `sam local`, no
deployment and no AWS account in the loop. Running the application locally is a different thing,
with the AWS Lambda Test Tool underneath; see
[Running it locally](/aws/lambda-web#running-it-locally).

```csharp
[HardenedTest]
public async Task AMessageReachesTheHandler(Application.Queues queues, OrderLog log) {
    await queues.Orders(new Order { Id = "A-1", Quantity = 2 });

    Assert.Equal("A-1", Assert.Single(log.Orders).Id);
}
```

The shape is the same as [any Hardened test](/guide/testing): assembly attributes install the
harness and name the application, and the test method takes what it needs.

## Fidelity is a project setting, not a test one

Two attributes, and the second is optional:

```csharp
// Bootstrap.cs
using Hardened.Aws.Lambda.Testing;
using Hardened.Functions.Testing;
using Hardened.Shared.Testing.Attributes;

[assembly: FunctionTesting]
[assembly: LambdaTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]
```

`[FunctionTesting]` makes the generated [trigger façades](/guide/triggers#testing) resolvable. The
delivery it registers builds a request and runs the pipeline, which covers routing, binding, the
filters and the handler — and names no cloud.

`[LambdaTesting]` replaces that one thing. The payload is packed into the envelope AWS actually
sends and goes in through the invocation loop, so the adapter, the payload peek, the body encoding
that source uses and its metadata are exercised too.

**No test method changes when you add or remove it.** That is the point: the same tests run at
either level, and against another provider. Delete the line to drop back down.

Neither attribute cares which order it is applied in. The neutral delivery registers with `TryAdd`
and the Lambda one replaces it, so both orders end with the envelope delivery.

## The façades

One nested class per trigger kind on the entry point, with a method per source:

| Trigger | Façade | A method named for the |
|---|---|---|
| `[Queue]` | `Application.Queues` | queue |
| `[Topic]` | `Application.Topics` | topic |
| `[Timer]` | `Application.Timers` | schedule |
| `[Change]` | `Application.Changes` | table |
| `[Stream]` | `Application.Streams` | stream |
| `[Blob]` | `Application.Blobs` | bucket |
| `[HardenedFunction]` | `Application.Invocations` | operation |

The method exists because the handler does, and its parameter is the type the handler binds. A
renamed source or a changed payload is a compile error in the test rather than a test that quietly
passes against nothing.

`[Queue("orders-new")]` becomes `queues.OrdersNew(...)`. Passing several payloads sends a batch:

```csharp
[HardenedTest]
public async Task EveryMessageInABatchIsHandled(Application.Queues queues, OrderLog log) {
    await queues.OrdersNew(new Order { Id = "A-1" }, new Order { Id = "A-2" });

    Assert.Equal(2, log.Orders.Count);
}
```

An invocation returns, so its façade method returns the handler's declared type:

```csharp
[HardenedTest]
public async Task ProcessesAnOrder(Application.Invocations invocations) {
    var accepted = await invocations.Process(new Order { Id = "A-1" });

    Assert.Equal("A-1", accepted.Id);
}
```

::: warning Two sources that produce the same method name
`HRDF002` warns when two sources collide on one façade method, because only one of them can be
reached. Rename one, or suppress the diagnostic to keep the collision.
:::

## Substituting a dependency

`[Mock]` works as it does anywhere else, so a handler's collaborator is a test parameter:

```csharp
[HardenedTest]
public async Task AnOrderIsPlaced(Application.Queues queues, [Mock] IOrderStore store) {
    await queues.OrdersNew(new Order { Id = "a-1" });

    store.Received().Place(Arg.Is<Order>(order => order.Id == "a-1"));
}
```

The mock library is one package and one assembly attribute; see
[Substituting services](/guide/testing-mocks).

## DynamoDB Local

`[LocalDynamoDb]` points the application's `IDynamoDbClientProvider` at a real DynamoDB in a
container, so a test hits an engine that rejects a malformed key, enforces a key schema and fails a
conditional write exactly as the service does. Derive from it and override `DdbSetup` to create the
tables:

```csharp
using Hardened.Aws.DynamoDbClient;
using Hardened.Aws.DynamoDbClient.Testing;

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

## Web handlers behind API Gateway

A Lambda web application's routes are ordinary routes, so
[`ITestWebApp`](/guide/testing-web) drives them with no Lambda involvement:

```csharp
[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]
```

`[LambdaWebTesting]` swaps the host underneath without touching a test:

```csharp
[assembly: LambdaWebTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]
```

A test still writes `app.Get("/orders/o-1")` exactly as it would against Kestrel, and what runs is a
real payload format 2.0 event through the invocation handler and back out as a proxy response — so
the adapter, the request it builds, dispatch and the response writer are all exercised, and none of
it is visible in the test.

That is what makes the portability claim checkable rather than asserted: the same test file runs on
the pipeline, on Kestrel and here, and only an assembly attribute differs.

One behaviour does differ, because the transport does. `[LambdaWebTesting]` is terminal: API Gateway
has nothing behind it to hand an unmatched path to, so a path with no route is a 404 from the host
rather than a fall-through.

## Next

- [Triggers](/guide/triggers): the façades, and what each source delivers
- [Writing a test](/guide/testing): the harness this builds on
- [Test hosts](/guide/testing-hosts): the seam `[LambdaWebTesting]` plugs into
- [DynamoDB client](/aws/dynamodb): what `[LocalDynamoDb]` stands a container up for
