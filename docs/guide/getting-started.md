# Getting started

Hardened ships as NuGet packages on nuget.org, split across two repositories. A web API needs the
[core framework](https://github.com/ipjohnson/Hardened.Framework); a Lambda function adds
[Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz).

## The short way

```bash
dotnet new install Hardened.Templates
dotnet new hardened-web -n Greeter
cd Greeter
dotnet run --project src/Greeter.Host
```

```console
$ curl localhost:5080/greeting/world
{"message":"Hello, world!"}
```

That is a working API with tests, a reference page at `/docs`, and every package version pinned in
one place. [Project templates](/guide/project-templates) covers the options — the host, the
contract, Lambda, libraries.

The rest of this page assembles the same thing by hand.

::: warning Everything is prerelease
`dotnet add package` needs `--prerelease` or it finds nothing. The templates pin explicit versions
and are unaffected.
:::

## A web project by hand

Two kinds of package reference:

```xml
<ItemGroup>
    <!-- Runtime -->
    <PackageReference Include="Hardened.Shared.Runtime" Version="0.15.0-rc1000" />
    <PackageReference Include="Hardened.Web.Runtime" Version="0.15.0-rc1000" />
    <PackageReference Include="Hardened.Web.Kestrel.Runtime" Version="0.15.0-rc1000" />

    <!-- Source generators. Not optional. -->
    <PackageReference Include="Hardened.Library.SourceGenerator" Version="0.15.0-rc1000" />
    <PackageReference Include="Hardened.Web.SourceGenerator" Version="0.15.0-rc1000" />
</ItemGroup>
```

::: danger The generators do not arrive with the runtime packages
Analyzers do not flow through a package reference. Reference only the runtime packages and the
project still compiles — into an application whose `Application` class has no
`PopulateServiceCollection`, or one that builds cleanly and answers **404 to every route**.

Nothing errors, and nothing says what is missing. [The templates](/guide/project-templates)
reference the right generators for you.
:::

To read what the generators emit, turn on `EmitCompilerGeneratedFiles`:

```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

Generated files appear under `obj/<configuration>/<tfm>/generated/`, one directory per generator:
the routing table, the request handlers, the parameter binding and the module registration, all as
ordinary C#.

## The smallest application

An application is a `partial class` marked `[HardenedModule]`, plus the runtime module that says
where it runs. `[KestrelRuntime]` serves HTTP without the ASP.NET Core request pipeline:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;

namespace Greeter;

[HardenedModule]
[KestrelRuntime]
public partial class Application;
```

`partial` is not optional — the generator writes the other half of the class, including
`PopulateServiceCollection`.

A handler is a plain class. No base type, no interface, no registration:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Greeter;

public class GreetingController {
    [Get("/hello/{name}")]
    public string Hello(string name) => $"Hello, {name}!";
}
```

`Program.cs` builds the service collection, hands it to the module, and starts Kestrel:

```csharp
using Greeter;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();

// A provider, not just AddLogging(). Without one the application serves in complete silence.
services.AddLogging(logging => logging.AddSimpleConsole(options => options.SingleLine = true));

services.AddHardenedEnvironment(args);

new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(
    services, kestrel => kestrel.ListenAnyIP(5080));

await app.RunAsync();
```

`AddHardenedEnvironment` registers the environment under both `IHardenedEnvironment` and
`IModuleEnvironment`. Registering only the first leaves `[IfEnvironment]` answering against a
different variable than the rest of the application — see [Environments](/guide/environments).

`dotnet run`, then:

```console
$ curl localhost:5080/hello/world
"Hello, world!"
```

## Hosting it somewhere else

The runtime attribute is the only thing that changes. `[AspNetCoreRuntime]` from
`Hardened.Web.AspNetCore.Runtime` puts the same handlers behind ASP.NET Core's pipeline;
`[LambdaWebModule]` from Hardened.Amz puts them behind API Gateway.

Handlers, filters, binding and the generated routing table do not change with the host, which is why
the templates put the implementation in one project, the host in another, and point the tests at the
implementation.

## Next

- [Project templates](/guide/project-templates) — the options, and what each one generates
- [Modules](/guide/modules) — how `[HardenedModule]` composes, and the hooks it gives you
- [Routing](/guide/routing) — the route attributes and their status codes
- [Testing](/guide/testing) — booting this application inside a test
- [AWS](/aws/) — running the same handlers on Lambda
