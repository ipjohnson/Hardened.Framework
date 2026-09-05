# Getting started

One command writes an API that builds, serves and tests. This page runs it, then assembles the
same application by hand.

## The short way

```bash
dotnet new install Hardened.Templates
dotnet new hardened-web -n Todos
cd Todos
dotnet run --project src/Todos.Host
```

```console
$ curl localhost:5080/todos/1
{"id":1,"title":"Read the generated code","done":true}
```

That is a working API with a reference page at `/docs`, a generated client, and tests that drive
the client through the application's own pipeline:

```csharp
[HardenedTest]
public async Task CreateTodo_AnswersCreatedWithALocation(TodosClient client) {
    var created = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" })
        .Returns<Created<ClientModels.Todo>>();

    Assert.Equal("ship it", created.Value.Title);
    Assert.Equal($"/todos/{created.Value.Id}", created.Location);
}
```

`dotnet test` runs them. [Project templates](/guide/project-templates) covers the options: the
host, the contract, the response model and the client.

::: warning Everything is prerelease
`dotnet add package` needs `--prerelease` or it finds nothing. The templates pin explicit versions
and are unaffected.
:::

## What the build wrote

Every project the template writes has `EmitCompilerGeneratedFiles` on. After a build,
`src/Todos/obj/Debug/net8.0/generated/` holds one directory per generator: the routing table, a
handler per route, the parameter binding and the module registration, as ordinary C#.

## The same application by hand

### Packages

Two kinds of package reference, the runtime and the source generators:

```xml
<ItemGroup>
    <PackageReference Include="Hardened.Shared.Runtime" Version="0.20.0-rc1000" />
    <PackageReference Include="Hardened.Web.Runtime" Version="0.20.0-rc1000" />
    <PackageReference Include="Hardened.Web.Kestrel.Runtime" Version="0.20.0-rc1000" />

    <PackageReference Include="Hardened.Library.SourceGenerator" Version="0.20.0-rc1000" />
    <PackageReference Include="Hardened.Web.SourceGenerator" Version="0.20.0-rc1000" />
</ItemGroup>
```

::: danger The generators do not arrive with the runtime packages
Analyzers do not flow through a package reference. Reference only the runtime packages and the
project still compiles, into an application whose `Application` class has no
`PopulateServiceCollection`, or one that builds cleanly and answers 404 to every route. Nothing
says what is missing. The templates reference the right generators.
:::

To read what the generators emit, turn on `EmitCompilerGeneratedFiles`:

```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

### The application

An application is a `partial class` marked `[HardenedModule]`, plus the runtime module that says
where it runs:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;

namespace Todos;

[HardenedModule]
[KestrelRuntime]
public partial class Application;
```

`partial` is required. The generator writes the other half of the class, including
`PopulateServiceCollection`.

### A handler

A plain class. No base type, no interface, no registration:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class GreetingController {
    [Get("/hello/{name}")]
    public string Hello(string name) => $"Hello, {name}!";
}
```

### Program.cs

```csharp
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Todos;

var services = new ServiceCollection();

services.AddLogging(logging => logging.AddSimpleConsole(options => options.SingleLine = true));
services.AddHardenedEnvironment(args);

new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(
    services, kestrel => kestrel.ListenAnyIP(5080));

await app.RunAsync();
```

Two of those lines matter more than they look. `AddLogging` needs a provider, or the application
serves in silence. `AddHardenedEnvironment` registers the environment under both interfaces the
framework reads; see [Registering one](/guide/environments#registering-one).

```console
$ dotnet run
$ curl localhost:5080/hello/world
"Hello, world!"
```

## Hosting it somewhere else

The runtime attribute is the only thing that changes:

| Attribute | Package | Runs |
|---|---|---|
| `[KestrelRuntime]` | `Hardened.Web.Kestrel.Runtime` | Kestrel, without the ASP.NET Core request pipeline |
| `[AspNetCoreRuntime]` | `Hardened.Web.AspNetCore.Runtime` | Inside ASP.NET Core's pipeline, behind `app.UseHardened()` |
| `[LambdaWebModule]` | `Hardened.Amz.Web.Lambda.Runtime` | Behind API Gateway. See [AWS](/aws/) |

Handlers, filters, binding and the generated routing table do not change with the host. The
templates put the implementation in one project and the host in another, and point the tests at
the implementation.

## Next

- [Project templates](/guide/project-templates): the options, and what each one writes
- [Modules](/guide/modules): how `[HardenedModule]` composes
- [Routing](/guide/routing): the route attributes and their status codes
- [Writing a test](/guide/testing): booting this application inside a test
- [AWS](/aws/): the same handlers on Lambda
