# DynamoDB client

`[DynamoDbClientModule]` on the application class registers `IDynamoDbClientProvider`. The
provider's `GetClient()` returns the default client, and `GetClient(name)` returns the client
configured under that name.

The example is the queue function from [Queues](/aws/queue). `Order` has a string `Id` and an int
`Quantity`. The application class in `src/Orders/Application.cs` carries the module:

```csharp
using Hardened.Aws.DynamoDbClient;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[DynamoDbClientModule]
public partial class Application;
```

`OrderStore` in `src/Orders/OrderStore.cs` takes the provider and saves each order to the table
`orders`:

```csharp
using Amazon.DynamoDBv2.Model;
using DependencyModules.Runtime.Attributes;
using Hardened.Aws.DynamoDbClient;

namespace Orders;

[SingletonService]
public class OrderStore(IDynamoDbClientProvider clients)
{
    public Task Save(Order order) =>
        clients
            .GetClient()
            .PutItemAsync(
                "orders",
                new Dictionary<string, AttributeValue>
                {
                    ["id"] = new AttributeValue(order.Id),
                    ["quantity"] = new AttributeValue { N = order.Quantity.ToString() },
                }
            );
}
```

The handler in `src/Orders/OrderHandler.cs` passes each order to the store:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderStore store)
{
    [Queue("orders")]
    public Task OnOrder(Order order) => store.Save(order);
}
```

`GetClient` returns the AWS SDK's `IAmazonDynamoDB`, with or without a name. The provider builds a
client the first time a call asks for it. After that, the provider returns the same client for the
life of the process. The module and the provider are in the namespace `Hardened.Aws.DynamoDbClient`.

`[DynamoDbStreamsModule]`, in `Hardened.Aws.Lambda.DynamoDb`, serves `[Change]` handlers from a
table's stream. [Changes](/aws/change) covers it.

## Packages

| Package | Contents |
|---|---|
| `Hardened.Aws.DynamoDbClient` | The module and the provider |
| `Hardened.Aws.DynamoDbClient.Testing` | `[LocalDynamoDb]`, for a test project |

The application's project references `Hardened.Aws.DynamoDbClient`:

```xml
<PackageReference Include="Hardened.Aws.DynamoDbClient" Version="0.0.0-HARDENED-VERSION" />
```

The package brings `AWSSDK.DynamoDBv2` 3.7.513.4 and `Hardened.Shared.Runtime`. It brings no host
package, so a web application and a function can both use it. Both packages ship in each release.

## The default client

Two environment variables shape the default client: `DYNAMODB_SERVICE_URL` and `AWS_REGION`. An
empty or blank value counts as not set.

| `DYNAMODB_SERVICE_URL` | `AWS_REGION` | The default client |
|---|---|---|
| Not set | Not set | The AWS SDK finds the Region and the credentials |
| Not set | Set | That Region. The AWS SDK finds the credentials |
| Set | Not set | That endpoint, signing as `us-east-1`, with the access key `local` |
| Set | Set | That endpoint, signing for that Region, with the access key `local` |

With `DYNAMODB_SERVICE_URL` set, the client signs its requests with the access key `local` and the
secret `local`. It uses no credentials from the environment.

On Lambda, `AWS_REGION` is the function's Region. A function's configuration cannot set it. Lambda
also puts the execution role's keys in the environment, where the AWS SDK finds them. With
`DYNAMODB_SERVICE_URL` unset, the default client on Lambda reaches DynamoDB in the function's Region,
with the function's role. A table in another Region needs a named client.

The AWS documentation covers
[the variables that Lambda sets](https://docs.aws.amazon.com/lambda/latest/dg/configuration-envvars.html).

## Running against DynamoDB Local

`DYNAMODB_SERVICE_URL` points the default client at DynamoDB Local. From the solution directory,
these commands start DynamoDB Local, create the table and run the function:

```bash
docker run -d -p 8000:8000 amazon/dynamodb-local

export AWS_ACCESS_KEY_ID=local AWS_SECRET_ACCESS_KEY=local AWS_REGION=us-east-1

aws dynamodb create-table --endpoint-url http://localhost:8000 \
    --table-name orders \
    --attribute-definitions AttributeName=id,AttributeType=S \
    --key-schema AttributeName=id,KeyType=HASH \
    --billing-mode PAY_PER_REQUEST

