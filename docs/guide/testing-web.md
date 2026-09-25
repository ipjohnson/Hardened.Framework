# Sending requests

`ITestWebApp` is a test parameter that sends a request through the application's pipeline and returns a `TestWebResponse`. Routing, the filters, parameter binding, the handler and serialization run. No socket is opened and no host is started.

```csharp
using DependencyModules.xUnit.Attributes;
using Hardened.Web.Testing;

namespace Todos.Tests;

public class TodoRequestTests
{
    [ModuleTest]
    public async Task GetTodo_ReturnsTheTodo(ITestWebApp app)
    {
        var response = await app.Get("/todos/1");

        response.Assert.Ok();
        Assert.Equal("Read the generated code", response.Deserialize<Todo>().Title);
    }

    [ModuleTest]
    public async Task CreateTodo_AnswersCreated(ITestWebApp app)
    {
        var response = await app.Post(new NewTodo("Write a test"), "/todos");

        Assert.Equal(201, response.StatusCode);
        Assert.Equal("/todos/3", response.Headers["Location"].ToString());
    }
}
```

The tests on this page are classes in the `tests/Todos.Tests` project that `dotnet new hardened-web -n Todos` creates. `Todo` and `NewTodo` are the application's own records. The template's test project references the library in `src/Todos`. The store starts with two todos, so the `POST` answers 201 with `Location: /todos/3`.

`ITestWebApp` and `TestWebResponse` are in the `Hardened.Web.Testing` namespace and package.

## Setup

The test project references the `Hardened.Web.Testing` package. The test project of the `hardened-web` template already does. `[assembly: WebTesting]` turns the package on. It goes beside `[assembly: HardenedTestEntryPoint]`, which [Writing a test](/guide/testing) covers with the rest of the project setup. This excerpt is from the template's `tests/Todos.Tests/Bootstrap.cs`:

```csharp
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Todos;

[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(TodosLibrary))]
```

`[WebTesting]` is valid on the assembly only. It registers `ITestWebApp`, a principal source for test credentials, and a client for each test parameter of a client type. [Typed clients](/guide/testing-clients) covers the clients. Without the attribute, a test that takes `ITestWebApp` fails with `InvalidOperationException: Instances of abstract classes cannot be created.`

`ITestWebApp` is also an `ITestContext`, so it carries `Step`, `Retry`, `Logger` and `CancellationRequest`. [Writing a test](/guide/testing) covers them.

## Request methods

Each method returns `Task<TestWebResponse>`.

| Method | Signature |
|---|---|
| `Get` | `Get(string path, Action<TestWebRequest>? webRequest = null)` |
| `Post` | `Post(object value, string path, Action<TestWebRequest>? webRequest = null)` |
| `Put` | `Put(object value, string path, Action<TestWebRequest>? webRequest = null)` |
| `Patch` | `Patch(object value, string path, Action<TestWebRequest>? webRequest = null)` |
| `Delete` | `Delete(string path, Action<TestWebRequest>? webRequest = null)` |
| `Request` | `Request(string method, object? value, string path, Action<TestWebRequest>? webRequest = null)` |

The value comes before the path. The application's own JSON serializer writes the value as JSON, with the application's resolvers. A `string`, a `byte[]` or a `ReadOnlyMemory<byte>` is sent as it is. `Request` sends any method. A null value sends no body.

A request with a body and no `Content-Type` header is sent with `Content-Type: text/js`. The JSON deserializer reads it. A body in another format needs its `Content-Type`, set in the callback or with `RawBody`.

The query string goes in the path. The path is percent-decoded the way Kestrel decodes a path:

| In the path | Reaches the pipeline as |
|---|---|
| `%20` | A space |
| `%C3%A9` | `é` |
| `%2F` | `%2F`, as written |
| `+` | A plus |

## Headers, raw bodies and cancellation

The callback receives a `TestWebRequest`:

