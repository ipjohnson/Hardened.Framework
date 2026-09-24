# Hosts

The application module names its host with one attribute from the host's package, such as
`[KestrelRuntime]`. The host project's `Program.cs` adds the application's services to a service
collection and starts that host.

`dotnet new hardened-web -n Todos --host kestrel --openapi-ui false` writes this application module
in the host project, `src/Todos.Host`:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;

namespace Todos.Host;

[HardenedModule]
[KestrelRuntime]
[TodosLibrary]
public partial class Application;
```

It writes this `Program.cs` beside it:

```csharp
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Todos.Host;

var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var configured)
    ? configured
    : 5080;

var environment = new EnvironmentImpl(arguments: args);

var services = new ServiceCollection();

services.AddLogging(logging => logging.AddSimpleConsole(options => options.SingleLine = true));

services.AddHardenedEnvironment(environment);

new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(
    services,
    kestrel => kestrel.ListenAnyIP(port)
);

await app.StartAsync();

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Todos.Host");

logger.LogInformation("Listening on http://localhost:{Port}", port);

await app.RunAsync();
```

After `dotnet run --project src/Todos.Host`, the application answers:

```http
GET /todos/1

HTTP/1.1 200 OK
Content-Type: application/json

{"id":1,"title":"Read the generated code","done":true}
```

Handlers, filters, parameter binding and the generated routing table do not change with the host.
They are in the library project, `src/Todos`. The template writes the same `src/Todos` and
`src/Todos.Client` for every `--host` value. It writes a different `Program.cs` for each host. The
code on this page leaves out the template's comments.

## Attributes and packages

| Host | `--host` | Application attribute | Host packages |
|---|---|---|---|
| Kestrel, without the ASP.NET Core request pipeline | `kestrel` | `[KestrelRuntime]` | `Hardened.Web.Kestrel.Runtime` |
| ASP.NET Core | `aspnet` | `[AspNetCoreRuntime]` | `Hardened.Web.AspNetCore.Runtime` |
| AWS Lambda, behind an API Gateway HTTP API or a function URL | `aws-lambda` | `[LambdaHttpModule]` | `Hardened.Aws.Lambda.Runtime`, `Hardened.Aws.Lambda.Http`, `Amazon.Lambda.Logging.AspNetCore` |
| Google Cloud Run | `cloud-run` | `[CloudRunRuntime]` | `Hardened.Gcp.CloudRun.Runtime`, `Hardened.Web.Kestrel.Runtime` |
| Google Cloud Functions 2nd gen | none | `[CloudRunRuntime]` | The Cloud Run packages, `Hardened.Gcp.Functions.Runtime`, `Hardened.Gcp.Functions.SourceGenerator`, `Google.Cloud.Functions.Hosting` |
| Azure Functions, isolated worker | `azure-functions` | `[HttpModule]` | `Hardened.Azure.Functions.Runtime`, `Hardened.Azure.Functions.Http`, `Hardened.Azure.Functions.SourceGenerator`, `Microsoft.Azure.Functions.Worker.Sdk` |

Every host project also references `Hardened.Shared.Runtime`, `Hardened.Web.Runtime`,
`Hardened.Library.SourceGenerator` and `Hardened.Web.SourceGenerator`. Each attribute is in the
namespace with the same name as its package: `Hardened.Web.Kestrel.Runtime`,
`Hardened.Web.AspNetCore.Runtime`, `Hardened.Aws.Lambda.Http`, `Hardened.Gcp.CloudRun.Runtime` and
`Hardened.Azure.Functions.Http`. [Project templates](/guide/project-templates) describes `--host`
and the other template options. To host on Cloud Functions, change a Cloud Run host project as
[Google Cloud Functions](#google-cloud-functions) describes.

`[KestrelRuntime]`, `[AspNetCoreRuntime]` and `[CloudRunRuntime]` bring `[HardenedWebModule]`,
which serves the routes. `[LambdaHttpModule]` and `[HttpModule]` do not. On Lambda and Azure
Functions, put `[HardenedWebModule]` on the module that holds the routes. The template's library
module, `TodosLibrary`, has it. An application without it still builds. On Lambda, the first
request then fails with "This function declares no handlers. A verb attribute or a trigger
attribute on a method is what compiles one."

## Differences between hosts

The template's host project behaves differently on each host:

| Host | Start it with | Port | A path with no route | SIGTERM with a request in flight |
|---|---|---|---|---|
| Kestrel | `dotnet run --project src/Todos.Host` | `PORT`, or 5080, on every interface | 404 | The request finishes |
| ASP.NET Core | `dotnet run --project src/Todos.Host` | `PORT`, or 5080, on `localhost` only | Passed on to the rest of the ASP.NET Core pipeline | The request finishes |
| AWS Lambda | `dotnet run --project src/Todos.Host`, which also starts the AWS Lambda Test Tool | `PORT`, or 5080 | 404 | Not applicable: there is no server |
| Google Cloud Run | `dotnet run --project src/Todos.Host` | `PORT`, or 5080, on every interface | 404 | The request finishes, within 10 seconds |
| Google Cloud Functions | `dotnet run --project src/Todos.Host` | `PORT`, or 8080 | 404 | The request finishes |
| Azure Functions | `func start` in `src/Todos.Host` | 7071 | 404 | The Functions host owns the connection |

## Kestrel

The Kestrel host builds no `HttpContext` and runs no ASP.NET Core middleware.
`HardenedKestrelApplication.Create(services, configureKestrel)` builds the service provider from
the collection and a Kestrel server that uses it. The `configureKestrel` callback receives
Kestrel's `KestrelServerOptions`. Without the callback, the server listens on port 5000 on every
interface.

The callback also sets up HTTPS, with Kestrel's `UseHttps` on a listen address. In the Kestrel
`Program.cs` above, this call serves HTTPS on port 5443 with the ASP.NET Core development
certificate:

```csharp
using Microsoft.AspNetCore.Hosting;

