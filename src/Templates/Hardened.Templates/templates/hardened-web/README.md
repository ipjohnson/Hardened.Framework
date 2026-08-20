# Hardened1

A web application on [Hardened](https://github.com/ipjohnson/Hardened.Framework) — a compile-time,
source-generated .NET framework. Routing, request handlers, parameter binding and dependency
injection are written during the build rather than resolved by reflection at run time.

## Run it

```bash
dotnet build
dotnet test
dotnet run --project src/Hardened1.Host
```

It prints its address and listens on **5080**. Set `PORT` to change it.

```bash
curl localhost:5080/greeting/world
{"message":"Hello, world!"}
```

#if (OpenApiUi)
There is a reference page at <http://localhost:5080/docs> and the document behind it at
`/openapi.json`. The page is served in the `development` environment only.

Visual Studio and Rider open it on F5 — pick the **Hardened1.Host (+Browser)** configuration, which
comes from `src/Hardened1.Host/Properties/launchSettings.json`. That profile names no environment
variables on purpose: `dotnet run` applies a profile's variables over the ones already set, so a
`PORT` pinned there would override the caller's. The `dotnet` CLI also ignores `launchBrowser`, so
from a terminal browse to the page yourself.
#endif

## The three projects

| | |
|---|---|
| `src/Hardened1` | Everything the application does — routes, services, models. Knows nothing about where it runs. |
| `src/Hardened1.Host` | Which runtime hosts it, and `Program.cs`. The only host-specific project. |
| `tests/Hardened1.Tests` | Tests, against the library rather than the host. |

That split is the point rather than a convention. Swapping the host — Kestrel, ASP.NET Core, and
Lambda in the Hardened.Amz packages — changes only the middle project. The other two are identical
whichever one you pick, which is why the tests target the library: a test suite that named the host
would be tied to a deployment target for no reason.

## Adding to it

#if (codeFirst)
A route is an attribute on a method of a plain class — no base type, no interface, no registration.
`src/Hardened1/GreetingController.cs` is the whole pattern:

```csharp
[Get("/{name}")]
public Greeting Hello(string name) => new($"Hello, {name}!");
```

`[BasePath]` on `Hardened1Library` prefixes every route in the assembly, so that one is served at
`/greeting/{name}`. `[Get]`, `[Post]`, `[Put]`, `[Delete]` and `[Patch]` all behave the same way.

A service is registered next to the class it belongs to, with `[SingletonService]`,
`[ScopedService]` or `[TransientService]` — the module lists nothing, so it cannot fall out of step.
#endif
#if (specFirst)
#if (openapi)
The contract is `src/Hardened1/contracts/greeting.yaml`. Add an operation there and the build writes
the model, the route and the validation its constraints describe, then stops compiling until
`GreetingService` implements the new method.
#endif
#if (smithy)
The contract is `src/Hardened1/contracts/greeting.smithy`. Add an operation there and the build
writes the model, the route and the validation its constraints describe, then stops compiling until
`GreetingService` implements the new method.

Building needs the [Smithy CLI](https://smithy.io/2.0/guides/smithy-cli/index.html) on `PATH`. The
build names the version it expects if yours differs.
#endif

That is the trade a contract-first project makes: the specification and the code cannot disagree,
because disagreeing is a build error. There are no route attributes anywhere in this project.
#endif

## Testing

`[HardenedTest]` boots the real application — the module graph, configuration and startup services —
and injects what the test asks for. `ITestWebApp` drives the real pipeline without a socket or a
port, so a test exercises routing, filters, binding and serialisation rather than calling a method:

```csharp
[HardenedTest]
public async Task GreetsByName(ITestWebApp app) {
    var response = await app.Get("/greeting/world");

    response.Assert.Ok();
}
```

Mark a parameter `[Mock]` and that service is substituted for the whole graph, including behind a
route.

## Reading the generated code

The fastest way to understand any of this is to read what the build wrote. It is ordinary C#, and
`EmitCompilerGeneratedFiles` is already on:

```
src/Hardened1/obj/Debug/net8.0/generated/
```

One directory per generator: the routing table, the handler for each route, the parameter binding
and the module registration are all there.

## Where to go next

- [Documentation](https://ipjohnson.github.io/Hardened.Docs)
- `AGENTS.md` in this directory — the invariants and gotchas, for anyone or anything editing the
  code rather than reading it
