# Web services

`[CloudRunRuntime]` runs the routes you already wrote as a Cloud Run service. The controllers,
filters and binding are the same as [any web application](/guide/routing).

```csharp
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;

[HardenedModule]
[CloudRunRuntime]
public partial class Application;

public class ProductController {
    [Get("/api/products/{id}")]
    public Product GetProduct(string id) => _repository.Find(id);
}
```

`dotnet new hardened-web --host cloud-run` writes this shape with tests and a Dockerfile.

## The host

`[CloudRunRuntime]` composes `[KestrelRuntime]`. A Cloud Run service is a container listening on a
port, and Kestrel is what listens; what the Cloud Run module adds is the front door that recognises
the envelopes the [trigger](/guide/triggers) adapters serve, the one dispatch a service serving
both web routes and triggers on one socket needs, and the revision on the request's transport info.

It is named on the application, the way the Kestrel and ASP.NET Core hosts are. A service whose
handlers are all in one project would still need it, because the host is a fact about the
deployment and not something a handler's attribute can imply.

## The entry point

`Program.cs` is written, not generated, and every line of it is doing something:

```csharp
using Hardened.Gcp.CloudRun.Runtime.Hosting;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Todos.Host;

var services = new ServiceCollection();

services.AddLogging(logging => logging.AddSimpleConsole(options => options.SingleLine = true));
services.AddHardenedEnvironment(new EnvironmentImpl(arguments: args));

new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(services, CloudRunHost.Listen);

await app.StartAsync();

Console.WriteLine($"Listening on {string.Join(", ", app.Addresses)}");

await CloudRunHost.RunAsync(app);
```

`CloudRunHost.Listen` binds every interface on the port `PORT` names, 8080 when it is unset, which
is the port Cloud Run sends to. It is `KestrelListen.FromEnvironment` with Cloud Run's default
filled in, so a container started by hand without the variable comes up on 8080 rather than
refusing to start.

`CloudRunHost.RunAsync` waits for `SIGTERM`, which Cloud Run sends when it retires an instance, or
`SIGINT` from a terminal. It then stops accepting requests and gives what is in flight ten seconds
to finish, which is the grace Cloud Run gives before `SIGKILL`. The plain `app.RunAsync()` returns
on `ProcessExit`, and the process was seen to exit with a response half written; this overload is
what drains.

## The container

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Todos.Host/Todos.Host.csproj --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Todos.Host.dll"]
```

Nothing in it is specific to the framework. Cloud Run sets `PORT`, the entry point reads it, and
`aspnet:8.0` is the image the container tier of the tests runs every fixture in.

```bash
gcloud run deploy todos --source . --region us-central1 --allow-unauthenticated
```

`--source` builds the Dockerfile with Cloud Build and deploys the image. There is no
infrastructure package in this line; the sources a service serves are wired to it with the
`gcloud` command on each family's page.

## Routes and triggers together

A service can serve `[Get]` routes and `[Queue]` handlers at once, on one port. The front door sees
every request first: one it recognises as a push, a CloudEvent or a Scheduler job is unwrapped into
its trigger route, and every other request is routed as the web request it is. An unrecognised
path is a 404 from the web router, and a push posted to a service whose handlers declare no queue
is answered 500 so Pub/Sub retries and reports it.

## Configuration

Response headers are a [configuration model](/guide/configuration) the web runtime defines, and an
application amends it exactly as it does under Kestrel; nothing about that changes with the host.

## Running it locally

```bash
dotnet run --project src/Todos.Host
```

```
Listening on http://[::]:8080
```

```bash
curl localhost:8080/todos
```

`PORT` moves it. There is no emulator to start: the service is the process, and a trigger is a
request that can be posted to it by hand or from the test project's container tier.

## Testing

Routes are ordinary Hardened routes, so [`ITestWebApp`](/guide/testing-web) drives them without
any Cloud Run involvement:

```csharp
[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]
```

A class carrying `[KestrelRuntime]` under `[assembly: KestrelTesting]` runs the same tests on a
socket, which is the host the container runs; see [Test hosts](/guide/testing-hosts). Nothing
about a web route needs `[CloudRunTesting]`, which changes only how a trigger is delivered.

## Next

- [Triggers](/guide/triggers): the sources other than HTTP
- [Testing Cloud Run handlers](/gcp/testing): the four fidelity levels
- [Routing](/guide/routing): the routes themselves, unchanged by the host
