# Lambda functions

A Lambda application is an ordinary Hardened application plus a `Program.cs` that starts the
invocation loop. The deployed artifact is the assembly, and the handler is its name alone.

```csharp
using Hardened.Shared.Runtime.Attributes;

namespace OrderIntake;

[HardenedModule]
public partial class Application;
```

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

`[HardenedFunction]` is a direct invocation: the caller waits on the return value and gets it back
serialised. It is the one trigger shape that answers. For a queue, a topic, a schedule or a stream,
see [Triggers](/guide/triggers).

`dotnet new hardened-function -n OrderIntake` writes this with tests.

## Packages

```xml
<ItemGroup>
    <PackageReference Include="Hardened.Shared.Runtime" Version="0.32.0-rc1000" />
    <PackageReference Include="Hardened.Requests.Runtime" Version="0.32.0-rc1000" />
    <PackageReference Include="Hardened.Functions.Runtime" Version="0.32.0-rc1000" />

    <!-- The host, and one adapter for the trigger the handler declares. -->
    <PackageReference Include="Hardened.Aws.Lambda.Runtime" Version="0.32.0-rc1000" />
    <PackageReference Include="Hardened.Aws.Lambda.Invoke" Version="0.32.0-rc1000" />

    <PackageReference Include="Hardened.Library.SourceGenerator" Version="0.32.0-rc1000" />
    <PackageReference Include="Hardened.Function.SourceGenerator" Version="0.32.0-rc1000"
                      PrivateAssets="all" />
</ItemGroup>
```

The adapter is a package rather than a flag, so a function carries only the event models it can
reach. Swap `.Invoke` for `.Sqs` and the same project serves a queue instead.

There is no `[LambdaFunctionModule]` to apply. The generator reads the `HardenedInvokeModule`
property `Hardened.Aws.Lambda.Invoke` declares and registers the adapter, which is what keeps the
cloud's name out of the application class.

## The entry point

`Program.cs` is written, not generated. It is short, and every line of it is doing something:

```csharp
using Hardened.Aws.Lambda.Runtime.Development;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderIntake;

using var emulator = await LambdaEmulator.StartIfLocal(typeof(Application));

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Information));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));

new Application().PopulateServiceCollection(services);

await HardenedLambdaBootstrap.Run(services.BuildServiceProvider());
```

The project is an `Exe`. A deployed handler is the assembly name alone, and the runtime starts that
only when the assembly has an entry point.

`LambdaEmulator.StartIfLocal` does nothing when the Lambda service started the process, because
`AWS_LAMBDA_RUNTIME_API` is already set. Started from an IDE or `dotnet run` there is no such
address, so it brings up the AWS Lambda Test Tool and sets the same variable the service would
have. Delete it and the function still deploys; only running it locally stops working.

`HardenedLambdaBootstrap.Run` builds the container before the loop starts. A missing registration is
then a cold start that fails immediately naming what was missing, rather than the first invocation
of the day failing while every later one on a warm sandbox succeeds.

::: warning One entry point
The project has a `Main`, so do not write a second one. Nothing generates it, and nothing will
warn you that the one you wrote replaced the bootstrap — the function will deploy and fail on its
first invocation.
:::

## Naming a function

The attribute's string names the operation:

```csharp
[HardenedFunction("process-order")]
public OrderResponse ProcessOrder(OrderRequest request) { ... }
```

Several operations can live in one assembly and one deployment artefact, each selected by name, so
a service ships as a set of operations without a project per operation. Omit the name and the
method name is used.

## Binding

The payload is deserialized into the parameter that is not a registered service. Everything else
binds as it does [everywhere else](/guide/parameter-binding):

```csharp
[HardenedFunction("process-order")]
public async Task<OrderResponse> ProcessOrder(OrderRequest request, IOrderService orders) =>
    await orders.Process(request);
```

Returning a value rather than a `Task` of one is fine, and both are invoked the same way. The
generated test façade reads the declared return type without unwrapping a `Task`, so a synchronous
return is what lets a test assert on the answer directly.

## The invocation deadline

Lambda gives an invocation a time limit, and the runtime turns it into the request's cancellation
token. It trips 500ms before the deadline rather than at it — cancelling exactly at the deadline
would tell a handler it was out of time at the moment Lambda killed it, with nothing left to do
about it. The margin is long enough to write a log line, report a batch failure or close a
connection.

An invocation that arrives with no time left is cancelled before the handler runs.

## Running it locally

```bash
dotnet run --project src/OrderIntake
```

```
Started the AWS Lambda Test Tool on http://localhost:5050
```

The tool's page on 5050 is where a payload is posted. A function that is not an HTTP API has no API
Gateway emulator and nothing on 5080 — that is [API Gateway](/aws/lambda-web#running-it-locally),
and it is the only shape that answers over HTTP locally.

The tool is a dotnet tool, pinned in the project's `.config/dotnet-tools.json` and restored during
the build. A failed restore is a warning rather than a broken build, because the deployed artifact
does not need it.

Most of the time there is nothing to run. The tests invoke the function through the real pipeline
with no AWS account and nothing to deploy; see [Testing AWS handlers](/aws/testing).

## Next

- [Triggers](/guide/triggers): the other seven sources a handler can name
- [Testing AWS handlers](/aws/testing): the façades, and the two fidelity levels
- [API Gateway](/aws/lambda-web): the same application behind HTTP
