# Invocations

`[HardenedFunction]` marks a handler that a caller invokes directly. On Google Cloud the caller
sends a POST to `/_triggers/invoke/{name}` on the service.

```csharp
using Hardened.Requests.Abstract.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [HardenedFunction]
    public OrderAccepted Process(Order order)
    {
        log.Record(order);

        return new OrderAccepted(order.Id, log.Orders.Count);
    }
}

public record OrderAccepted(string Id, int Received);
```

```http
POST /_triggers/invoke/Process
Content-Type: application/json

{"id":"A-1","quantity":2}

HTTP/1.1 200 OK
Content-Type: application/json

{"id":"A-1","received":1}
```

The request's body binds to the handler's parameter. The handler's return value is the response
body. The same request reaches the handler on Cloud Run and on Cloud Functions 2nd gen.
`[HardenedFunction]` is in `Hardened.Requests.Abstract.Attributes`.

`dotnet new hardened-function -n Orders --host gcp` writes this function and its tests. The handler
is in `src/Orders/OrderHandler.cs`. `invoke` is the template's default `--trigger`. `Order` and
`OrderLog` are classes that the template writes in `src/Orders`. `Order` has a string `Id` and an
int `Quantity`. `OrderLog` is a `[SingletonService]` that keeps the orders it is given.

## Packages

A Cloud Run service that serves invocations references `Hardened.Gcp.CloudRun.Runtime` and
`Hardened.Gcp.CloudRun.Invoke`:

```xml
<PackageReference Include="Hardened.Gcp.CloudRun.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Gcp.CloudRun.Invoke" Version="0.0.0-HARDENED-VERSION" />
```

`Hardened.Gcp.CloudRun.Invoke` sets the build property `HardenedInvokeModule` to
`Hardened.Gcp.CloudRun.Invoke.InvokeModule`. The build registers that module on the application
class. The application class does not name an adapter module. A Cloud Functions 2nd gen function
references the same adapter package.

The Google Cloud [Overview](/gcp/) covers the other packages, the application class and the entry
point. [Hosts](/guide/hosts) shows the packages of a Cloud Functions 2nd gen function.
[Web services](/gcp/web) covers running the application as one.

## Handler names

The path segment after `/_triggers/invoke/` is the invocation's name. The request routes as
`INVOKE /{name}`. A handler with a name answers only that name. `src/Orders/PricingHandler.cs` adds
one beside the template's `OrderHandler`:

```csharp
using Hardened.Requests.Abstract.Attributes;

namespace Orders;

public class PricingHandler
{
    [HardenedFunction("quote-order")]
    public Quote QuoteOrder(Order order) => new(order.Id, order.Quantity * 5m);
}

public record Quote(string Id, decimal Total);
```

```http
POST /_triggers/invoke/quote-order
Content-Type: application/json

{"id":"A-1","quantity":2}

HTTP/1.1 200 OK
Content-Type: application/json

{"id":"A-1","total":10}
```

The attribute's argument decides which names a handler answers:

| Handler | Answers the name |
|---|---|
| `[HardenedFunction("quote-order")]` | `quote-order` |
| `[HardenedFunction]` on a method named `Process` | Any name that no named handler has, including `Process` |
| `[HardenedFunction("Process")]` on a method named `Process` | Any name that no named handler has, including `Process` |

A name equal to the method's name counts as no name. One service can hold several named handlers,
each at its own path. The name matches exactly, including case. A trailing slash after the name is
dropped.

With two handlers that have no name, one of them answers every invocation.
[Testing functions](/guide/testing-functions) covers this case.

In a service whose `[HardenedFunction]` handlers all have names, a name that no handler has answers
500 with an empty body. The service logs an `InvalidOperationException`:

```text
No handler is registered for INVOKE /Process. An event source is wired to this function that no trigger attribute declared.
```

Only a POST with a name after `/_triggers/invoke/` is an invocation. A GET to
`/_triggers/invoke/Process` and a POST to `/_triggers/invoke/` go to the service's web routes. They
answer 404 when no route matches.

::: warning
A handler with no name also receives every trigger delivery that no other handler's route matches.
In a service with a `[Queue("orders")]` handler beside the template's `Process`, a Pub/Sub push
from the subscription `payments` runs `Process`. The service answers 200, which acknowledges the
message. A mistyped invocation name also reaches `Process`. Give each `[HardenedFunction]` a name in
a service that holds more than one handler.
:::

[Triggers](/guide/triggers) covers declaring triggers. [Queues](/gcp/queue) covers Pub/Sub pushes.

## Request body and headers

The request's body binds as JSON to the handler's body parameter, whatever the request's
`Content-Type`. [Parameter binding](/guide/parameter-binding) covers which parameter is the body.
[JSON serialization](/guide/json) covers property names and options. A handler with no body
parameter answers a POST with no body.

Every header of the POST reaches the handler under its own name, including `Authorization`. A
handler reads the headers through an `IExecutionRequest` parameter. The interface is in
`Hardened.Requests.Abstract.Execution`. Header names match without regard to case.
`src/Orders/TenantHandler.cs` reads a `tenant` header:

```csharp
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;

namespace Orders;

public class TenantHandler
{
    [HardenedFunction("tenant-of")]
    public string TenantOf(Order order, IExecutionRequest request) =>
        request.Headers.TryGetValue("tenant", out var tenant) ? tenant.ToString() : "none";
}
```

```http
POST /_triggers/invoke/tenant-of
Content-Type: application/json
tenant: acme

{"id":"A-1","quantity":2}

HTTP/1.1 200 OK
Content-Type: application/json

"acme"
```

