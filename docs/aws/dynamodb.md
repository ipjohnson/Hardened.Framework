# DynamoDB client

`IDynamoDbClientProvider` supplies DynamoDB clients by name, built on first use and kept for the
life of the process.

**Source:** [`src/Clients/DynamoDb`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Clients/DynamoDb)
in [Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz).

## Wiring it up

```csharp
using Hardened.Amz.DynamoDbClient;
using Hardened.Shared.Runtime.Attributes;

[HardenedModule]
[DynamoDbModule]
public partial class Application { }
```

```csharp
public interface IDynamoDbClientProvider {
    IAmazonDynamoDB GetClient(string clientName = "");
}
```

```csharp
[SingletonService]
public class OrderRepository {
    private readonly IDynamoDbClientProvider _clients;

    public OrderRepository(IDynamoDbClientProvider clients) {
        _clients = clients;
    }

    public Task<GetItemResponse> Get(string id) =>
        _clients.GetClient().GetItemAsync("orders", Key(id));
}
```

## Why a provider rather than a client

One registration can only describe one client. Reaching a second account, assuming a different role
or talking to another region each need their own credentials and configuration, and a container that
resolves a single `IAmazonDynamoDB` has nowhere to put them.

Construction is also deferred: a client is built the first time it is asked for, not when the service
collection is built. And clients are cached — `AmazonDynamoDBClient` is thread-safe and owns a
connection pool, so constructing one per request is a well-known way to exhaust sockets.

The provider returns the SDK's own interface, so a test that wants to substitute a client can do so
without going through the provider at all.

## Configuring the default client

Two environment variables cover the common case:

| Variable | Effect |
|---|---|
| `AWS_REGION` | The region. Left to the SDK's own resolution when unset, which is the deployed case |
| `DYNAMODB_SERVICE_URL` | Overrides the endpoint. This is what points a process at DynamoDB Local |

Deployed, neither needs to be set: the SDK resolves credentials from the role and the region from the
environment.

::: tip A region alongside an endpoint is carried as the signing region
`RegionEndpoint` and `ServiceURL` are mutually exclusive on the SDK's config — assigning either
clears the other. When both variables are set, the region is applied as `AuthenticationRegion` so it
is not silently dropped. Without that, a process with both set, which is the ordinary local
development shape, signed every request as `us-east-1` whatever its region said.

DynamoDB Local authenticates nothing, but the SDK signs every request, so credentials still have to
exist. The provider supplies arbitrary placeholders in that branch.
:::

## Named clients

Anything beyond the default — different credentials, an assumed role, a second region, a custom retry
policy — is a factory registered under a name. The factory receives the service provider, so it can
resolve whatever it needs:

```csharp
config.Amend((DynamoDbOptions options) =>
    options.Clients["audit"] = provider =>
        new AmazonDynamoDBClient(
            provider.GetRequiredService<IAuditCredentials>().Resolve(),
            new AmazonDynamoDBConfig { RegionEndpoint = RegionEndpoint.USEast1 }));
```

```csharp
var auditClient = _clients.GetClient("audit");
```

Asking for a name that was never configured throws an `InvalidOperationException` that lists the
names that *are* configured — which turns a typo into a message that names the mistake rather than a
null reference somewhere later.

To replace how the default client is built, set `DefaultClient` instead. `ServiceUrl` and `Region`
are then ignored, because a caller supplying a factory has said everything:

```csharp
config.Amend((DynamoDbOptions options) =>
    options.DefaultClient = provider => new AmazonDynamoDBClient(RegionEndpoint.EUWest2));
```

See [Configuration](/guide/configuration#amending-configuration) for where `Amend` is called from.

## Testing against a real DynamoDB

`[LocalDynamoDb]` points the provider at DynamoDB Local in a Testcontainers container, so data tests
exercise the engine rather than a fake. See
[Testing AWS handlers](/aws/testing#dynamodb-local).
