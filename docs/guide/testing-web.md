# Testing web handlers

`ITestWebApp` sends a request through the application's real pipeline — routing, filters, parameter
binding, the handler, serialisation — without a socket, a port or a running host.

## Setup

Two assembly attributes: the test harness, and the application under test.

```csharp
// Bootstrap.cs
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;

[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]
```

A `Usings.cs` with the common imports keeps the test files themselves short:

```csharp
global using Hardened.Shared.Testing.Attributes;
global using Hardened.Web.Testing;
global using Xunit;
```

## Sending a request

Take `ITestWebApp` as a parameter:

```csharp
public class HomeControllerTests {
    [HardenedTest]
    public async Task GetReturnsTheValue(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/test");

        response.Assert.Ok();

        Assert.Equal("somevalue", response.Deserialize<string>());
    }
}
```

| Method | Signature |
|---|---|
| `Get` | `Get(path, configure?)` |
| `Post` | `Post(value, path, configure?)` |
| `Put` | `Put(value, path, configure?)` |
| `Patch` | `Patch(value, path, configure?)` |
| `Delete` | `Delete(path, configure?)` |
| `Request` | `Request(method, value, path, configure?)` |

The body value is serialised for you, so a POST takes the model:

```csharp
[HardenedTest]
public async Task AddsTheValues(ITestWebApp testWebApp) {
    var model = new MathAddModel { Values = new List<int> { 10, 20, 30 } };

    var response = await testWebApp.Post(model, "/int/add");

    response.Assert.Ok();
    Assert.Equal(60, response.Deserialize<int>());
}
```

Note the argument order: the value comes first, the path second.

## Headers and query strings

Query strings go in the path. Headers go through the configure callback:

```csharp
[HardenedTest]
public async Task BindsEverySource(ITestWebApp testWebApp) {
    var response = await testWebApp.Get(
        "/binding/mixed/id-9?filter=active",
        request => request.Headers["X-Tenant"] = "acme");

    response.Assert.Ok();
    Assert.Equal("id-9|active|acme|3", response.Deserialize<string>());
}
```

`TestWebRequest` also carries a `Token`, for asserting that a handler honours cancellation, and a
raw body for a payload the serialiser would never produce:

```csharp
[HardenedTest]
public async Task ARawBodyOnTheRequestAnswersTheValidationStatus(ITestWebApp app) {
    var response = await app.Post(new object(), "/registration", request => request.RawBody("{\"name\":"));

    response.Assert.BadRequest();
    Assert.Equal("ValidationError", response.Deserialize<RequestValidationError>().Type);
}
```

A `string` or a `byte[]` passed as the value goes on the wire as itself too, so
`app.Request("POST", "{\"name\":", "/registration")` is the same request.

## The response

```csharp
public class TestWebResponse {
    public int StatusCode { get; }
    public IDictionary<string, StringValues> Headers { get; }
    public Stream Body { get; }
    public IWebAssertThat Assert { get; }

    public T Deserialize<T>();
    public IAsyncEnumerable<T> DeserializeAsyncEnumerable<T>();
}
```

`Deserialize<T>` transparently decompresses gzip and Brotli bodies. The test client sends
`Accept-Encoding: gzip`, so a test asserting on the deserialised value never has to know which it
got.

`DeserializeAsyncEnumerable<T>` reads NDJSON, one object per line, for streaming handlers.

### Status assertions

```csharp
response.Assert.Ok();           // any 2xx
response.Assert.NotFound();     // 404
response.Assert.BadRequest();   // 400
response.Assert.Unauthorized(); // 401
response.Assert.Forbidden();    // 403
```

Anything else is an `Assert.Equal` against `response.StatusCode`.

## Mocking a service behind a route

`[Mock]` composes with `ITestWebApp`. The mock is registered before the application's own graph is
built, so the handler on the other end of the route gets it:

```csharp
using NSubstitute;

public class MathControllerTests {
    [HardenedTest]
    public async Task UsesTheMockedService(
        ITestWebApp testWebApp,
        [Mock] IMathService<int> mockService) {

        mockService.Add(Arg.Any<int[]>()).Returns(100);

        var model = new MathAddModel { Values = new List<int> { 10, 20, 30 } };

        var response = await testWebApp.Post(model, "/int/add");

        response.Assert.Ok();
        Assert.Equal(100, response.Deserialize<int>());
    }
}
```

## An HttpClient over the pipeline

A client library — generated or hand-written — is an `HttpClient` consumer, and the harness has no
port. `ITestWebApp.CreateHttpClient()` returns an `HttpClient` whose handler runs the same pipeline
`Get` runs: the request's method, path, headers, cookies and body bytes become an execution context,
the chain runs, and the response comes back with its status, headers, `Set-Cookie` and body.

```csharp
[HardenedTest]
public async Task MalformedJsonThroughAnHttpClientAnswersTheValidationStatus(ITestWebApp app) {
    using var client = app.CreateHttpClient();
    using var content = new StringContent("{\"name\":", System.Text.Encoding.UTF8, "application/json");
    using var response = await client.PostAsync("/registration", content, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}
```

