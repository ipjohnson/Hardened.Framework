# From scratch

This page builds a Hardened application by hand: a module class, one handler and a `Program.cs`
that starts Kestrel. `dotnet new hardened-web` writes a larger application with a client and tests.
[Getting started](/guide/getting-started) starts from that template instead.

The application that this page builds answers this request:

```http
GET /hello/world

HTTP/1.1 200 OK
Content-Type: application/json

"Hello, world!"
```

## Create the project

The application is a console project:

```bash
dotnet new console -n Greetings
cd Greetings
```

The project uses `Microsoft.NET.Sdk`. `Hardened.Web.Kestrel.Runtime` brings in the ASP.NET Core
shared framework with a framework reference, so the project does not need the Web SDK.

The Hardened packages target `net8.0`. This application builds and runs targeting `net8.0` or
`net10.0`.

## Add the packages

The project references three runtime packages and three source generator packages:

```xml
<ItemGroup>
  <PackageReference Include="Hardened.Shared.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Web.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Web.Kestrel.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Library.SourceGenerator" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Web.SourceGenerator" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Validation.SourceGenerator" Version="0.0.0-HARDENED-VERSION" />
</ItemGroup>
```

| Package | Contents |
|---|---|
| `Hardened.Shared.Runtime` | `[HardenedModule]`, configuration and the environment |
| `Hardened.Web.Runtime` | Routing, the route attributes such as `[Get]`, and the HTTP pipeline |
| `Hardened.Web.Kestrel.Runtime` | `[KestrelRuntime]` and `HardenedKestrelApplication` |
| `Hardened.Library.SourceGenerator` | Generates each module's half: `PopulateServiceCollection`, the module's attribute, service registration and configuration |
| `Hardened.Web.SourceGenerator` | Generates the routing table and a handler class for each route |
| `Hardened.Validation.SourceGenerator` | Generates validators from constraint attributes |

`Hardened.Validation.SourceGenerator` turns constraint attributes, such as `[Range]`, into checks
that run before the handler. [Validation](/guide/validation) covers these checks.

Every Hardened package is released at one version. Reference the same version of each. Every
version is a prerelease version. `dotnet add package` without `--prerelease` fails with "There are
no stable versions available".

The generator packages are development dependencies. `dotnet add package` writes them with
`PrivateAssets` set to `all`.

`Hardened.Web.Kestrel.Runtime` depends on `Hardened.Web.Runtime` and `Hardened.Shared.Runtime`.
The runtime packages do not depend on the generator packages. The project references each generator
package itself.

A project with route attributes that references `Hardened.Library.SourceGenerator` and not
`Hardened.Web.SourceGenerator` fails to build. `Hardened.Library.SourceGenerator` reports the error
`HRDR006`:

```text
error HRDR006: 'Greetings.GreetingController' declares routes and nothing in this project turns them into a routing table, so every one of them answers 404 at run time. Reference Hardened.Web.SourceGenerator as an analyzer, or drop the route attributes if this assembly is not meant to serve them.
```

Without `Hardened.Library.SourceGenerator`, the `Application` class has no
`PopulateServiceCollection`. `Program.cs` then fails to compile with `CS1061`.

## Add the application module

A `partial` class marked `[HardenedModule]` is a module. The application is a module. The module
class goes in its own file, here `Application.cs`:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;

namespace Greetings;

[HardenedModule]
[KestrelRuntime]
public partial class Application;
```

`[HardenedModule]` is in the `Hardened.Shared.Runtime.Attributes` namespace. `[KestrelRuntime]` is
in the `Hardened.Web.Kestrel.Runtime` namespace.

The generator writes the other half of the class, including `PopulateServiceCollection`. It also
writes an attribute class named after the module, here `ApplicationAttribute`. Without `partial`,
the build fails with `CS0260`.

`[KestrelRuntime]` is the attribute of the `KestrelRuntime` module in the
`Hardened.Web.Kestrel.Runtime` package. Applying it to the application imports that module. The
module serves the application on Kestrel. [Modules](/guide/modules) covers importing other modules.

## Add a handler

A method with a route attribute is a handler. Its class needs no base type, no interface and no
registration. The handler class goes in its own file, here `GreetingController.cs`:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Greetings;

public class GreetingController
{
    [Get("/hello/{name}")]
    public string Hello(string name) => $"Hello, {name}!";
}
```