DYNAMODB_SERVICE_URL=http://localhost:8000 dotnet run --project src/Orders
```

The AWS [Overview](/aws/) shows how to send the function an event while it runs locally. After the
`aws lambda invoke` on that page sends the function an SQS message with the body
`{"id":"A-1","quantity":2}`, the item is in the table:

```console
$ aws dynamodb get-item --endpoint-url http://localhost:8000 --table-name orders --key '{"id":{"S":"A-1"}}'
{
    "Item": {
        "id": {
            "S": "A-1"
        },
        "quantity": {
            "N": "2"
        }
    }
}
```

DynamoDB Local keeps separate tables for each access key and Region, unless it runs with
`-sharedDb`. A tool that creates the tables must use the access key `local` and the Region that the
function signs with. The commands above export both. When the table was created under another
Region, the function fails each message with a `ResourceNotFoundException` that reads
`Cannot do operations on a non-existent table`.

The `amazon/dynamodb-local` image keeps its tables in memory. The tables are gone when the
container stops. The AWS documentation covers
[DynamoDB Local and its options](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/DynamoDBLocal.UsageNotes.html).

## Named clients

`DynamoDbOptions`, in `Hardened.Aws.DynamoDbClient`, is the configuration model behind the
provider. It has four properties:

| Property | Type | Set by |
|---|---|---|
| `ServiceUrl` | `string` | `DYNAMODB_SERVICE_URL`. Empty when the variable is not set |
| `Region` | `string` | `AWS_REGION`. Empty when the variable is not set |
| `Clients` | `Dictionary<string, Func<IServiceProvider, IAmazonDynamoDB>>` | An amendment. Empty by default |
| `DefaultClient` | `Func<IServiceProvider, IAmazonDynamoDB>?` | An amendment. `null` by default |

An amendment puts a factory in `Clients` under a name. `src/Orders/ApplicationConfiguration.cs` is
a second file of the `partial class Application`. It adds the client `audit`:

```csharp
using Amazon;
using Amazon.DynamoDBv2;
using DependencyModules.Runtime.Interfaces;
using Hardened.Aws.DynamoDbClient;
using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Orders;

public partial class Application : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        var config = new AppConfig();

        config.Amend((DynamoDbOptions options) =>
            options.Clients["audit"] = _ => new AmazonDynamoDBClient(RegionEndpoint.EUWest1)
        );

        services.AddSingleton<IConfigurationPackage>(config);
    }
}
```

[Configuration](/guide/configuration) covers `AppConfig`, `Amend` and amending in one environment.

`GetClient(name)` returns the client that the factory builds:

```csharp
var audit = clients.GetClient("audit");
```

The factory receives the application's `IServiceProvider`. The provider runs a factory the first
time a call asks for its name, not at startup. A named client is exactly what its factory builds.
`DYNAMODB_SERVICE_URL` and `AWS_REGION` shape only the default client.

Names match exactly, including case. `GetClient` with a name that nothing configured throws
`InvalidOperationException`:

```text
No DynamoDB client is configured under the name 'typo'. Configured names: 'audit'.
```

## Replacing the default client

`DefaultClient` replaces how the default client is built. The provider then ignores
`DYNAMODB_SERVICE_URL` and `AWS_REGION`. This amendment, in place of the `Amend` call above, gives
the default client its own retry count:

```csharp
config.Amend((DynamoDbOptions options) =>
    options.DefaultClient = _ =>
        new AmazonDynamoDBClient(new AmazonDynamoDBConfig { MaxErrorRetry = 5 })
);
```

## Client lifetime

Code that disposes a client from the provider breaks that client for every later caller, because
the provider keeps it. After `using (var client = clients.GetClient())`, the next `GetClient()`
returns the disposed client. A request on that client throws `ObjectDisposedException`:

```text
Cannot access a disposed object. Object name: 'Amazon.DynamoDBv2.AmazonDynamoDBClient'.
```

When the provider is disposed, it disposes every client it built.

## Testing against DynamoDB Local

`[LocalDynamoDb]` is `LocalDynamoDbAttribute`, in the namespace
`Hardened.Aws.DynamoDbClient.Testing`. The package of the same name brings `Testcontainers.DynamoDb`
4.14.0. The test project references it:

```xml
<PackageReference Include="Hardened.Aws.DynamoDbClient.Testing" Version="0.0.0-HARDENED-VERSION" />
```

The attribute registers its own provider over the application's. That provider returns the same
client for every name, whatever `Clients` and `DefaultClient` say. The client points at DynamoDB
Local in a Docker container. Testcontainers starts the container when a test first asks the provider
for a client. The attribute needs a running Docker daemon. Without one, the test fails when the
container starts.

A class derived from the attribute overrides `DdbSetup` to create the tables. `OrdersTableAttribute`
in `tests/Orders.Tests/OrdersTableAttribute.cs` creates the table `orders`:

```csharp
using System.Reflection;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Hardened.Aws.DynamoDbClient;
using Hardened.Aws.DynamoDbClient.Testing;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Orders.Tests;

