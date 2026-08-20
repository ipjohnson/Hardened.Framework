# Getting started

Hardened ships as NuGet packages on nuget.org, split across two repositories. A web API needs the
[core framework](https://github.com/ipjohnson/Hardened.Framework); a Lambda function adds
[Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz). No private feed and no token.

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

The rest of this page assembles the same thing by hand, which is worth doing once to see what the
template is actually setting up.

::: warning Everything is prerelease
There is no stable release yet, so `dotnet add package` needs `--prerelease` or it finds nothing.
The templates pin explicit versions, so they are unaffected.
:::

## A web project by hand

Two kinds of package reference, and the second kind is the one people miss.

```xml
<ItemGroup>
    <!-- Runtime -->
    <PackageReference Include="Hardened.Shared.Runtime" Version="0.11.0-rc1000" />
    <PackageReference Include="Hardened.Web.Runtime" Version="0.11.0-rc1000" />
    <PackageReference Include="Hardened.Web.Kestrel.Runtime" Version="0.11.0-rc1000" />

    <!-- Source generators. Not optional. -->
    <PackageReference Include="Hardened.Library.SourceGenerator" Version="0.11.0-rc1000" />
    <PackageReference Include="Hardened.Web.SourceGenerator" Version="0.11.0-rc1000" />
</ItemGroup>
```

::: danger The generators do not arrive with the runtime packages
Analyzers do not flow through a package reference. Reference only the runtime packages and the
project still compiles — into an application whose `Application` class has no
`PopulateServiceCollection`, or one that builds cleanly and answers **404 to every route**, because
nothing generated a routing table.

Nothing errors, and nothing says what is missing. This is the most common way to get a Hardened
project wrong, and it is why [the templates](/guide/project-templates) exist.
:::

To read what the generators emit — the fastest way to understand any of this — turn on
`EmitCompilerGeneratedFiles`:

```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

Generated files then appear under `obj/<configuration>/<tfm>/generated/`, one directory per
generator. They are ordinary C#: the routing table, the request handlers, the parameter binding and
the module registration are all there to be read.

## The smallest application

An application is a `partial class` marked `[HardenedModule]`, plus whichever runtime module says
where it runs. `[KestrelRuntime]` serves HTTP without the ASP.NET Core request pipeline, and is the
one to reach for first:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;

namespace Greeter;

[HardenedModule]
[KestrelRuntime]
public partial class Application;
```

`partial` is not optional. The generator writes the other half of the class — including
`PopulateServiceCollection`, which `Program.cs` calls.

A handler is a plain class. No base type, no interface, no registration:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Greeter;

public class GreetingController {
    [Get("/hello/{name}")]
    public string Hello(string name) => $"Hello, {name}!";
}
```

And `Program.cs` builds the service collection, hands it to the module, and starts Kestrel:

```csharp
using Greeter;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();

// A provider, not just AddLogging(). Without one the application starts and serves in complete
// silence - no request log, no startup warning - which is a confusing first run.
services.AddLogging(logging => logging.AddSimpleConsole(options => options.SingleLine = true));

services.AddHardenedEnvironment(args);

new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(
    services, kestrel => kestrel.ListenAnyIP(5080));

await app.RunAsync();
```

`AddHardenedEnvironment` is called by the application rather than by the framework, because only the
application knows where its environment name and arguments come from. It registers the environment
under both `IHardenedEnvironment` and `IModuleEnvironment` — the first is what your code reads, the
second is what decides which services get registered at all. Registering only the first leaves
`[IfEnvironment]` answering against a different variable than everything else in the application.
See [Environments](/guide/environments).

`dotnet run`, then:

```console
$ curl localhost:5080/hello/world
"Hello, world!"
```

## Hosting it somewhere else

The runtime attribute is the only thing that changes. `[AspNetCoreRuntime]` from
`Hardened.Web.AspNetCore.Runtime` puts the same handlers behind ASP.NET Core's pipeline when you
need its middleware or authentication; `[LambdaWebModule]` from Hardened.Amz puts them behind API
Gateway.

Handlers, filters, binding and the generated routing table do not change with the host. That is why
the templates put the implementation in one project and the host in another, and point the tests at
the implementation.

## Where the route came from

Nothing scanned for `GreetingController`. During the build the web generator found the `[Get]`
attribute, emitted a handler bound to that method's exact signature, and added the route to a
generated routing table. Under `obj/` you will find something close to what you would have written
by hand — which is the point: there is no container graph to reason about and no startup cost to
measure.

## Next

- [Project templates](/guide/project-templates) — the options, and what each one generates
- [Modules](/guide/modules) — how `[HardenedModule]` composes, and the hooks it gives you
- [Routing](/guide/routing) — the route attributes and their status codes
- [Testing](/guide/testing) — booting this application inside a test
- [AWS](/aws/) — running the same handlers on Lambda