| Member | Sets |
|---|---|
| `Headers` | The request headers, an `IDictionary<string, StringValues>` matched without regard to case |
| `RawBody(string text, string contentType = "application/json")` | The body's bytes and its `Content-Type` |
| `RawBody(byte[] bytes, string contentType)` | The body's bytes and its `Content-Type` |
| `Token` | The request's `CancellationToken` |

The request carries `Accept-Encoding: gzip` by default. A value that the callback sets for `Accept-Encoding` is sent instead. In this example, `TodoController.All` carries `[Compress]`, as on the [Compression](/guide/compression) page:

```csharp
using DependencyModules.xUnit.Attributes;
using Hardened.Web.Testing;

namespace Todos.Tests;

public class TodoHeaderTests
{
    [ModuleTest]
    public async Task ListTodos_Uncompressed(ITestWebApp app)
    {
        var response = await app.Get(
            "/todos",
            request => request.Headers["Accept-Encoding"] = "identity"
        );

        Assert.False(response.Headers.ContainsKey("Content-Encoding"));
    }
}
```

A `Cookie` header is split into the request's cookies, which `[FromCookie]` binds.

A raw body replaces the value that the method was called with. This test sends a malformed JSON body:

```csharp
using DependencyModules.xUnit.Attributes;
using Hardened.Web.Testing;

namespace Todos.Tests;

public class TodoBodyTests
{
    [ModuleTest]
    public async Task CreateTodo_MalformedJson_IsBadRequest(ITestWebApp app)
    {
        var response = await app.Request(
            "POST",
            null,
            "/todos",
            request => request.RawBody("{\"title\":")
        );

        response.Assert.BadRequest();
    }
}
```

The 400 is the validation envelope, with `"field":"request"` and `"code":"invalid"`. [Validation](/guide/validation) covers it.

Without `Token`, the request carries the test's cancellation token. A handler's `CancellationToken` parameter receives the token. A handler that throws on the cancelled token answers 500. The response's `Failure` is then the `TaskCanceledException`. A token cancelled after a streamed response has started makes the call throw `TaskCanceledException`.

## Reading the response

The response has these members:

| Member | What it is |
|---|---|
| `StatusCode` | The status, 200 when the pipeline set none |
| `Headers` | The response headers, an `IDictionary<string, StringValues>` matched without regard to case |
| `Body` | The body as a `Stream`, as the pipeline wrote it, compressed when it was compressed |
| `Failure` | The exception behind a failed request, or null |
| `Assert` | The status assertions |
| `Deserialize<T>()` | The body read as JSON |
| `ReadTextAsync()` | The body as a string |
| `DeserializeAsyncEnumerable<T>()` | An NDJSON body, one line at a time |

A test runs on the pipeline host by default. There, `Headers` holds what the pipeline wrote, such as `Content-Type`, `Location` and `X-Correlation-Id`. Headers that a server adds, such as `Date` and `Content-Length`, are absent. A cookie that the handler sets is in `Headers` as `Set-Cookie`.

`Deserialize<T>()`, `ReadTextAsync()` and `DeserializeAsyncEnumerable<T>()` undo a gzip or Brotli `Content-Encoding` before reading. Any other content coding throws `BadContentEncodingException`. `Deserialize<T>()` and `DeserializeAsyncEnumerable<T>()` read with System.Text.Json's web defaults: camelCase names, matched without regard to case. They do not use the application's JSON settings.

`Deserialize<T>()` reads `Body` from where it stands and does not rewind it. A second `Deserialize<T>()`, or one after `ReadTextAsync()`, throws `JsonException`. `Deserialize<T>()` on an empty body, such as a 204's, throws `JsonException` too. `ReadTextAsync()` and `DeserializeAsyncEnumerable<T>()` rewind `Body` before reading.

On the pipeline host, the call returns when the handler has finished, so every item of a stream is in `Body`. `DeserializeAsyncEnumerable<T>()` skips blank lines. It throws `JsonException` on the `data:` lines of a server-sent-events body. `ReadTextAsync()` returns those lines. [Streaming responses](/guide/streaming) covers the stream formats.