public sealed class OrdersTableAttribute : LocalDynamoDbAttribute
{
    protected override async Task DdbSetup(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        IHardenedEnvironment environment,
        IServiceProvider serviceProvider
    )
    {
        var client = serviceProvider.GetRequiredService<IDynamoDbClientProvider>().GetClient();

        try
        {
            await client.CreateTableAsync(
                new CreateTableRequest
                {
                    TableName = "orders",
                    KeySchema = [new KeySchemaElement("id", KeyType.HASH)],
                    AttributeDefinitions = [new AttributeDefinition("id", ScalarAttributeType.S)],
                    BillingMode = BillingMode.PAY_PER_REQUEST,
                }
            );
        }
        catch (ResourceInUseException)
        {
            // An earlier container created the table.
        }
    }
}
```

The test in `tests/Orders.Tests/OrderStoreTests.cs` sends an order to the handler. It reads what the
handler wrote through its own provider parameter:

```csharp
using Amazon.DynamoDBv2.Model;
using Hardened.Aws.DynamoDbClient;
using Hardened.Shared.Testing.Attributes;
using Xunit;

namespace Orders.Tests;

public class OrderStoreTests
{
    [HardenedTest]
    [OrdersTable]
    public async Task AnOrderIsSaved(Application.Queues queues, IDynamoDbClientProvider clients)
    {
        await queues.Orders(new Order { Id = "A-1", Quantity = 2 });

        var response = await clients
            .GetClient()
            .GetItemAsync(
                "orders",
                new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue("A-1") }
            );

        Assert.Equal("2", response.Item["quantity"].N);
    }
}
```

`DdbSetup` runs once when the test starts, and again for each façade call. A `CreateTableAsync`
call for a table that exists throws `ResourceInUseException` with the message
`Cannot create preexisting table`. The exception fails the test, so the example catches it.
[Testing functions](/guide/testing-functions) covers the façades.

::: warning
A test that carries `[LocalDynamoDb]` passes without `[DynamoDbClientModule]` on the application
class, because the attribute registers its own provider. The function itself then fails every
message with `HandlerInstance is not an instance of Orders.OrderHandler`.
:::

One container runs for each image. Every test in the test run shares it. Tables and items stay from
one test to the next. Testcontainers removes the containers after the test run.

`Image` names the container image. The default is `amazon/dynamodb-local:latest`. Attributes with
different images get different containers:

```csharp
[OrdersTable(Image = "amazon/dynamodb-local:3.3.1")]
```

A test that needs no DynamoDB replaces the provider with `[Mock]`.
[Substituting services](/guide/testing-mocks) covers `[Mock]` and the test attribute interfaces that
`[LocalDynamoDb]` implements.

### The `LocalDynamoDb` class

`LocalDynamoDb`, a static class in `Hardened.Aws.DynamoDbClient.Testing`, starts and reaches the
same containers for a test that wires its clients some other way.

| Member | What it does |
|---|---|
| `LocalDynamoDb.Endpoint` | Starts the default image's container on first use, and returns its URL, such as `http://127.0.0.1:63374/` |
| `LocalDynamoDb.EndpointFor(image)` | The same, for another image |
| `LocalDynamoDb.CreateClient()`, `CreateClient(image)` | An `IAmazonDynamoDB` pointed at that image's container, signing with the access key `local` |
| `LocalDynamoDb.StopAll()` | Stops every container that the class started. The next call starts a new container, with no tables |
| `LocalDynamoDb.DefaultImage` | The constant `amazon/dynamodb-local:latest` |

## Next

- [Changes](/aws/change): reacting to changes in a DynamoDB table
- [Configuration](/guide/configuration): configuration models and `Amend`
- [Substituting services](/guide/testing-mocks): `[Mock]` and the test attribute interfaces
- [Overview](/aws/): the AWS packages, and running a function locally
