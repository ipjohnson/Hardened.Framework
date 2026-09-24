# Test hosts

By default a test runs on the pipeline host, which executes the application's chain inside the
test's own call and opens no socket. `[KestrelRuntime]` on a test class runs each test in it on a
Kestrel server instead, on a loopback port that the kernel picks.

```csharp
using DependencyModules.Testing.Attributes;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Testing;
using NSubstitute;
using Xunit;

namespace Todos.Tests;

[KestrelRuntime]
public class TodoSocketTests
{
    [HardenedTest]
    public async Task ListTodos_OverTheSocket(ITestWebApp app)
    {
        var response = await app.Get("/todos");

        response.Assert.Ok();
        Assert.True(response.Headers.ContainsKey("Date"), "a header only a server writes");
    }

    [HardenedTest]
    public async Task GetTodo_ReadsTheMockedStore(ITestWebApp app, [Mock] ITodoStore store)
    {
        store.Find(1).Returns(new Todo(1, "from the mock", false));

        var todo = (await app.Get("/todos/1")).Deserialize<Todo>();

        Assert.Equal("from the mock", todo.Title);
    }
}
```

The attribute is the one from `Hardened.Web.Kestrel.Runtime` that an application names its host
with. The test project also needs `[assembly: KestrelTesting]`, which
[Enabling a socket host](#enabling-a-socket-host) describes.

`ITestWebApp`, an `HttpClient` parameter and every typed client send their requests to that server.
The server resolves the handlers from the test's own container, so a `[Mock]` parameter is the same
substitute over the socket.

The first test checks for `Date`, a header that only a server writes. The pipeline host does not
add it.

## Enabling a socket host

Each socket host is in a package of its own. The package has a provider attribute, which the test
project declares on its assembly. With the provider attribute declared, the host's runtime
attribute on a test runs that test on the host. Both provider attributes need
`[assembly: WebTesting]` beside them.

| Host | Test package | Provider attribute | Runtime attribute |
|---|---|---|---|
| Kestrel | `Hardened.Web.Kestrel.Testing` | `[assembly: KestrelTesting]` | `[KestrelRuntime]` |
| ASP.NET Core | `Hardened.Web.AspNetCore.Testing` | `[assembly: AspNetCoreTesting]` | `[AspNetCoreRuntime]` |

::: warning
Without the provider attribute, a test marked `[KestrelRuntime]` or `[AspNetCoreRuntime]` runs on
the pipeline with no error. A test that asserts nothing a socket changes then passes without ever
opening a socket.
:::

The `hardened-web` template adds the package reference and the provider attribute for its host. It
declares `KestrelTesting` for `--host kestrel` and `--host cloud-run`, and `AspNetCoreTesting` for
`--host aspnet`. For `--host kestrel`, `tests/Todos.Tests/Bootstrap.cs` holds these lines beside
`[assembly: WebTesting]`:

```csharp
using Hardened.Web.Kestrel.Testing;

[assembly: KestrelTesting]
```

The template also writes one test class, `TodosSocketTests`, that carries the runtime attribute.

One test project can declare both provider attributes. Each test then runs on the host that its own
runtime attribute names. The socket hosts run under both runner packages, for xUnit and for NUnit.

## Choosing the host for a test

A runtime attribute goes on a method, a class or the assembly. The narrowest one decides the host.
The template puts `[KestrelRuntime]` on one class rather than on the assembly.

`[PipelineHost]`, from `Hardened.Web.Testing`, puts a test back on the pipeline host. It also goes
on a method, a class or the assembly. Added to `TodoSocketTests` from the first example, this method
runs on the pipeline host, because `[PipelineHost]` on the method is narrower than
`[KestrelRuntime]` on the class:

```csharp
    [HardenedTest]
    [PipelineHost]
    public async Task ListTodos_InProcess(ITestWebApp app)
    {
        var response = await app.Get("/todos");

        response.Assert.Ok();
        Assert.False(response.Headers.ContainsKey("Date"));
    }
```

Each test on a socket host starts a server of its own. The server stops when the test ends.

## What differs on a socket

The socket hosts serve plain HTTP on `127.0.0.1`. A test sees these differences between the hosts:

| | Pipeline | Kestrel | ASP.NET Core |
|---|---|---|---|
| Response headers | Only what the application wrote | What the server sent, including `Date` and `Server: Kestrel` | What the server sent, including `Date` and `Server: Kestrel` |
| `TestWebResponse.Failure` after a handler threw | The exception | Null | Null |
| A path with no route | 404 | 404 | Handed to the rest of the ASP.NET Core pipeline, which answers 404 when nothing else does |
| `LastResponse` | What the pipeline answered | What came back over the socket | What came back over the socket |
| Requests in one test | Each in a container of its own | All in one container | All in one container |

The credential attributes work on both socket hosts. A test marked `[Grants("todos:read")]` reaches
an `[AuthorizeGrants("todos:read")]` handler. The same request without the attribute answers 401.
[Sending requests](/guide/testing-web) covers the credential attributes, `Failure` and
`LastResponse`.

`[Shared]` on a parameter changes nothing on a socket host. [Writing a test](/guide/testing) covers
the container per request and `[Shared]`.

## The ASP.NET Core composition

By default the ASP.NET Core test host builds a `WebApplication` whose pipeline is
`app.UseHardened()` alone. `IAspNetCoreTestComposition` mirrors what the host project's
`Program.cs` puts around `UseHardened()`. Its `Configure(WebApplicationBuilder)` runs before the
application is built. Its `Configure(WebApplication)` runs after, in place of the default, so it
calls `app.UseHardened()` itself.

A composition is a class that implements the interface and has a public parameterless constructor.
In the `--host aspnet` project, `tests/Todos.Tests/ProgramComposition.cs` holds this one:

```csharp
using Hardened.Web.AspNetCore.Runtime;
using Hardened.Web.AspNetCore.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Todos.Tests;

public sealed class ProgramComposition : IAspNetCoreTestComposition
{
    public void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks();
    }

    public void Configure(WebApplication app)
    {
        app.UseHardened();

        app.MapHealthChecks("/healthz");
    }
}
```

`[assembly: AspNetCoreTesting(typeof(ProgramComposition))]` names the composition, once for the
test project. In `tests/Todos.Tests/Bootstrap.cs`, it replaces the template's
`[assembly: AspNetCoreTesting]`:

```csharp
using Hardened.Web.AspNetCore.Testing;
using Todos.Tests;

[assembly: AspNetCoreTesting(typeof(ProgramComposition))]
```

`tests/Todos.Tests/HealthTests.cs` sends one request to the composition's endpoint and one to a
Hardened route:

```csharp
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.AspNetCore.Runtime;
using Hardened.Web.Testing;
using Xunit;

namespace Todos.Tests;

[AspNetCoreRuntime]
public class HealthTests
{
    [HardenedTest]
    public async Task Healthz_IsAnsweredByAspNetCore(ITestWebApp app)
    {
        var response = await app.Get("/healthz");

        response.Assert.Ok();
        Assert.Equal("Healthy", await response.ReadTextAsync());
    }

    [HardenedTest]
    public async Task ListTodos_IsAnsweredByHardened(ITestWebApp app)
    {
        var response = await app.Get("/todos");

        response.Assert.Ok();
    }
}
```

The composition applies only to tests on the ASP.NET Core host. A test on the pipeline or on Kestrel
in the same project gets 404 for `/healthz`.

A composition that does not call `app.UseHardened()` serves no Hardened route. `GET /todos` then
answers 404. The composition's own endpoint still answers 200.

The environment name in `app.Environment` is the test's Hardened environment. It is `test` unless
`[EnvironmentName]` names another. [Environments](/guide/environments) covers `[EnvironmentName]`.

Without the public parameterless constructor, every test on the host fails with an
`InvalidOperationException`. The message is the class's full name followed by
`is named as the composition of [assembly: AspNetCoreTesting], and it is not a public IAspNetCoreTestComposition with a parameterless constructor.`

Without `[assembly: WebTesting]` beside `[assembly: AspNetCoreTesting]`, every test fails with
`[assembly: AspNetCoreTesting] builds the container for a test [WebTesting] registered a host in, and this test has none: declare [assembly: WebTesting] beside the entry point attribute.`

## Ports and teardown

A socket host listens on port 0 of `127.0.0.1`. It reads the address back from the server once the
server has started. Tests that run at the same time get different ports.

A test can take `ITestHost` as a parameter. Its `BaseAddress` is the address that the server bound,
such as `http://127.0.0.1:56953/`.

When a test ends, the host disposes its client first. Disposing the client closes every connection
that the test's clients opened. The host then stops the server and waits at most
`SocketHost.StopBound` for requests that are still running. Kestrel then aborts what is left.

`StopBound` is 10 seconds by default. It is a static property in `Hardened.Web.Testing`. A test
project can set it. A test that ends while a handler is still waiting out 60 seconds makes its run
10.6 seconds longer than a run whose requests have all finished. With `StopBound` set to 2 seconds,
the run is about 3 seconds longer.

## Test hosts on each cloud

Every host that a test can run on has a test attribute. All of them need `[assembly: WebTesting]`.

| Host | Test package | Declared with | Where a request goes |
|---|---|---|---|
| The pipeline, the default | `Hardened.Web.Testing` | Nothing, or `[PipelineHost]` on a method, a class or the assembly | The application's chain, in the test's own call |
| Kestrel, and Google Cloud Run | `Hardened.Web.Kestrel.Testing` | `[assembly: KestrelTesting]`, then `[KestrelRuntime]` on a method, a class or the assembly | A Kestrel server on `127.0.0.1` |
| ASP.NET Core | `Hardened.Web.AspNetCore.Testing` | `[assembly: AspNetCoreTesting]`, then `[AspNetCoreRuntime]` on a method, a class or the assembly | A `WebApplication` on `127.0.0.1` |
| AWS Lambda | `Hardened.Aws.Lambda.Testing` | `[LambdaWebTesting]` on a method, a class or the assembly, with `[LambdaHttpModule]` beside it | An API Gateway event, through the Lambda invocation handler |
| Azure Functions | `Hardened.Azure.Functions.Testing` | `[AzureFunctionsWebTesting]` on a method, a class or the assembly | The worker's request data, through the Functions invocation handler |

No test attribute runs the Google Cloud Functions server. `[CloudRunRuntime]` runs on the Kestrel
host, so the tests of a Cloud Run or Cloud Functions application run on the pipeline, or on Kestrel
under `[KestrelRuntime]`.

`[LambdaWebTesting]` builds an API Gateway payload format 2.0 event from the request. It invokes the
function's `LambdaInvocationHandler` and reads the proxy response back.

The attribute needs the Lambda HTTP module in the test's application. The template's test project
builds the library module, `TodosLibrary`, which does not import it. `[LambdaHttpModule]` beside
`[LambdaWebTesting]` loads the module. Naming the host project's `Application` as the entry point
loads it too, with the test project referencing the host project. [Writing a test](/guide/testing)
covers the entry point. Without the module, every request fails with
`No service for type 'Hardened.Aws.Lambda.Runtime.Hosting.LambdaInvocationHandler' has been registered.`

In the `--host aws-lambda` project, these lines add `Hardened.Aws.Lambda.Testing` to
`Directory.Packages.props` and to `tests/Todos.Tests/Todos.Tests.csproj`:

```xml
<PackageVersion Include="Hardened.Aws.Lambda.Testing" Version="$(HardenedVersion)" />
```

```xml
<PackageReference Include="Hardened.Aws.Lambda.Testing" />
```

`tests/Todos.Tests/TodoLambdaTests.cs` then puts both attributes on its class:

```csharp
using Hardened.Aws.Lambda.Http;
using Hardened.Aws.Lambda.Testing;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Xunit;

namespace Todos.Tests;

[LambdaWebTesting]
[LambdaHttpModule]
public class TodoLambdaTests
{
    [HardenedTest]
    public async Task ListTodos_ThroughTheInvocationHandler(ITestWebApp app)
    {
        var response = await app.Get("/todos");

        response.Assert.Ok();
        Assert.False(response.Headers.ContainsKey("Date"));
    }

    [HardenedTest]
    public async Task APathWithNoRoute_Is404(ITestWebApp app)
    {
        var response = await app.Get("/nothing-here");

        Assert.Equal(404, response.StatusCode);
    }
}
```

`[AzureFunctionsWebTesting]` builds the isolated worker's `HttpRequestData`. It invokes the
function's `FunctionsInvocationHandler` and reads the `HttpResponseData` back. No Functions host
process starts.

The attribute loads the Azure Functions HTTP module itself, so the template's test project needs
nothing beside it. For `--host azure-functions`, the template writes
`[assembly: AzureFunctionsWebTesting]`, so every test in that project runs through the worker.

The Lambda and Azure Functions hosts answer 404 for a path with no route. They set `Failure` to
null. AWS [Testing](/aws/testing) covers the `ResponseMode` of `[LambdaWebTesting]`.

## Limits

The Lambda and Azure Functions test hosts do not apply the credential attributes. On either host, a
test marked `[Grants("todos:read")]` gets 401 from an `[AuthorizeGrants("todos:read")]` handler.
The same request gets 200 when the test sets the `X-Test-Grants` header by hand.

Neither host records `LastResponse`. Reading it after a request throws, as it does before any
request.

Under `[LambdaWebTesting]`, a response read through an `HttpClient` has no `Content-Type`, so a
typed client fails. The template's Kiota tests fail under it with
``The response declares a body of List`1 and carried none.``

## Next

| Page | Covers |
|---|---|
| [Writing a test](/guide/testing) | A container per request, and `[Shared]` |
| [Sending requests](/guide/testing-web) | What `ITestWebApp` sends and reads |
| [Testing functions](/guide/testing-functions) | Testing trigger and invocation handlers |
| [Hosts](/guide/hosts) | Each host's `Program.cs` |
| AWS [Testing](/aws/testing) | `[LambdaWebTesting]` and the Lambda response mode |
