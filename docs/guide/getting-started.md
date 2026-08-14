# Installation

Hardened ships as a set of NuGet packages split across two repositories. A web API needs the
[core framework](https://github.com/ipjohnson/Hardened.Framework); a Lambda function adds
[Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz).

## The package feed

Packages are published to GitHub Packages rather than nuget.org, which means a feed and a
credential. Add a `nuget.config` beside the solution:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
    <packageSources>
        <clear />
        <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
        <add key="github-ipjohnson" value="https://nuget.pkg.github.com/ipjohnson/index.json" />
    </packageSources>
    <packageSourceCredentials>
        <github-ipjohnson>
            <add key="Username" value="ipjohnson" />
            <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
        </github-ipjohnson>
    </packageSourceCredentials>
</configuration>
```

`%GITHUB_TOKEN%` is expanded from the environment, so the token never lands in a file you might
commit. It needs the `read:packages` scope and nothing else.

::: warning GitHub Packages always wants a token
The feed rejects anonymous requests even for public packages. A restore that fails with a 401 on
the `github-ipjohnson` source almost always means `GITHUB_TOKEN` is unset in the shell that ran it —
including the shell your IDE inherited when you launched it from a desktop icon rather than a
terminal.
:::

## A web project

Three package references and a generator:

```xml
<ItemGroup>
    <PackageReference Include="Hardened.Shared.Runtime" Version="..." />
    <PackageReference Include="Hardened.Web.Runtime" Version="..." />
    <PackageReference Include="Hardened.Web.AspNetCore.Runtime" Version="..." />
</ItemGroup>
```

The source generators arrive with those packages. To read what they emit — which is the fastest way
to understand any of this — turn on `EmitCompilerGeneratedFiles`:

```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

Generated files then appear under `obj/<configuration>/<tfm>/generated/`, one directory per
generator. They are ordinary C#: the routing table, the request handlers, the parameter binding and
the module registration are all there to be read.

## The smallest application

An application is a `partial class` marked `[HardenedModule]`, plus whichever runtime module
describes where it runs. For ASP.NET Core that is `[AspNetCoreRuntime]`:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.AspNetCore.Runtime;

namespace Greeter;

[HardenedModule]
[AspNetCoreRuntime]
public partial class Application { }
```

`partial` is not optional. The generator adds the other half of the class — including
`PopulateServiceCollection`, which is what `Program.cs` calls.

A handler is a plain class. No base type, no interface, no registration:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Greeter;

public class GreetingController {
    [Get("/hello/{name}")]
    public string Hello(string name) => $"Hello, {name}!";
}
```

And `Program.cs` builds an ordinary ASP.NET Core host, hands the module the service collection, and
inserts the Hardened middleware:

```csharp
using Greeter;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.AspNetCore.Runtime;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));

new Application().PopulateServiceCollection(builder.Services);

var app = builder.Build();

app.UseHardened();

app.Run();
```

`IHardenedEnvironment` is registered by the application rather than by the framework, because only
the application knows where its environment name and arguments come from. Leave it out and startup
fails on the first service that asks for one — see [Environments](/guide/environments).

`dotnet run`, then:

```
$ curl localhost:5000/hello/world
"Hello, world!"
```

## Where the route came from

Nothing scanned for `GreetingController`. During the build, the web generator found the `[Get]`
attribute, emitted a handler bound to that method's exact signature, and added the route to a
generated routing table. Under `obj/` you will find something close to what you would have written
yourself — which is the point: there is no container graph to reason about and no startup cost to
measure.

## Next

- [Modules](/guide/modules) — how `[HardenedModule]` composes, and the hooks it gives you
- [Routing](/guide/routing) — the route attributes and their status codes
- [Testing](/guide/testing) — booting this application inside a test
- [AWS](/aws/) — running the same handlers on Lambda
