# Test hosts

The attribute an application names its host with runs a test on that host. `[KestrelRuntime]` on
a test class runs each test in it on Kestrel, on a loopback port the kernel picks.
`[AspNetCoreRuntime]` runs it inside a real `WebApplication`, built the way `Program.cs` builds
one.

```csharp
using Hardened.Web.Kestrel.Runtime;

[KestrelRuntime]
public class TodoSocketTests {

    [HardenedTest]
    public async Task ListTodos_OverTheSocket(ITestWebApp app) {
        var response = await app.Get("/todos");

        response.Assert.Ok();
        Assert.True(response.Headers.ContainsKey("Date"), "a header only a server writes");
    }

    [HardenedTest]
    public async Task CreateTodo_OverTheSocket(TodosClient client, [Mock] ITodoStore store) {
        store.Add("ship it").Returns(new Todo(7, "ship it", false));

        var created = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" })
            .Returns<Created<ClientModels.Todo>>();

        Assert.Equal("/todos/7", created.Location);
    }
}
```

Everything the test holds is unchanged. `ITestWebApp` sends to the socket, and so does every typed
client. `Returns<T>()` reads the same answer. `LastResponse` is what came back over the wire. The
`[Mock]` is the same substitute, because the server resolves its handlers from the test's
container.

## Turning it on

One package per host, and one assembly attribute naming it:

```csharp
// Bootstrap.cs
using Hardened.Web.Kestrel.Testing;
using Hardened.Web.AspNetCore.Testing;

[assembly: KestrelTesting]       // Hardened.Web.Kestrel.Testing
[assembly: AspNetCoreTesting]    // Hardened.Web.AspNetCore.Testing
```

Each host is its own package because hosting brings the ASP.NET Core shared framework with it,
and a suite that runs on the pipeline should not carry that. A runtime attribute no package in
scope answers for leaves the test on the pipeline.

## Choosing per test

The runtime attribute is valid on a method, a class or the assembly, and the narrowest wins.
`[PipelineHost]` opts a test back to the pipeline:

```csharp
[KestrelRuntime]
public class SocketTests {

    [HardenedTest]
    public async Task OverTheSocket(ITestWebApp app) { /* Kestrel */ }

    [HardenedTest]
    [PipelineHost]
    public async Task InProcess(ITestWebApp app) { /* the pipeline */ }
}
```

Every test on a host binds and stops a server of its own. Put the attribute on the class that
needs it rather than on the assembly.

## What changes on a socket

| | Pipeline | `[KestrelRuntime]` | `[AspNetCoreRuntime]` |
|---|---|---|---|
| `TestWebResponse.Headers` | what the pipeline wrote | what Kestrel sent | what Kestrel sent |
| `TestWebResponse.Failure` | the handler's exception | null | null |
| An unmatched path | 404 | 404 | whatever `Program.cs` put behind `UseHardened()`, then ASP.NET Core's 404 |
| Cost per test | none | one Kestrel bind | one `WebApplication` build and bind |

Credentials are two headers, so they travel. See [Credentials](/guide/testing-credentials).

## The ASP.NET Core composition

`[AspNetCoreTesting]` builds the `WebApplication` with `app.UseHardened()` and nothing else. A
`Program.cs` that adds middleware around it is mirrored by an `IAspNetCoreTestComposition`, which
replaces the default and so calls `UseHardened()` itself:

```csharp
public sealed class ProductionComposition : IAspNetCoreTestComposition {

    public void Configure(WebApplicationBuilder builder) {
        builder.Services.AddHttpLogging(options => { });
    }

    public void Configure(WebApplication app) {
        app.UseHttpLogging();
        app.UseHardened();
    }
}
```

```csharp
[assembly: AspNetCoreTesting(typeof(ProductionComposition))]
```

## Ports and teardown

The host listens on port 0 and reads the port back from the server, so tests running in parallel
never collide. When the test ends the client is disposed first, then the server is stopped under a
bound, `SocketHost.StopBound`, so a hung connection cannot hold the run open.

## Runners

Both hosts work under xUnit and NUnit. The runner package decides which; see
[Setting up a project](/guide/testing#setting-up-a-project).

## Next

- [Sending requests](/guide/testing-web): what `ITestWebApp` does on the pipeline
- [Typed clients](/guide/testing-clients): the clients that send to the socket
- [Getting started](/guide/getting-started#hosting-it-somewhere-else): the hosts an application names
