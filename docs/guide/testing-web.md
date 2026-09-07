# Sending requests

`ITestWebApp` sends a request through the application's pipeline and hands back the response.
Routing, filters, binding, the handler and serialization all run. No socket is opened and no host
is started.

```csharp
public class TodoTests {

    [HardenedTest]
    public async Task GetTodo_ReturnsTheTodo(ITestWebApp app) {
        var response = await app.Get("/todos/1");

        response.Assert.Ok();
        Assert.Equal("Read the generated code", response.Deserialize<Todo>().Title);
    }

    [HardenedTest]
    public async Task CreateTodo_AnswersCreated(ITestWebApp app) {
        var response = await app.Post(new NewTodo("Write a test"), "/todos");

        Assert.Equal(201, response.StatusCode);
        Assert.Equal("/todos/3", response.Headers["Location"].ToString());
    }
}
```

## Setup

`[WebTesting]` installs the harness. It goes beside the entry point attribute:

```csharp
// Bootstrap.cs
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;

[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(TodosLibrary))]
```

The test project references `Hardened.Web.Testing` beside the packages every test project has;
see [Setting up a project](/guide/testing#setting-up-a-project). A `Usings.cs` keeps the test
files short:

```csharp
global using Hardened.Shared.Testing.Attributes;
global using Hardened.Web.Testing;
global using Xunit;
```

## Requests

| Method | Signature |
|---|---|
| `Get` | `Get(path, configure?)` |
| `Post` | `Post(value, path, configure?)` |
| `Put` | `Put(value, path, configure?)` |
| `Patch` | `Patch(value, path, configure?)` |
| `Delete` | `Delete(path, configure?)` |
| `Request` | `Request(method, value, path, configure?)` |

The value comes first and the path second. The value is serialized as JSON. A `string` or a
`byte[]` goes on the wire as it is.

Query strings go in the path. Headers go through the configure callback:

```csharp
var response = await app.Get(
    "/binding/mixed/id-9?filter=active",
    request => request.Headers["X-Tenant"] = "acme");
```

`RawBody` sends bytes the serializer would never produce, for a test of what a malformed payload
answers:

```csharp
var response = await app.Post(new object(), "/registration", request => request.RawBody("{\"name\":"));

response.Assert.BadRequest();
Assert.Equal("ValidationError", response.Deserialize<RequestValidationError>().Type);
```

`request.Token` carries a `CancellationToken` into the request, for asserting that a handler
observes cancellation.

## The response

```csharp
public class TestWebResponse {
    public int StatusCode { get; }
    public IDictionary<string, StringValues> Headers { get; }
    public Stream Body { get; }
    public Exception? Failure { get; }
    public IWebAssertThat Assert { get; }

    public T Deserialize<T>();
    public Task<string> ReadTextAsync();
    public IAsyncEnumerable<T> DeserializeAsyncEnumerable<T>();
}
```

The harness sends `Accept-Encoding: gzip` on every request, so a handler that compresses answers
compressed. `Deserialize<T>`, `ReadTextAsync` and `DeserializeAsyncEnumerable<T>` undo gzip and
Brotli before reading. `Body` is the bytes as answered.

`ReadTextAsync` is for a body that is not JSON: a YAML document, a rendered page.
`DeserializeAsyncEnumerable<T>` reads NDJSON one item at a time, for a
[streaming handler](/guide/streaming).

`Failure` is the exception the pipeline recorded when it refused or failed the request. A handler
that threw answers the error envelope, whose 500 body says nothing about the cause. `Failure` is
the cause. It is null on a [socket host](/guide/testing-hosts), where only the envelope crosses
the wire.

### Status assertions

```csharp
response.Assert.Ok();           // any 2xx
response.Assert.BadRequest();   // 400
response.Assert.Unauthorized(); // 401
response.Assert.Forbidden();    // 403
response.Assert.NotFound();     // 404
```

Anything else is an `Assert.Equal` against `StatusCode`.

## A mock behind a route

`[Mock]` composes with `ITestWebApp`. The handler resolves from the container the mock was
registered in:

```csharp
using NSubstitute;

[HardenedTest]
public async Task UsesTheMockedService(ITestWebApp app, [Mock] IMathService<int> math) {
    math.Add(Arg.Any<int[]>()).Returns(100);

    var response = await app.Post(new MathAddModel { Values = [10, 20, 30] }, "/int/add");

    response.Assert.Ok();
    Assert.Equal(100, response.Deserialize<int>());
}
```

[Substituting services](/guide/testing-mocks) has the rest.

## What runs

The request enters through `IMiddlewareService`, the seam `app.UseHardened()` uses, so every
registered filter, the generated binding code and the serialization filter all run. A `[Retry]`
retries. A validation failure answers its status. A handler that throws produces the error
response production would.

The host does not run: Kestrel, ASP.NET Core middleware registered outside Hardened, and TLS. A
test that needs those puts a host on the test; see [Test hosts](/guide/testing-hosts).

## Next

- [Typed clients](/guide/testing-clients): the same pipeline behind a generated client
- [Credentials](/guide/testing-credentials): who `app.Get` sends as
- [Asserting a response](/guide/testing-responses): `LastResponse` and `Failure` in full