await using var app = HardenedKestrelApplication.Create(
    services,
    kestrel => kestrel.ListenAnyIP(5443, listen => listen.UseHttps())
);
```

Without an argument, `UseHttps()` uses the development certificate that `dotnet dev-certs https`
creates. Other overloads take a certificate, a certificate file or a certificate store. A relative
certificate file path is resolved against the current directory.

`StartAsync` runs the registered startup services, adds routing, and then starts listening. The
startup services are the application's `IStartupService` registrations.
[Modules](/guide/modules) covers them.

`RunAsync` starts the server if `StartAsync` has not, and then waits. On Ctrl-C (SIGINT) or
SIGTERM, `RunAsync` stops the server and lets requests in flight finish before it returns.

The overload `RunAsync(signals, grace)` handles the signals it is given. It then stops the server
and lets requests in flight finish for up to `grace`. In the Kestrel `Program.cs` above, this call
replaces `await app.RunAsync();`:

```csharp
using System.Runtime.InteropServices;

await app.RunAsync([PosixSignal.SIGTERM, PosixSignal.SIGINT], TimeSpan.FromSeconds(10));
```

`AddHardenedKestrel(configureKestrel)` registers the same server as an `IHostedService`, for an
application that runs inside a .NET generic host. The generic host then owns configuration, logging
and shutdown. This `Program.cs` runs the application inside a generic host:

```csharp
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Todos.Host;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHardenedEnvironment(new EnvironmentImpl(arguments: args));

new Application().PopulateServiceCollection(builder.Services);

builder.Services.AddHardenedKestrel(kestrel => kestrel.ListenAnyIP(5080));

await builder.Build().RunAsync();
```

## ASP.NET Core

The template writes this `Program.cs` for ASP.NET Core:

```csharp
using Hardened.Shared.Runtime.Application;
using Hardened.Web.AspNetCore.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Todos.Host;

var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var configured)
    ? configured
    : 5080;

var environment = new EnvironmentImpl(arguments: args);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHardenedEnvironment(environment);

new Application().PopulateServiceCollection(builder.Services);

var app = builder.Build();

app.UseHardened();

app.Urls.Add($"http://localhost:{port}");

await app.StartAsync();