`[Get]` is in the `Hardened.Web.Runtime.Attributes` namespace. The parameter `name` matches the
`{name}` path token, so `name` binds from the path. The return value is written as JSON. A `string`
is written as a JSON string, with `Content-Type: application/json`.

[Routing](/guide/routing) covers the route attributes. [Parameter binding](/guide/parameter-binding)
covers the other sources a parameter binds from.

## Write `Program.cs`

`Program.cs` fills a `ServiceCollection` from the application and starts Kestrel. The `Program.cs`
below replaces the one `dotnet new console` wrote:

```csharp
using Greetings;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();

services.AddLogging(logging => logging.AddSimpleConsole());
services.AddHardenedEnvironment(args);

new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(
    services,
    kestrel => kestrel.ListenAnyIP(5080)
);

await app.RunAsync();
```

`AddLogging` is required. Without it, `HardenedKestrelApplication.Create` throws an
`InvalidOperationException` that names `ILogger<RequestLogger>`. An application that calls
`AddLogging` with no provider runs and writes no log. `AddSimpleConsole` writes the log to the
console, including lines for each request.

`AddHardenedEnvironment(args)` registers the environment. Without it, `RunAsync` throws an
`InvalidOperationException` that names `IHardenedEnvironment`. The environment's name comes from
the environment variable `HARDENED_ENVIRONMENT`. The name is `development` when
`HARDENED_ENVIRONMENT` is unset.
[Environments](/guide/environments) covers the environment.

`PopulateServiceCollection` registers the application module, the modules it imports and the
routing table.

`HardenedKestrelApplication.Create` builds the service provider from the collection. Its second
argument configures Kestrel's `KestrelServerOptions`. Here, Kestrel listens on port 5080 on every
address.

`RunAsync` starts the server and waits. Ctrl+C stops the server. The process then exits with
code 0.

[Hosts](/guide/hosts) covers the other options of the Kestrel host.

## Run the application

`dotnet run` builds and starts the application:

```bash
dotnet run
```

From a second terminal, `curl` gets the JSON string:

```console
$ curl localhost:5080/hello/world
"Hello, world!"
```

## Find the generated code

Set `EmitCompilerGeneratedFiles` to `true` in the project file:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

Each build then writes the generated C# to `obj/<configuration>/<tfm>/generated/`, with one
directory for each generator. The files are in a second directory named after the generator class,
for example
`obj/Debug/net8.0/generated/Hardened.Web.SourceGenerator/Hardened.Web.SourceGenerator.WebLibrarySourceGenerator/`.

The build writes these files for this application:

| Generator | File | Contents |
|---|---|---|
| `Hardened.DependencyModules.SourceGenerator` | `Application.Module.g.cs` | `PopulateServiceCollection` and `ApplicationAttribute` |
| `Hardened.Library.SourceGenerator` | `Application.ServiceProvider.cs` | `CreateServiceProvider` |
| `Hardened.Library.SourceGenerator` | `Application.Configuration.cs` | The configuration provider |
| `Hardened.Web.SourceGenerator` | `Application.Routing.cs` | The routing table, and the registrations for it and the controller |
| `Hardened.Web.SourceGenerator` | `GreetingController_Hello_541.cs` | The handler for the route: binding `name` and calling `Hello` |
| `Hardened.Web.SourceGenerator` | `Application.Links.cs` | A typed link for each route |

`Hardened.DependencyModules.SourceGenerator` is one of the two generators in the
`Hardened.Library.SourceGenerator` package. The name of the handler file ends in a number that the
generator chooses.

## Other hosts

`[KestrelRuntime]` is one of five host attributes. Another host needs a different host attribute,
host package and `Program.cs`. The handler does not change. [Hosts](/guide/hosts) covers each host.

## Limits

This application serves no OpenAPI document. A request to `/openapi.json` or `/docs` gets a 404.
[The OpenAPI document](/guide/openapi-document) covers serving the document and the reference page.

## Next

| Page | Covers |
|---|---|
| [Getting started](/guide/getting-started) | Starting from the `hardened-web` template |
| [Hosts](/guide/hosts) | The other hosts and their `Program.cs` |
| [Modules](/guide/modules) | Modules and importing them |
| [Routing](/guide/routing) | Route attributes and path tokens |
| [Writing a test](/guide/testing) | Tests that send requests through the application |
