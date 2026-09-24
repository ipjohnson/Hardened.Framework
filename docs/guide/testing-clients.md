# Typed clients

A test method can take a client generated from the application's OpenAPI document as a parameter. The
client sends its requests through an `HttpClient` whose handler runs the application's pipeline in
process, with no socket.

```csharp
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Runtime.Responses;
using Hardened.Web.Testing;
using Todos.Client;

namespace Todos.Tests;

public class TodoClientTests
{
    [HardenedTest]
    public async Task GetTodo_ReturnsTheTodo(TodosClient client)
    {
        var todo = await client.Todos[1].GetAsync().Returns<Ok<ClientModels.Todo>>();

        Assert.Equal("Read the generated code", todo.Value.Title);
    }
}
```

The class is in `tests/Todos.Tests`, the test project that `dotnet new hardened-web -n Todos` creates.
`[assembly: KiotaTesting]` in the template's `Bootstrap.cs` builds `TodosClient` for the test.
`Returns<Ok<ClientModels.Todo>>()` asserts that the call answered 200 with a `Todo`. It returns the
`Ok<T>`, whose `Value` is the body. [Generated clients](/guide/clients) covers how the template
generates `TodosClient`.

## Client testing packages

Kiota and Refit clients each have a testing package and an assembly attribute:

| Package | Assembly attribute | Builds |
|---|---|---|
| `Hardened.Kiota.Testing` | `[KiotaTesting]`, namespace `Hardened.Kiota.Testing` | A class deriving from Kiota's `BaseRequestBuilder` with a public constructor taking one `IRequestAdapter` |
| `Hardened.Refit.Testing` | `[RefitTesting]`, namespace `Hardened.Refit.Testing` | An interface with a method carrying a Refit verb attribute, such as `[Get]`, on itself or on an interface it extends |

Once the test assembly declares an attribute, every client of the shape in its row is a test
parameter. No client needs code of its own. The template's test project references the package and
declares the attribute for the generator that `--client` chose. In the project from
`dotnet new hardened-web -n Todos`, the declaration is in `tests/Todos.Tests/Bootstrap.cs`:

```csharp
using Hardened.Kiota.Testing;

[assembly: KiotaTesting]
```

`[KiotaTesting]` builds each client with an anonymous authentication provider. It builds the client
over the test's `HttpClient`, with no Kiota retry or redirect handler. A 429 or a 3xx reaches the
client as the pipeline answered it. The client's `BaseUrl` is the host's address, so the client
reaches a socket host too.

`[RefitTesting]` builds the interface with `RestService.For` over the test's `HttpClient`. The client
has the default `RefitSettings`, which use System.Text.Json.