await app.WaitForShutdownAsync();
```

`app.UseHardened()` adds the Hardened middleware to the ASP.NET Core pipeline. It then runs the
registered startup services and adds routing as the last step of the Hardened middleware. Without
`app.UseHardened()`, every Hardened route answers 404. The application still starts. Nothing is
logged.

`UseHardened` waits up to 15 seconds for the startup services, then continues. The Kestrel, Cloud
Run, Lambda and Cloud Functions hosts wait for the startup services to finish before they take a
request.

Each request that reaches the Hardened middleware has one of three outcomes:

| Request | Outcome |
|---|---|
| Matches a Hardened route | Hardened answers it, before any ASP.NET Core endpoint mapped to the same path |
| Matches no Hardened route | It continues down the ASP.NET Core pipeline, where the middleware and endpoints after `UseHardened` can answer it |
| Matches no Hardened route, and nothing after `UseHardened` answers it | ASP.NET Core answers 404 |

This line, added after `app.UseHardened();`, maps an ASP.NET Core endpoint:

```csharp
app.MapGet("/status", () => "served by ASP.NET Core");
```

The endpoint answers `/status`:

```http
GET /status

HTTP/1.1 200 OK
Content-Type: text/plain; charset=utf-8

served by ASP.NET Core
```

The Hardened route still answers `/todos/1`:

```http
GET /todos/1

HTTP/1.1 200 OK
Content-Type: application/json

{"id":1,"title":"Read the generated code","done":true}
```

The template's `Program.cs` listens on `localhost` only, through `app.Urls.Add`.
`HARDENED_ENVIRONMENT` names the Hardened environment on this host, as on the others.
`ASPNETCORE_ENVIRONMENT` sets only ASP.NET Core's hosting environment.

On this host, ASP.NET Core writes its own hosting diagnostics and request log lines, in the
`Microsoft.AspNetCore.Hosting.Diagnostics` category. The Kestrel and Cloud Run hosts do not write
them.

## AWS Lambda

The template writes this `Program.cs` for AWS Lambda:

```csharp
using Hardened.Aws.Lambda.Runtime.Development;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Todos.Host;

var environment = new EnvironmentImpl(arguments: args);

using var emulator = await LambdaEmulator.StartIfLocal(typeof(Application), apiGateway: true);

var services = new ServiceCollection();

services.AddLogging(logging => logging.AddLambdaLogger());

services.AddHardenedEnvironment(environment);

new Application().PopulateServiceCollection(services);

await HardenedLambdaBootstrap.Run(services.BuildServiceProvider());
```

`[LambdaHttpModule]` registers the HTTP adapter. It also brings the Lambda invocation loop and the
request pipeline. The adapter reads API Gateway payload format 2.0, which an HTTP API and a
function URL send.

`HardenedLambdaBootstrap.Run` runs the startup services. It then takes invocations from the Lambda
Runtime API until the sandbox shuts down. The deployed function has no server and no port.

The template logs through `AddLambdaLogger()` from `Amazon.Lambda.Logging.AspNetCore`. A console
provider can deadlock an invocation that throws. That invocation then hangs until the function
times out.

`LambdaEmulator.StartIfLocal` does nothing when `AWS_LAMBDA_RUNTIME_API` is set. The Lambda service
sets it. Otherwise, it starts the AWS Lambda Test Tool with the tool's API Gateway emulator in front
of the function. It then points the function at the tool.

The API Gateway emulator listens on `PORT`, or 5080. The tool listens on
`HARDENED_LAMBDA_EMULATOR_PORT`, or 5050. The tool's web page invokes the function by hand.

The template pins the tool, `amazon.lambda.testtool` 0.15.1, in
`src/Todos.Host/.config/dotnet-tools.json`. The host project's build runs `dotnet tool restore`.
Stopping the application stops the tool that it started.

## Google Cloud Run

`[CloudRunRuntime]` brings `[KestrelRuntime]`. A Cloud Run application runs on the Kestrel host.
When the project references an adapter package, the application also serves trigger handlers, such
as a `[Queue]` handler, on the same port as the routes.

The template's `Program.cs` for Cloud Run is the Kestrel one, with `CloudRunHost.RunAsync(app)` in
place of `app.RunAsync()` and one more `using` line:

```csharp
using Hardened.Gcp.CloudRun.Runtime.Hosting;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Todos.Host;

