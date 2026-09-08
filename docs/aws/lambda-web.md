# API Gateway

`[ApiGatewayModule]` runs the routes you already wrote behind API Gateway. The controllers, filters
and binding are the same as [any web application](/guide/routing).

```csharp
using Hardened.Aws.Lambda.ApiGateway;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;

[HardenedModule]
[ApiGatewayModule]
public partial class Application;

public class ProductController {
    [Get("/api/products/{id}")]
    public Product GetProduct(string id) => _repository.Find(id);
}
```

```csharp
[HardenedTest]
public async Task GetsAProduct(ITestWebApp testWebApp) {
    var response = await testWebApp.Get("/api/products/42");

    response.Assert.Ok();
    Assert.Equal("42", response.Deserialize<Product>().Id);
}
```

`dotnet new hardened-web --host aws-lambda` writes this shape, and running it answers on 5080 the
way the Kestrel host does.

## The module

`[ApiGatewayModule]` brings the API Gateway payload adapter and, through the runtime module it
composes, the invocation loop and the web pipeline.

It is named here rather than inferred, which is the one place a Lambda application does name its
cloud. A function whose handlers sit beside its entry point gets its adapter from their
[trigger attributes](/guide/triggers) — `[Get]` and `[Post]` bind `HardenedHttpModule` like any
other trigger. The templates put the handlers in a library project and the entry point in a host
project, and a generator only sees the compilation it runs in, so the host says which adapter serves
the routes it cannot see. The Kestrel and ASP.NET Core hosts name theirs for the same reason.

The payload format is API Gateway HTTP API, version 2.0. There is no option for the REST API's
payload format 1.0.

## Configuration

Response headers are a [configuration model](/guide/configuration) the web runtime defines, and an
application amends it:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Shared.Runtime.Configuration;

[HardenedModule]
[ApiGatewayModule]
public partial class Application : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        var config = new AppConfig();

        config.Amend((ResponseHeaderConfiguration response) =>
            response.Add("Access-Control-Allow-Origin", "*"));

        services.AddSingleton<IConfigurationPackage>(config);
    }
}
```

## Running it locally

Run the host project, from the IDE or with `dotnet run`:

```bash
dotnet run --project src/Todos.Host
```

```
Started the AWS Lambda Test Tool on http://localhost:5050
Listening on http://localhost:5080
```

```bash
curl localhost:5080/todos
```

A Lambda web application has no HTTP server of its own, and nothing starts it locally the way the
Lambda service does. So when `AWS_LAMBDA_RUNTIME_API` is unset, `LambdaEmulator.StartIfLocal` in
`Program.cs` starts the
[AWS Lambda Test Tool](https://github.com/aws/aws-lambda-dotnet/tree/master/Tools/LambdaTestTool-v2)
as a child process and points the bootstrap at it. The tool's API Gateway emulator takes HTTP on
5080, turns each request into a payload format 2.0 event, and hands the function's response back as
HTTP. The debugger is on the process the Lambda service would start, running the same `Main`,
bootstrap and event serialization.

| Variable | Default | |
|---|---|---|
| `PORT` | `5080` | The API Gateway emulator, where the application answers |
| `HARDENED_LAMBDA_EMULATOR_PORT` | `5050` | The Lambda Runtime API emulator and the tool's page |
| `AWS_LAMBDA_RUNTIME_API` | unset | Set by Lambda and by the runtime interface emulator. When set, none of this runs |

The tool is a dotnet tool. The [templates](/guide/project-templates) pin it in the host project's
tool manifest and restore it in the build. Elsewhere,
`dotnet tool install -g amazon.lambda.testtool`. A tool left running by the debugger's stop button
is found on its port and reused by the next start.

::: info Program.cs is written, not generated
The host project has a `Main`, and the call to `LambdaEmulator.StartIfLocal` in it is what brings
the tool up. Deleting `Program.cs` leaves the project with no entry point at all, and the deployed
handler fails on its first invocation with "Entry point not found".
:::

## Response mode

A function answers in one of two ways, and the deployment picks which:

| `HARDENED_LAMBDA_RESPONSE_MODE` | What leaves the function | Front door |
|---|---|---|
| `buffered` (the default) | One payload format 2.0 response when the handler returns | An HTTP API, or a function URL in `BUFFERED` invoke mode |
| `stream` | A stream that opens at the first body byte | A function URL in `RESPONSE_STREAM` invoke mode |

The variable has to match the invoke mode of the front door in front of it. The wire protocol is
fixed there rather than chosen by the function: `RESPONSE_STREAM` expects an HTTP prelude before the
body, `BUFFERED` and an HTTP API expect the JSON envelope, and neither accepts the other. The event
the function receives is the same document either way, which is why something has to say which.

An unrecognised value fails the application at startup rather than falling back. A deployment that
spelt it wrong would otherwise run buffered behind a front door expecting the prelude, and the first
request would be a 500 with nothing in the logs to say why.

```csharp
// Amending it from the application, for a host that decides its own mode.
services.ConfigureLambdaResponseMode(mode => mode.Mode = LambdaResponseMode.Stream);
```

In `stream` mode the status, headers and cookies are sent as the prelude that opens the stream, and
they are read at the **first body byte** rather than when the handler returns. Anything set after
the first write is recorded on the response and never reaches the client. That is what makes a
refusal work: the pipeline serializes it before any handler wrote, so the stream opens with the
refusal's own status.

Only a web-shaped source can stream. A queue, a topic, a stream record or a scheduled rule has no
caller holding a connection, so a function serving one stays buffered under a `stream` variable
rather than failing. That keeps the setting safe to apply account-wide.

`[ServerSentEvents]` handlers need `stream`. Under `buffered` their events are delivered together
when the invocation ends, or never when the function times out first. See
[Streaming responses](/guide/streaming).

## Testing

Routes are ordinary Hardened routes, so [`ITestWebApp`](/guide/testing-web) drives them without any
Lambda involvement:

```csharp
[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]
```

That covers routing, binding, filters and serialization. It does not cover the API Gateway event
conversion. `[LambdaWebTesting]` puts a real proxy event through the invocation loop instead; see
[Testing AWS handlers](/aws/testing).

## Next

- [Triggers](/guide/triggers): the sources other than HTTP
- [Testing AWS handlers](/aws/testing): the two fidelity levels
- [Routing](/guide/routing): the routes themselves, unchanged by the host
