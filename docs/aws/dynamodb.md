# DynamoDB client

`IDynamoDbClientProvider` supplies DynamoDB clients by name, built on first use and kept for the
life of the process.

```csharp
using Hardened.Amz.DynamoDbClient;
using Hardened.Shared.Runtime.Attributes;

[HardenedModule]
[DynamoDbModule]
public partial class Application { }
```

```csharp
[SingletonService]
public class OrderRepository(IDynamoDbClientProvider clients) {

    public Task<GetItemResponse> Get(string id) =>
        clients.GetClient().GetItemAsync("orders", Key(id));
}
```

Deployed, nothing needs to be set: the SDK resolves credentials from the role and the region from
the environment. Locally, `DYNAMODB_SERVICE_URL=http://localhost:8000` points the default client
at DynamoDB Local. Source:
[`src/Clients/DynamoDb`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Clients/DynamoDb)
in [Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz).

## A provider rather than a client

```csharp
public interface IDynamoDbClientProvider {
    IAmazonDynamoDB GetClient(string clientName = "");
}
```

Reaching a second account, assuming a different role or talking to another region each need
their own credentials and configuration, which is what the name selects between. A client is
built the first time it is asked for and cached for the life of the process. The provider returns
the SDK's own interface, so a test can substitute a client without going through the provider.

## Configuring the default client

| Variable | Effect |
|---|---|
| `AWS_REGION` | The region. Left to the SDK's own resolution when unset, which is the deployed case |
| `DYNAMODB_SERVICE_URL` | Overrides the endpoint. This is what points a process at DynamoDB Local |

::: tip A region alongside an endpoint is carried as the signing region
`RegionEndpoint` and `ServiceURL` are mutually exclusive on the SDK's config. When both variables
are set, the region is applied as `AuthenticationRegion`. DynamoDB Local authenticates nothing,
but the SDK signs every request, so credentials still have to exist. The provider supplies
placeholders in that branch.
:::

## Named clients

Anything beyond the default is a factory registered under a name. The factory receives the
service provider, so it can resolve whatever it needs:

```csharp
config.Amend((DynamoDbOptions options) =>
    options.Clients["audit"] = provider =>
        new AmazonDynamoDBClient(
            provider.GetRequiredService<IAuditCredentials>().Resolve(),
            new AmazonDynamoDBConfig { RegionEndpoint = RegionEndpoint.USEast1 }));
```

```csharp
var auditClient = clients.GetClient("audit");
```

Asking for a name that was never configured throws an `InvalidOperationException` listing the
names that are configured.

To replace how the default client is built, set `DefaultClient` instead. `ServiceUrl` and
`Region` are then ignored:

```csharp
config.Amend((DynamoDbOptions options) =>
    options.DefaultClient = provider => new AmazonDynamoDBClient(RegionEndpoint.EUWest2));
```

See [Amending configuration](/guide/configuration#amending-configuration) for where `Amend` is
called from.

## Testing against a real DynamoDB

`[LocalDynamoDb]` points the provider at DynamoDB Local in a Testcontainers container. See
[DynamoDB Local](/aws/testing#dynamodb-local).

## Next

- [DynamoDB Streams](/aws/ddb-streams): handling the table's stream
- [Configuration](/guide/configuration): the model the options come from
- [Testing AWS handlers](/aws/testing#dynamodb-local): the container-backed client in a test