var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var configured)
    ? configured
    : 5080;

var environment = new EnvironmentImpl(arguments: args);

var services = new ServiceCollection();

services.AddLogging(logging => logging.AddSimpleConsole(options => options.SingleLine = true));

services.AddHardenedEnvironment(environment);

new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(
    services,
    kestrel => kestrel.ListenAnyIP(port)
);

await app.StartAsync();

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Todos.Host");

logger.LogInformation("Listening on http://localhost:{Port}", port);

await CloudRunHost.RunAsync(app);
```

`CloudRunHost.RunAsync` waits for SIGTERM or SIGINT. It then stops the server and lets requests in
flight finish for up to 10 seconds.

Cloud Run sets `PORT`. Locally, without `PORT`, the application listens on 5080. The template
writes a `Dockerfile` for this host only.

## Google Cloud Functions

`Hardened.Gcp.Functions.Runtime` runs a `[CloudRunRuntime]` application as a Google Cloud
Functions 2nd gen function. The application class does not change. To turn a Cloud Run host
project into a Cloud Functions one, add three packages and delete `Program.cs`.

After the change, the host project references these packages:

```xml
<ItemGroup>
  <PackageReference Include="Hardened.Shared.Runtime" />
  <PackageReference Include="Hardened.Web.Runtime" />
  <PackageReference Include="Hardened.Web.Kestrel.Runtime" />
  <PackageReference Include="Hardened.Gcp.CloudRun.Runtime" />
  <PackageReference Include="Hardened.Gcp.Functions.Runtime" />
  <PackageReference Include="Hardened.Gcp.Functions.SourceGenerator" PrivateAssets="all" />
  <PackageReference Include="Google.Cloud.Functions.Hosting" />
  <PackageReference Include="Hardened.Library.SourceGenerator" />
  <PackageReference Include="Hardened.Web.SourceGenerator" />
</ItemGroup>
```

The template keeps the versions in `Directory.Packages.props`. These lines add the three new
packages to it:

```xml
<PackageVersion Include="Hardened.Gcp.Functions.Runtime" Version="$(HardenedVersion)" />
<PackageVersion Include="Hardened.Gcp.Functions.SourceGenerator" Version="$(HardenedVersion)" />
<PackageVersion Include="Google.Cloud.Functions.Hosting" Version="3.0.1" />
```

Hardened builds against `Google.Cloud.Functions.Hosting` 3.0.1. That package writes the program's
`Main` during the build. Reference it directly from the host project. Without the direct
reference, the build warns with `HRDGF002`. It then fails with `CS5001`, "Program does not contain
a static 'Main' method suitable for an entry point".

`Hardened.Gcp.Functions.SourceGenerator` writes one entry type for the application. Its name is the
application class name with `CloudFunction` appended, as in `Todos.Host.ApplicationCloudFunction`.
The entry type implements `IHttpFunction`. Its `[FunctionsStartup]` attribute names
`HardenedFunctionsStartup<Application>`. That startup class adds the application's services to the
Functions Framework's own service collection. It also runs the startup services and adds routing
before the server takes a request.

`dotnet run --project src/Todos.Host` serves the function on port 8080. `PORT` changes the port. A
deployment names the entry type as its entry point. ASP.NET Core writes its hosting diagnostics and
request log lines on this host, as on the ASP.NET Core host.

## Azure Functions

The template writes this `Program.cs` for Azure Functions:

```csharp
using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.Hosting;
using Todos.Host;

var environment = new EnvironmentImpl(arguments: args);

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(worker => worker.UseHardened<Application>(environment))
    .Build();

