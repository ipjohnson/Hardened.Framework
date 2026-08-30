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

`TestWebRequest` also carries a `Token`, for asserting that a handler honours cancellation.

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

## What this exercises

The request goes through `IMiddlewareService` — the same entry point `app.UseHardened()` uses — so
the execution chain, every registered filter, the generated binding code and the serialisation
filter all run. A `[Retry]` attribute retries. A validation failure returns the configured status. A
handler that throws produces the error response the application would produce in production.

What is *not* exercised is the host: Kestrel, ASP.NET Core middleware registered outside Hardened,
and TLS. A test that needs those needs a real host.