`[FromHeader]` does not bind on a `[HardenedFunction]`. The build succeeds. Every invocation then
answers 500. The service logs this message:

```text
Attribute type Hardened.Web.Runtime.Attributes.FromHeaderAttribute does not implement ICustomBindingAttribute
```

The query string and cookies do not reach the handler.

## Responses and failures

An invocation answers with these statuses and bodies:

| Case | Status | Body |
|---|---|---|
| The handler returns a value | 200 | The value, as JSON |
| The handler is `void` or `Task`, or returns null | 200 | Empty |
| The handler throws | 500 | `{"type":"ServerError","message":"The server could not complete this request.","details":""}` |
| The body does not bind | 400 | A `ValidationError` naming the field |
| A constraint fails | 400 | A `ValidationError` naming the field and the constraint |
| No handler has the name | 500 | Empty |

A response with a value has `Content-Type: application/json`. A handler that returns `Task<T>`
answers as one that returns `T` does. A Cloud Functions 2nd gen function gives the same status and
body in each case.

The message of the handler's exception does not reach the caller. The service logs the exception
with the route:

```text
INVOKE /fail request failed System.InvalidOperationException: Order A-1 was refused.
```

A body that the parameter cannot bind answers 400. The response body names the field and gives the
JSON reader's message:

```http
POST /_triggers/invoke/Process
Content-Type: application/json

{"id":"A-1","quantity":"two"}

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"order.quantity","code":"invalid","message":"The JSON value could not be converted to System.Int32."}]}
```

When the project references `ValidationModules.SourceGenerator`, a body that fails a constraint
answers 400 with the constraint's code and message. [Validation](/guide/validation) covers the
constraints.

Hardened does not repeat a failed invocation. `[Retry]` repeats a `[HardenedFunction]` only with
`AllowNonIdempotent = true`. [The execution pipeline](/guide/execution-pipeline) covers `[Retry]`.

## Changing the path prefix

`InvokeModule` has one setting:

| Property | Default | Effect |
|---|---|---|
| `Prefix` | `/_triggers/invoke/` | The path an invocation's name follows |

To change it, put `[InvokeModule(Prefix = "/operations/")]` on the application class. The module is
in the namespace `Hardened.Gcp.CloudRun.Invoke`.

```csharp
using Hardened.Gcp.CloudRun.Invoke;
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[CloudRunRuntime]
[InvokeModule(Prefix = "/operations/")]
public partial class Application;
```

```http
POST /operations/Process
Content-Type: application/json

{"id":"A-1","quantity":2}

HTTP/1.1 200 OK
Content-Type: application/json

{"id":"A-1","received":1}
```

With a prefix set, a POST under `/_triggers/invoke/` answers 404. The build then registers only the
module that the application class declares. The module adds a leading and a trailing slash to
`Prefix` when they are missing. `Prefix = "operations"` reads names under `/operations/`.

The `[InvokeModule]` in `Hardened.Aws.Lambda.Invoke` is a different module, with no settings.
[Invocations](/aws/invoke) on AWS covers it. `[CloudRunTesting]` does not read `Prefix`, as
[Testing an invocation](#testing-an-invocation) shows.

## Deploying

An invocation needs nothing wired to the service. The caller sends the POST to the service's URL,
with the path above. Cloud Run services are private by default. A caller needs the Cloud Run
Invoker role, `roles/run.invoker`. The caller sends an identity token in `Authorization`. Google's
example sends it with curl:

```bash
curl -H "Authorization: Bearer $(gcloud auth print-identity-token)" SERVICE_URL
```

Hardened does not check the token. The handler sees `Authorization` like any other header.

A Cloud Functions 2nd gen function that serves invocations is deployed as an HTTP function, with
`--trigger-http`. On a function, the path follows the function's URL, as it follows a service's. A
function created with the Cloud Functions v2 API also has a `cloudfunctions.net` URL.

The Google Cloud [Overview](/gcp/) covers deploying a service. [Web services](/gcp/web) covers
deploying a function. Google Cloud's
[Authentication overview](https://docs.cloud.google.com/run/docs/authenticating/overview) covers the
invoker role.
[Invoke with an HTTPS Request](https://docs.cloud.google.com/run/docs/triggering/https-request)
covers identity tokens.
[gcloud functions deploy](https://docs.cloud.google.com/sdk/gcloud/reference/functions/deploy)
covers `--trigger-http`.

## Testing an invocation

`Application.Invocations` is the façade a test calls. [Testing functions](/guide/testing-functions)
covers it, its method names and the return types it cannot carry.

The template's test project declares `[assembly: CloudRunTesting]` beside `[assembly: WebTesting]`.
Under `[CloudRunTesting]`, a call is a POST to `/_triggers/invoke/{name}` on the test's web host.
The call returns the response body, read back as the handler's return type.

`[CloudRunTesting]` posts to `/_triggers/invoke/` whatever `Prefix` the application sets. With
`[InvokeModule(Prefix = "/operations/")]`, each of the template's tests fails with an
`InvalidOperationException`:

```text
The source would not read the answer to INVOKE /Process as an acknowledgement: the service answered 404. It said: 
```

[Testing](/gcp/testing) on Google Cloud covers `[CloudRunTesting]`.

## Next

| Page | Covers |
|---|---|
| [Overview](/gcp/) | The packages, the entry point and deploying a service |
| [Triggers](/guide/triggers) | The other sources a handler can serve |
| [Testing functions](/guide/testing-functions) | Calling an invocation from a test through `Application.Invocations` |
| [Queues](/gcp/queue) | A service that Pub/Sub pushes messages to |
| [Web services](/gcp/web) | Running the same application as a Cloud Functions 2nd gen function |