host.Run();
```

`UseHardened<TApplication>(environment)` adds the application's services to the worker's own
service collection. It also registers the generated function with the worker.

`[HttpModule]` makes `Hardened.Azure.Functions.SourceGenerator` write one HTTP function, named
`Http`, for every method on every path. The function's authorization level is `Anonymous`. The
Functions host asks for no function key. The routing table then matches the path, as on every other
host. The template's `host.json` sets the HTTP route prefix to an empty string. The Functions host
serves the application's paths without the `/api` prefix.

`func start`, from Azure Functions Core Tools, builds the project and serves it on port 7071. Run it
in `src/Todos.Host`. `dotnet run` does not start this host. The worker exits with "Configuration is
missing the 'HostEndpoint' information". Only the Functions host can start the worker.

The template's `local.settings.json` sets `FUNCTIONS_WORKER_RUNTIME` to `dotnet-isolated` and
`AzureWebJobsStorage` to `UseDevelopmentStorage=true`. The build copies the file to the output. The
publish output never includes it.

## Health endpoints

Every application that imports `[HardenedWebModule]` serves `/health/live` and `/health/ready`. No
route attribute declares them. They answer `GET` and `HEAD`. Another method does not match them.
Both endpoints send `Cache-Control: no-store`. Neither appears in the OpenAPI document.

`/health/live` answers 200 and runs no checks. `/health/ready` runs every registered `IHealthCheck`
at the same time. `[SingletonService]` registers a check. This one uses the template's store,
`ITodoStore`:

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Web.Runtime.Health;

namespace Todos;

[SingletonService]
public class StoreHealthCheck(ITodoStore store) : IHealthCheck
{
    public string Name => "store";

    public async Task<HealthCheckResult> Check(CancellationToken cancellationToken)
    {
        var todo = await store.Find(1);

        return todo is null
            ? HealthCheckResult.Unhealthy("todo 1 is missing")
            : HealthCheckResult.Healthy();
    }
}
```

The worst result decides the status of `/health/ready`:

| Worst result | Status |
|---|---|
| `Healthy` | 200 |
| `Degraded` | 200 |
| `Unhealthy` | 503 |

A check that throws, or that runs past its timeout, counts as `Unhealthy`. With no checks
registered, `/health/ready` answers 200.

On the Kestrel host with this check, `/health/ready` answers 200:

```http
GET /health/ready

HTTP/1.1 200 OK
Cache-Control: no-store
```

`DELETE /todos/1` removes todo 1:

```http
DELETE /todos/1

HTTP/1.1 204 No Content
```

`/health/ready` then answers 503:

```http
GET /health/ready

HTTP/1.1 503 Service Unavailable
Cache-Control: no-store
```

`HealthCheckConfiguration` holds the settings for both endpoints:

| Property | Default | Sets |
|---|---|---|
| `LivePath` | `/health/live` | The liveness path |
| `ReadyPath` | `/health/ready` | The readiness path |
| `CheckTimeout` | 2 seconds | The time one check may take |
| `TotalTimeout` | 5 seconds | The time the whole readiness probe may take |
| `Requirement` | none | A requirement both endpoints add to the application's own |

`/health/ready` cancels the token passed to `Check` when either timeout runs out. A configuration
that the application registers replaces the default one. This registration moves both paths:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Web.Runtime.Health;
using Microsoft.Extensions.DependencyInjection;

namespace Todos.Host;

public partial class Application : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(
            new HealthCheckConfiguration
            {
                LivePath = "/alive",
                ReadyPath = "/ready",
            }
        );
    }
}
```

Neither endpoint carries `[AllowAnonymous]`. Under `[RequireAuthorization]`, both answer 401 to a
caller with no credentials. [Authorization](/guide/authorization) covers both attributes.
`Requirement` can only narrow what the application already requires. A
route that the application declares at either path answers in place of the built-in endpoint.

## Limits

A Lambda invocation in API Gateway payload format 1.0 fails. A REST API and an Application Load
Balancer send that format. The `hardened-web` template has no `--host` value for Cloud Functions.

## Next

- [From scratch](/guide/from-scratch): an application assembled by hand on Kestrel
- [Test hosts](/guide/testing-hosts): running the tests on each host
- [AWS Lambda](/aws/lambda-web), [Google Cloud](/gcp/web) and [Azure Functions](/azure/web):
  deploying to each cloud
