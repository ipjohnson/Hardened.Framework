# Invocations

`[HardenedFunction]` is a direct invocation: the caller waits on the return value and gets it back
serialised. It is the one trigger shape that answers.

```csharp
using Hardened.Requests.Abstract.Attributes;

public class OrderHandler(OrderLog log) {

    [HardenedFunction]
    public OrderAccepted Process(Order order) {
        log.Record(order);

        return new OrderAccepted(order.Id, log.Orders.Count);
    }
}
```

## Packages

```xml
<PackageReference Include="Hardened.Gcp.CloudRun.Runtime" Version="0.31.0-rc1000" />
<PackageReference Include="Hardened.Gcp.CloudRun.Invoke" Version="0.31.0-rc1000" />
```

`dotnet new hardened-function --host gcp --trigger invoke` writes this shape with tests. It is the
template's default trigger.

## Invoked over HTTP

Cloud Run has no invoke API of its own; a service is invoked by sending it a request. The operation
is a POST to `/_triggers/invoke/{operation}`, the payload is the body, and the handler's return
value is the response body:

```bash
curl -X POST "https://orders-abc123-uc.a.run.app/_triggers/invoke/Process" \
    -H "Authorization: Bearer $(gcloud auth print-identity-token)" \
    -H "Content-Type: application/json" \
    -d '{"id":"A-1","quantity":2}'
```

```json
{"id":"A-1","received":1}
```

The name is the method's, or the string the attribute was given: `[HardenedFunction("process-order")]`
answers at `/_triggers/invoke/process-order`. Several operations can live in one service, each
selected by name.

A service deployed with `--no-allow-unauthenticated` takes an identity token from a caller with
the invoker role, which is what the `Authorization` header above carries. Nothing in the adapter
checks it; Cloud Run does, before the request reaches the container.

To put the operations somewhere other than `/_triggers/invoke/`, write the module out:

```csharp
using Hardened.Gcp.CloudRun.Invoke;

[HardenedModule]
[CloudRunRuntime]
[InvokeModule(Prefix = "/operations/")]
public partial class Application;
```

That is the one reason to write it out.

## Binding

The payload is deserialized into the parameter that is not a registered service. Everything else
binds as it does [everywhere else](/guide/parameter-binding). Returning a value rather than a
`Task` of one is fine, and both are invoked the same way; the generated test façade reads the
declared return type without unwrapping a `Task`, so a synchronous return is what lets a test
assert on the answer directly.

## Testing

```csharp
[HardenedTest]
public async Task ProcessesAnOrder(Application.Invocations invocations) {
    var accepted = await invocations.Process(new Order { Id = "A-1" });

    Assert.Equal("A-1", accepted.Id);
}
```

`Invocations` for `[HardenedFunction]`, with a method per operation that returns the handler's
declared type. Under `[assembly: CloudRunTesting]` that is the POST above, sent to the test's host,
with the answer read back from the response. See [Testing Cloud Run handlers](/gcp/testing).

## Next

- [Web services](/gcp/web): the routes a service can serve beside its operations
- [Timers](/gcp/timer): the other request whose route is in the URL