The two attributes can be declared in one test assembly. Each builds the clients of its own shape.
When one test calls both kinds of client, `Returns<T>()` depends on the order of the declarations, as
[Limits](#limits) describes.

A client parameter sends the credential in scope. [Sending requests](/guide/testing-web) covers
credentials.

## Client models

The generated models are named after the document's schemas. The schemas are named after the
application's own types. The template's test project references both the library and the client.
Each of them has a `Todo` type. The test project's `Usings.cs` aliases the client's namespace as
`ClientModels`:

```csharp
global using ClientModels = Todos.Client.Models;
```

In a test, `Todo` is the application's record. `ClientModels.Todo` is the client's model.

## Asserting a call

`Returns<T>()` is an extension method on `Task`, in `Hardened.Web.Testing`. It awaits the call and
reads the status, the body and the headers that the call was answered with. It returns a `T` built
from them.

Three more methods of `TodoClientTests` use it:

```csharp
[HardenedTest]
public async Task CreateTodo_AnswersCreatedWithALocation(TodosClient client)
{
    var created = await client
        .Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" })
        .Returns<Created<ClientModels.Todo>>();

    Assert.Equal("ship it", created.Value.Title);
    Assert.Equal($"/todos/{created.Value.Id}", created.Location);
}

[HardenedTest]
public async Task GetTodo_UnknownId_IsATypedNotFound(TodosClient client)
{
    var missing = await client
        .Todos[9999]
        .GetAsync()
        .Returns<NotFound<ClientModels.NotFound>>();

    Assert.Contains("9999", missing.Body.Detail);
}

[HardenedTest]
public async Task RemoveTodo_AnswersNoContent(TodosClient client)
{
    await client.Todos[2].DeleteAsync().Returns<NoContent>();
}
```

`T` is one of the response types in `Hardened.Web.Runtime.Responses`, which are the types a handler
returns. [Declared responses](/guide/responses) covers them. The status must be `T`'s status. The
body must be of `T`'s type argument. A header that `T` requires must be present.

`Returns<T>()` reads a refusal that the client throws, and does not rethrow it. A Kiota client throws
the generated model for a status that the document declares a body for. It throws `ApiException` for
a status with no declared body. `Returns<T>()` reads the status and the headers from the thrown
exception. `ClientModels.NotFound` is the model that Kiota generated for the 404's body. Kiota throws
it in the second test. `missing.Body` is that model.

A Kiota client returns a success as the body alone. `[KiotaTesting]` records the response that the
client received. `Returns<T>()` reads the status and the headers of a success from that response.

### Response types

`Returns<T>()` takes these types:

| Type | Status | Carries |
|---|---|---|
| `Ok<T>` | 200 | `Value`, and `Headers`, every response header |
| `Created<T>` | 201 | `Value`, and `Location`, which must be present |
| `Accepted` | 202 | `Location`, when present |
| `NoContent` | 204 | Nothing |
| `NotModified` | 304 | `ETag`, when present |
| `BadRequest<T>` | 400 | `Body` |
| `Unauthorized<T>` | 401 | `Body`, and `Challenge`, parsed from `WWW-Authenticate` when present |
| `PaymentRequired<T>` | 402 | `Body` |
| `Forbidden<T>` | 403 | `Body` |
| `NotFound<T>` | 404 | `Body` |
| `MethodNotAllowed` | 405 | `Allow`, which must be present |
| `MethodNotAllowed<T>` | 405 | `Body`, and `Allow`, which must be present |
| `NotAcceptable` | 406 | Nothing |
| `RequestTimeout<T>` | 408 | `Body` |
| `Conflict<T>` | 409 | `Body` |
| `Gone<T>` | 410 | `Body` |
| `PreconditionFailed<T>` | 412 | `Body` |
| `ContentTooLarge<T>` | 413 | `Body` |
| `UnsupportedMediaType<T>` | 415 | `Body` |
| `UnprocessableContent<T>` | 422 | `Body` |
| `PreconditionRequired<T>` | 428 | `Body` |
| `RateLimited<T>` | 429 | `Body`, and `RetryAfter`, from `Retry-After`, which must be present |
| `InternalServerError<T>` | 500 | `Body` |
| `NotImplemented<T>` | 501 | `Body` |
| `BadGateway<T>` | 502 | `Body` |
| `ServiceUnavailable<T>` | 503 | `Body`, and `After`, from `Retry-After` when present |
| `GatewayTimeout<T>` | 504 | `Body` |
| `Status<TCode, TBody>` | The status `TCode` names | `Body` |
| `Status<TCode>` | The status `TCode` names | Nothing |

`Returns<T>()` reads `Retry-After` as a whole number of seconds. An HTTP date there fails the
assertion. `TCode` is a status marker such as `Http.Locked`. [Declared responses](/guide/responses)
lists the markers.

The problem types without a type argument, such as `NotFound` and `Conflict`, are not in the table.
`Returns<NotFound>()` fails to compile with error `CS0311`.

### Failure messages

A failed assertion throws `InvalidOperationException`. The message depends on what the call was
answered with:

| Answered | Message |
|---|---|
| Another status | `Expected 200 (Ok<Todo>), the call was answered 404 carrying a NotFound.` |
| A body of another model | `The response declares a body of Conflict and carried NotFound. The client deserialised this status into a different model than the one the expectation names.` |
| No body where `T` declares one | `The response declares a body of RequestValidationError and carried none.` |
| No required header | `The response declares a Location header and carried none. Present: ` followed by the header names |

The status in the first message is `T`'s, with `T` written as in source. That message names the body
that the call carried, or ends `with no body`. When Kiota throws a bare `ApiException`, the message
adds `The client threw a bare ApiException rather than a model, which is what it does for a status the document declares no body for - so there was nothing for it to deserialise into, whatever the response carried.`

## Asserting the status alone

`ReturnsStatus<T>()` checks the status and returns nothing. It takes every response type in
`Hardened.Web.Runtime.Responses`, including `NotFound`, `Conflict` and the other problem types
without a type argument:

```csharp
[HardenedTest]
public async Task GetTodo_UnknownId_IsNotFound(TodosClient client)
{
    await client.Todos[9999].GetAsync().ReturnsStatus<NotFound>();
}
```

`ReturnsStatus<T>()` is the assertion for a status that the document declares no body for, where
Kiota throws a bare `ApiException`. A different status fails it with the same message that
`Returns<T>()` gives, such as `Expected 404 (NotFound), the call was answered 200 carrying a Todo.`

## How a client is built

For a test parameter of a type it does not otherwise know, `[WebTesting]` tries three ways to build a
client, in this order:

| Order | Builds the client with |
|---|---|
| 1 | A public `ITestClientFactory<T>` for the type, declared in the test assembly |
| 2 | The attribute of a client testing package, such as `[KiotaTesting]` |
| 3 | The type's only public constructor, when it takes exactly one `HttpClient` |

A client written by hand with that constructor needs no testing package. `[WebTesting]` builds it
over the test's `HttpClient`.

A parameter type that none of the three builds fails the test. The message names all three. Without
`[assembly: KiotaTesting]`, a `TodosClient` parameter fails with
`Todos.Client.TodosClient cannot be built for a test parameter. None of the three routes applies: Todos.Tests declares no public ITestClientFactory<TodosClient>, it names no ITestClientRoute in a [assembly: TestClientRoute] attribute, and TodosClient has no single public constructor taking exactly one HttpClient. Add a factory - one class, one method from HttpClient to TodosClient - or reference the client generator's testing package and name its route in an assembly attribute.`

`app.CreateClient<T>()` builds a client the same way inside a test, from an `ITestWebApp`. It takes
an optional credential, which [Sending requests](/guide/testing-web) covers.

`[KiotaTesting]` and `[RefitTesting]` derive from `TestClientRouteAttribute`.
`[assembly: TestClientRoute(typeof(T))]` names an `ITestClientRoute` of your own. The route class
implements `CanBuild(Type)` and `Build(TestClientContext, Type)`. It needs a public parameterless
constructor.

## Client factories

`ITestClientFactory<TClient>`, in `Hardened.Web.Testing`, builds one client type. `[WebTesting]` uses
a public, non-abstract class in the test assembly that implements it and has a public parameterless
constructor. For its own client type, a factory takes precedence over the generator's attribute. The
attribute builds the rest.

`Create(HttpClient http)` receives the test's `HttpClient`, which already sends the credential.
`[WebTesting]` calls `Create(TestClientContext context)`. By default, that method calls
`Create(context.Http)`.

`TestClientContext` has `Http`, `BaseAddress` and `CreateHttpClient(params DelegatingHandler[] handlers)`.
`CreateHttpClient` builds an `HttpClient` whose requests pass through the handlers in order, then the
host, with the credential applied. To put a handler in front of the pipeline, pass it to
`CreateHttpClient` in an override of `Create(TestClientContext)`.

This factory, in `tests/Todos.Tests/TodosClientFactory.cs`, puts a handler in front of the pipeline:

```csharp
using Hardened.Web.Testing;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Todos.Client;

namespace Todos.Tests;

public class TodosClientFactory : ITestClientFactory<TodosClient>
{
    public TodosClient Create(HttpClient http) =>
        new(new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: http));

    public TodosClient Create(TestClientContext context) =>
        Create(context.CreateHttpClient(new TenantHandler("acme")));
}

public class TenantHandler(string tenant) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        request.Headers.Add("X-Tenant", tenant);

        return base.SendAsync(request, cancellationToken);
    }
}
```

Every request from the factory's clients carries `X-Tenant: acme`. `HttpClientRequestAdapter` takes
the `HttpClient`'s address, so the factory sets no `BaseUrl`. The two Kiota packages that the factory
uses come with `Hardened.Kiota.Testing`. The template's test project needs no other reference.

`Returns<T>()` and `ReturnsStatus<T>()` read a success from a Kiota client that `[KiotaTesting]`
built. For a Kiota client from a factory, they read a refusal only. A success from such a client fails
with
`Returns<Ok<Todo>>() has no route that read this call, which returned a Todo. A Kiota client built through [assembly: KiotaTesting] records what it receives, and no call through one has been answered in this test; a client built by an ITestClientFactory of the test project's own, or constructed by hand, is not recorded.`
`LastResponse` still holds such a success. [Sending requests](/guide/testing-web) covers it.

`Returns<T>()` reads every status from a Refit client that a factory built. With `--client refit` and
a MessagePack serializer, the template writes `MessagePackClientFactory`, a factory of this kind.
[MessagePack](/guide/message-pack) covers it.

## The underlying `HttpClient`

A test parameter of type `HttpClient` and `app.CreateHttpClient()` both give a plain `HttpClient` over
the handler that the typed clients send through. The client sends the credential in scope as default
headers.

This method of `TodoClientTests` takes one as a parameter, with `using System.Net.Http.Json;` added to
the file:

```csharp
[HardenedTest]
public async Task GetTodo_OverAnHttpClient(HttpClient http)
{
    var todo = await http.GetFromJsonAsync<Todo>(
        "/todos/1",
        TestContext.Current.CancellationToken
    );

    Assert.Equal("Read the generated code", todo!.Title);
}
```

The client's handler, `PipelineHttpMessageHandler`, turns the request's method, path, headers,
cookies and body into a pipeline request. The handler turns the answer into an `HttpResponseMessage`
with the status, the headers, `Set-Cookie` and the body. The client's `BaseAddress` is
`http://harness/`. The handler ignores the scheme and the host. The path is decoded as
[Sending requests](/guide/testing-web) describes. The client adds no `Accept-Encoding`, so a response
is compressed only when the request asks for it.

## Refit clients

`Returns<T>()` reads every status from a Refit method that returns `Task<IApiResponse<T>>` or
`Task<IApiResponse>`. The template's `.refitter` sets `returnIApiResponse`, which writes every
operation that way. [Generated clients](/guide/clients) covers the file.

This is `tests/Todos.Tests/TodoRefitTests.cs` in a project from
`dotnet new hardened-web -n Todos --client refit`:

```csharp
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Runtime.Responses;
using Hardened.Web.Testing;
using Todos.Client;

namespace Todos.Tests;

public class TodoRefitTests
{
    [HardenedTest]
    public async Task GetTodo_ReturnsTheTodo(ITodosClient client)
    {
        var todo = await client.GetTodo(1).Returns<Ok<ClientModels.Todo>>();

        Assert.Equal("Read the generated code", todo.Value.Title);
    }

    [HardenedTest]
    public async Task GetTodo_UnknownId_IsATypedNotFound(ITodosClient client)
    {
        var missing = await client.GetTodo(9999).Returns<NotFound<ClientModels.NotFound>>();

        Assert.Contains("9999", missing.Body.Detail);
    }
}
```

For a method that returns `Task<T>`, `Returns<T>()` reads a refusal from the `ApiException` that
Refit throws. A success fails, because the call returned the body alone. The failure message ends
with
`A Refit method declared Task<IApiResponse<T>> returns an envelope that carries the status and the headers, and this call returned the body alone; Refitter's --use-api-response declares the envelope.`

Refit hands over an error body as text. `Returns<T>()` reads it as `T`'s body type through the
client's own `RefitSettings`. When the body cannot be read as that type, the assertion fails with
`The 404 body could not be read as String through the client's serializer: ` and the serializer's
message. A client that needs its own `RefitSettings`, such as another serializer, is built with a
factory, as in [Client factories](#client-factories).

## Requests a client cannot send

A client makes the requests that the document allows. A request that the document does not allow,
such as a string where the path takes an `int`, goes through `ITestWebApp`.
[Sending requests](/guide/testing-web) covers it. This method of `TodoClientTests` sends one:

```csharp
[HardenedTest]
public async Task GetTodo_MalformedId_IsBadRequest(ITestWebApp app)
{
    (await app.Get("/todos/not-a-number")).Assert.BadRequest();
}
```

The template's `TodoTests.cs` has the same test.

## Limits

`Returns<T>()` reads a Kiota success from the last response that a `[KiotaTesting]` client received in
the test. Await each call before the next.

With `[KiotaTesting]` declared before `[RefitTesting]`, a Refit call after a Kiota call in the same
test is read with the Kiota call's status. The assertion on the Refit call reports that status, as in
`Expected 201 (Created<TodoDto>), the call was answered 200 carrying a ApiResponse<TodoDto>.` With
`[RefitTesting]` declared first, both calls are read correctly.

`[Shared]` does not send a client's requests to one container when `[KiotaTesting]` builds the
client, or when a factory builds it with `context.CreateHttpClient`. On an `HttpClient`, a Refit
interface or a client with an `HttpClient` constructor, `[Shared]` does send them to one container.
[Writing a test](/guide/testing) covers `[Shared]`.

## Next

- [Sending requests](/guide/testing-web): `ITestWebApp`, credentials and `LastResponse`
- [Generated clients](/guide/clients): how the template generates the client
- [Declared responses](/guide/responses): the response types a handler returns
- [Test hosts](/guide/testing-hosts): the same clients over a socket