In this example, `TodoController.All` carries `[Compress]`. `TodoFeedController`, from the first example of [Streaming responses](/guide/streaming), answers `GET /todos/feed`.

```csharp
using DependencyModules.xUnit.Attributes;
using Hardened.Web.Testing;

namespace Todos.Tests;

public class TodoResponseTests
{
    [ModuleTest]
    public async Task ListTodos_IsCompressed(ITestWebApp app)
    {
        var response = await app.Get("/todos");

        Assert.Equal("gzip", response.Headers["Content-Encoding"].ToString());
        Assert.Equal(2, response.Deserialize<List<Todo>>().Count);
    }

    [ModuleTest]
    public async Task TheDocumentNamesGetTodo(ITestWebApp app)
    {
        var document = await (await app.Get("/openapi.json")).ReadTextAsync();

        Assert.Contains("\"getTodo\"", document);
    }

    [ModuleTest]
    public async Task Feed_StreamsEveryTodo(ITestWebApp app)
    {
        var response = await app.Get("/todos/feed");

        var titles = new List<string>();

        await foreach (var todo in response.DeserializeAsyncEnumerable<Todo>())
        {
            titles.Add(todo.Title);
        }

        Assert.Equal(["Read the generated code", "Add an endpoint"], titles);
    }
}
```

In the first test, `Body` starts with the gzip header `1F 8B`.

## Status assertions

`Assert` on the response checks the status:

| Call | Passes on |
|---|---|
| `response.Assert.Ok()` | Any status from 200 to 299 |
| `response.Assert.BadRequest()` | 400 |
| `response.Assert.Unauthorized()` | 401 |
| `response.Assert.Forbidden()` | 403 |
| `response.Assert.NotFound()` | 404 |

On any other status, the call throws `WebAssertionException`. xUnit and NUnit report it as the test's failure. `Ok()` on a 404 fails with `Expected a 2xx status, the response was 404.` `NotFound()` on a 200 fails with `Expected status 404, the response was 200.`

A test asserts any other status on `StatusCode`, as the first example does for the 201.

## Credentials

`[Grants]`, `[Subject]` and `[Anonymous]` name who a test's requests are sent as. They are in `Hardened.Web.Testing`. They send two headers, `X-Test-Grants` and `X-Test-Subject`:

| Attribute | Sends |
|---|---|
| `[Grants("todos:read", "todos:write")]` | `X-Test-Grants: todos:read todos:write` |
| `[Grants]`, with no grants | `X-Test-Grants: -` |
| `[Subject("pia")]` | `X-Test-Subject: pia`, and `X-Test-Grants: -` when no grants are in scope |
| `[Anonymous]` | Neither header |

`ITestWebApp`, an `HttpClient` parameter and every client built for a parameter send the credential. They send it on a socket host too.

In this example, `TodoController.ById` carries `[AuthorizeGrants("todos:read")]`, as in the first example of [Authorization](/guide/authorization):

```csharp
using DependencyModules.xUnit.Attributes;
using Hardened.Web.Testing;

namespace Todos.Tests;

[Grants("todos:read")]
public class TodoReaderTests
{
    [ModuleTest]
    public async Task AReaderGetsTheTodo(ITestWebApp app)
    {
        (await app.Get("/todos/1")).Assert.Ok();
    }

    [ModuleTest]
    [Anonymous]
    public async Task NobodyIsUnauthorized(ITestWebApp app)
    {
        (await app.Get("/todos/1")).Assert.Unauthorized();
    }

    [ModuleTest]
    public async Task AWriterIsForbidden([Grants("todos:write")] ITestWebApp writer)
    {
        (await writer.Get("/todos/1")).Assert.Forbidden();
    }
}
```

The attributes are valid on a parameter, a method, a class and the assembly. They apply widest first: the assembly, then the class, then the method, then the parameter. Each one changes what the wider levels set:

| Attribute | Grants | Subject |
|---|---|---|
| `[Grants]` | Replaced | Kept |
| `[Subject]` | Kept | Replaced |
| `[Anonymous]` | Cleared | Cleared |