Its `BaseAddress` is `http://harness/`, which the handler ignores and a client that builds relative
URLs resolves against. The path is decoded the way Kestrel decodes one - `%20` is a space, `%2F`
stays as written - and the same table is measured over a real socket in the Kestrel host's own
tests. The handler is `PipelineHttpMessageHandler`, public and constructible from the root
`IServiceProvider` for a test with nothing else in hand, and it is held to the same request,
response and telemetry conformance suites every other transport is.

## Typed clients as parameters

A test declares a client type as a parameter and gets one built over that `HttpClient`:

```csharp
[HardenedTest]
public async Task GetTodo_ThroughTheGeneratedClient(TodosClient client) {
    var todo = await client.Todos[1].GetAsync();

    Assert.Equal("Read the generated code", todo!.Title);
}
```

How the client is constructed is the only generator-shaped question, and it is answered in two
steps. By convention: a type with a single public constructor taking exactly one `HttpClient` is
constructed with the harness's client, which is what NSwag's output and most hand-written clients
look like. Otherwise by a factory: a public `ITestClientFactory<T>` in the test assembly, found once
per assembly, with one method from `HttpClient` to `T`. Kiota needs the factory, because its
constructor takes an `IRequestAdapter`; the template writes it:

```csharp
public sealed class TodosClientFactory : ITestClientFactory<TodosClient> {
    public TodosClient Create(HttpClient http) =>
        new(new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: http) {
            BaseUrl = "http://harness"
        });
}
```

A parameter type with neither route fails the test naming both. `app.CreateClient<T>()` is the same
construction for a client built inside the test, with a credential of the test's choosing.
`[Mock]` composes with all of it: the mock is in the same graph the handler resolves from, so a
client reaching the handler sees it. The framework's
[`GeneratedClientTests`](https://github.com/ipjohnson/Hardened.Framework/blob/main/src/IntegrationTests/Web/Hardened.IntegrationTests.WebApp.SUT.Tests/Transport/GeneratedClientTests.cs) is the worked example: a Kiota client over its widest integration
application, through every door this page describes.

## Credentials

Who a request is sent as is an attribute, valid on a parameter, a method, a class or the assembly,
and the narrower wins:

| Attribute | Sends |
|---|---|
| `[Grants("todos:read", "todos:write")]` | `X-Test-Grants` with the grants |
| `[Subject("pia")]` | `X-Test-Subject`, for a test where one caller's data reaching another is the point |
| `[Anonymous]` | nothing, cancelling whatever a wider level declared |

| Level | Beats |
|---|---|
| parameter | method, class, assembly |
| method | class, assembly |
| class | assembly |

The headers are the ones `TestGrantsPrincipalSource` reads, which `[WebTesting]` registers beside
the application's own sources; it answers only a request carrying them, so an application's own
authentication is untouched in a test with no attributes. They apply to the `HttpClient` the harness
hands out, to every client built over it, and to `app.Get` and friends when the configure callback
set neither header. Two parameters of one client type with different attributes are two clients:

```csharp
[HardenedTest]
public async Task TwoParametersCarryTwoCredentials(
    ProbeClient reader, [Anonymous] ProbeClient nobody, [Grants("pets:write")] ProbeClient writer) {
    using var read = await reader.Pets(TestContext.Current.CancellationToken);
    using var refused = await nobody.Pets(TestContext.Current.CancellationToken);
    using var forbidden = await writer.Pets(TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
}
```

## The last response

A refusal is asserted with `Assert.ThrowsAsync` against whatever the client library throws. What no
client library surfaces is the response it did not throw on: the 201 and its `Location`, a 204, an
`ETag`. The transport keeps the most recent response the pipeline answered inside the current test
and exposes it as a static, whether it went out through a client or through `app.Get`:

```csharp
[HardenedTest]
public async Task CreateTodo_AnswersCreated(TodosClient client) {
    var todo = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" });

    Assert.Equal(201, LastResponse.Status);
    Assert.Equal($"/todos/{todo!.Id}", LastResponse.Headers["Location"]);
}
```

`LastResponse` carries `Status`, `Headers`, `ContentType` and `Body` as bytes. It is keyed on xUnit's
`TestContext.Current`, so parallel tests read their own answers, and reading it before anything was
answered fails naming the test.

## What this exercises

The request goes through `IMiddlewareService` — the same entry point `app.UseHardened()` uses — so
the execution chain, every registered filter, the generated binding code and the serialisation
filter all run. A `[Retry]` attribute retries. A validation failure returns the configured status. A
handler that throws produces the error response the application would produce in production.

What is *not* exercised is the host: Kestrel, ASP.NET Core middleware registered outside Hardened,
and TLS. A test that needs those needs a real host, and every suite should budget for one smoke
test against a real socket — the two response-cache defects the 0.19 trial found were visible only
there.

## Next

- [Clients](/guide/clients) — the generated client the scaffold drives through this transport
