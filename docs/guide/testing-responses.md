# Asserting a response

`Returns<T>()` asserts a call through a client by naming the response type the contract declares.
The status, the body type and the headers that status carries are one word.

```csharp
var created = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" })
    .Returns<Created<ClientModels.Todo>>();

Assert.Equal("ship it", created.Value.Title);
Assert.Equal($"/todos/{created.Value.Id}", created.Location);

var missing = await client.Todos[9999].GetAsync().Returns<NotFound<ClientModels.NotFound>>();

Assert.Contains("9999", missing.Body.Detail);

await client.Todos[2].DeleteAsync().Returns<NoContent>();
```

The types are `Hardened.Requests.Abstract.Responses`, the same ones a handler returns. A test and
the handler it exercises use one word for one answer.

## What Returns checks

`Returns<T>()` is an extension on the call's `Task`, in `Hardened.Web.Testing`. It awaits the call
and reads what came back through the route the assembly named: for Kiota, the returned model or
the exception the client threw; for Refit, the `IApiResponse<T>` envelope. It checks the status
against `T`'s own, builds `T` from the body and the headers, and hands it back. A different status
fails naming both, in the contract's words:

```
Expected 404 (NotFound<NotFound>), the call was answered 200 carrying a Todo.
```

`T` is a type that says what the body is, or one that says there is none:

| Expectation | Status | Carries |
|---|---|---|
| `Ok<T>` | 200 | `Value` |
| `Created<T>` | 201 | `Value`, `Location` |
| `NoContent` | 204 | nothing |
| `BadRequest<T>` | 400 | `Body` |
| `NotFound<T>` | 404 | `Body` |
| `Conflict<T>` | 409 | `Body` |

Every status in the [built-in response types](/guide/responses#the-built-in-response-types) has
its `<T>` form here, and the bodiless ones (`Accepted`, `NoContent`, `NotModified`,
`NotAcceptable`) are expectations as they are.

## ReturnsStatus

`ReturnsStatus<T>()` checks the status alone and returns nothing:

```csharp
await client.Todos[9999].GetAsync().ReturnsStatus<NotFound>();
await nobody.Pets.GetAsync().ReturnsStatus<Unauthorized>();
```

It is for a refusal the document declares no body for, where Kiota throws its bare
`ApiException`, and for a type that states something the wire does not carry back: the bare
`NotFound` names the resource, and `Conflict` its detail.

Under the throws response model the document describes only the success, so a 404 the handler
throws is undeclared and `ReturnsStatus<NotFound>()` is the assertion. Under `Response` or `Union`
the same call is `Returns<NotFound<ClientModels.NotFound>>()`. See
[Declared responses](/guide/responses).

## The last response

A client library returns the body and throws for a refusal. What it does not surface is the
response it did not throw on: the 201 and its `Location`, a 204, an `ETag`. `LastResponse` is the
most recent response the pipeline answered inside the current test, whether it went out through
a client or through `app.Get`:

```csharp
[HardenedTest]
public async Task CreateTodo_AnswersCreated(TodosClient client) {
    var todo = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" });

    Assert.Equal(201, LastResponse.Status);
    Assert.Equal($"/todos/{todo!.Id}", LastResponse.Headers["Location"]);
}
```

`LastResponse` carries `Status`, `Headers`, `ContentType` and `Body` as bytes. It is keyed on the
runner's current test, so parallel tests read their own answers. Reading it before anything was
answered fails naming the test, and `IsAvailable` says whether there is one. On a socket host it
is what came back over the wire.

`Returns<T>()` is the first choice. `LastResponse` is for a test written without a response type.

## The failure behind a 500

A request through `ITestWebApp` answers the error envelope when the handler throws, and the
envelope says nothing about the cause. `TestWebResponse.Failure` is the exception the pipeline
recorded:

```csharp
var response = await app.Get("/orders/broken");

Assert.Equal(500, response.StatusCode);
Assert.IsType<InvalidOperationException>(response.Failure);
```

It is null on a [socket host](/guide/testing-hosts), where only the envelope crosses the wire.

## Next

- [Typed clients](/guide/testing-clients): where the call comes from
- [Declared responses](/guide/responses): the response types a handler returns
- [Generated clients](/guide/clients): what the document declares, and what Kiota makes of it