A parameter with no attribute takes the method's credential.

`[Shared]` on a parameter that also carries a credential attribute does not send its requests to one container. Put the credential on the method instead. [Writing a test](/guide/testing) covers `[Shared]`.

## How the caller is authenticated

`[WebTesting]` registers `TestGrantsPrincipalSource` beside the application's own principal sources. The source is in `Hardened.Requests.Testing`. [Authentication](/guide/authentication) covers principal sources.

The source authenticates a request that carries `X-Test-Grants`. The caller holds the grants that the header names, separated by spaces. A value of `-` names no grants. The caller's subject is the value of `X-Test-Subject`, or `integration-test` when that header is absent. The caller's scheme is `test`. A request without `X-Test-Grants` is left to the application's own sources, including a request that carries only `X-Test-Subject`.

Authorization sees the caller. `[AuthorizeGrants]` and `ICurrentCaller` read its grants and subject. A caller with no grants gets 403. A request with no caller gets 401.

Outside a test, nothing registers the source, so the two headers authenticate nobody. The application with `[AuthorizeGrants("todos:read")]` on `ById`, run on Kestrel, answers a request that carries them with a 401:

```http
GET /todos/1
X-Test-Grants: todos:read
X-Test-Subject: pia

HTTP/1.1 401 Unauthorized
Content-Type: application/json
WWW-Authenticate: Bearer

{"type":"AuthorizationException","message":"This request requires authentication.","details":""}
```

## Setting the caller in the test

A parameter's attribute applies to that parameter alone, so two parameters of one type with different attributes are two callers. A callback that sets `X-Test-Grants` or `X-Test-Subject` sends what it set, and nothing from the attributes. A callback that sets only `X-Test-Subject` therefore sends no caller.

The attributes resolve to a `TestCredential`, declared as `TestCredential(IReadOnlyList<string>? Grants, string? Subject = null)`. `TestCredential.Anonymous` sends neither header. An empty grant list sends `X-Test-Grants: -`. `app.CreateClient<T>(credential)` and `app.CreateHttpClient(credential)` build a client that sends the credential given. Without a credential, they send the credential in scope.

`ById` carries the same `[AuthorizeGrants("todos:read")]` in this example:

```csharp
using DependencyModules.xUnit.Attributes;
using Hardened.Web.Runtime.Responses;
using Hardened.Web.Testing;
using Todos.Client;

namespace Todos.Tests;

public class TodoCallerTests
{
    [ModuleTest]
    public async Task ReadingATodoNeedsTheGrant(
        [Grants("todos:read")] TodosClient reader,
        [Anonymous] TodosClient nobody,
        [Grants("todos:write")] TodosClient writer
    )
    {
        await reader.Todos[1].GetAsync().Returns<Ok<ClientModels.Todo>>();
        await nobody.Todos[1].GetAsync().ReturnsStatus<Unauthorized>();
        await writer.Todos[1].GetAsync().ReturnsStatus<Forbidden>();
    }

    [ModuleTest]
    public async Task AHeaderSetInTheTestIsSentAsIs(ITestWebApp app)
    {
        var response = await app.Get(
            "/todos/1",
            request => request.Headers["X-Test-Grants"] = "todos:read"
        );

        response.Assert.Ok();
    }

    [ModuleTest]
    public async Task ACredentialBuiltInTheTest(ITestWebApp app)
    {
        var reader = app.CreateClient<TodosClient>(new TestCredential(["todos:read"], "pia"));

        await reader.Todos[1].GetAsync().Returns<Ok<ClientModels.Todo>>();
    }
}
```

`Returns<T>()` and `ReturnsStatus<T>()` assert a call through a client. [Typed clients](/guide/testing-clients) covers them.

## The last response

`LastResponse` holds the most recent response that the pipeline answered in the running test. It is a static class in `Hardened.Web.Testing`. It records the response to a request sent through `ITestWebApp`, through an `HttpClient` or through a client built for a parameter. It also records a response that the client threw on. It holds what a client does not hand back, such as the status and the `Location` of a 201 that the client returned as a body:

```csharp
using DependencyModules.xUnit.Attributes;
using Hardened.Web.Testing;
using Todos.Client;

namespace Todos.Tests;

public class TodoLastResponseTests
{
    [ModuleTest]
    public async Task CreateTodo_AnswersCreated(TodosClient client)
    {
        var todo = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" });

        Assert.Equal(201, LastResponse.Status);
        Assert.Equal($"/todos/{todo!.Id}", LastResponse.Headers["Location"]);
    }
}
```

| Member | What it is |
|---|---|
| `Status` | The status, 200 when the pipeline set none |
| `Headers` | The headers, an `IReadOnlyDictionary<string, StringValues>` matched without regard to case |
| `ContentType` | The content type, null when there is none, as on a 204 |
| `Body` | The body as a `byte[]`, as written, compressed when it was compressed |
| `IsAvailable` | Whether the running test has had a response |

The recorded response is kept per running test, so tests that run in parallel each read their own. Reading `LastResponse` before any response throws `InvalidOperationException` with this message: `LastResponse has nothing to report: no request has been answered through the pipeline in 'Todos.Tests.TodoTimingTests.ReadsLastResponseFirst'. Send one through ITestWebApp, or through a client it built, before reading it.` The quoted name is the running test's.

On a socket host, `LastResponse` holds what came back over the wire, with the server's headers. It records the body of an event stream as empty there.

## The exception behind a failed request

The response's `Failure` is the exception that the pipeline recorded when it failed or refused the request, or null. In this example, `[Mock]` makes the store throw. [Substituting services](/guide/testing-mocks) covers `[Mock]`.

```csharp
using DependencyModules.xUnit.Attributes;
using Hardened.Web.Testing;
using NSubstitute;

namespace Todos.Tests;

public class TodoFailureTests
{
    [ModuleTest]
    public async Task GetTodo_StoreFails_IsAServerError(ITestWebApp app, [Mock] ITodoStore store)
    {
        var offline = new InvalidOperationException("The store is offline.");

        store.Find(1).Returns(Task.FromException<Todo?>(offline));

        var response = await app.Get("/todos/1");

        Assert.Equal(500, response.StatusCode);
        Assert.Equal("The store is offline.", response.Failure?.Message);
    }
}
```

| Case | Status | `Failure` |
|---|---|---|
| The handler throws | 500 | The exception that the handler threw |
| Validation or binding fails | 400 | A `ValidationException` |
| The JSON body is malformed | 400 | A `JsonException` |
| Authorization refuses the request | 401 or 403 | An `AuthorizationException` |
| The handler returns a status, such as `NotFound` or `Conflict` | The returned status | Null |
| No route matches the path | 404 | Null |

For a thrown exception, the 500's body is `{"type":"ServerError","message":"The server could not complete this request.","details":""}`. `Failure` is null on a socket host, where only the response crosses the wire.

## The pipeline host

The pipeline host runs the application's chain in the test's own call. It is the chain that `app.UseHardened()` and the Kestrel host run. A path with no route answers 404, including in an application that names `[AspNetCoreRuntime]`.

On the pipeline host, each request runs against a container of its own. [Writing a test](/guide/testing) covers the container per request and `[Shared]`. `RootServiceProvider` on `ITestWebApp` is the test's container, not a request's. A service read from it after a request does not show what the request changed, unless the parameter carries `[Shared]`.

Kestrel, ASP.NET Core middleware outside Hardened and TLS do not run. [Test hosts](/guide/testing-hosts) covers running a test over a socket.

## Next

| Page | Covers |
|---|---|
| [Typed clients](/guide/testing-clients) | A generated client as a test parameter, and `Returns<T>()` |
| [Substituting services](/guide/testing-mocks) | A substitute behind a route |
| [Test hosts](/guide/testing-hosts) | The same requests over a socket |
| [Authorization](/guide/authorization) | The grants a request is judged against |
