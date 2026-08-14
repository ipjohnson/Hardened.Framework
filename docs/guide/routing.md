# Routing

A route is an attribute on a method of a plain class. There is no base type to inherit and no
registration to remember — the generator finds the attribute during the build and emits a handler
bound to that method.

```csharp
using Hardened.Web.Runtime.Attributes;

public class HomeController {
    [Get("/")]
    public string HelloWorld() => "Hello World";

    [Post("/hello")]
    public Task HelloWorldAsync() => Task.CompletedTask;
}
```

The class name means nothing. `Controller` is a convention, not a requirement, and the class is
never registered as a service — the generator instantiates it through the container when a request
arrives.

## The route attributes

| Attribute | Method |
|---|---|
| `[Get(path)]` | `GET` |
| `[Post(path)]` | `POST` |
| `[Put(path)]` | `PUT` |
| `[Delete(path)]` | `DELETE` |
| `[Patch(path)]` | `PATCH` |

The verb comes from the attribute's own name, so all five behave identically — same path templates,
same binding, same filters:

```csharp
[BasePath("/verbs")]
public class ItemController {
    [Get("/item/{id}")]
    public string GetItem(string id) => _items.Find(id);

    [Delete("/item/{id}")]
    public string DeleteItem(string id) => _items.Remove(id);

    [Patch("/item/{id}")]
    public string PatchItem(string id, PatchModel model) => _items.Apply(id, model);
}
```

Two routes may share a path under different verbs — `GET /verbs/item/{id}` and
`DELETE /verbs/item/{id}` reach different handlers.

## Path tokens

Braces mark a token, and the token binds to the parameter of the same name:

```csharp
[Get("/hello/{name}")]
public string Hello(string name) => $"Hello, {name}!";

[Get("/pair/{first}/{second}")]
public string Pair(string first, string second) => $"{first}:{second}";
```

Tokens arrive as strings and are converted to the parameter's declared type:

```csharp
[Get("/double/{count}")]
public int Double(int count) => count * 2;
```

Token names belong to the route, not to the position, so two routes may share a prefix and still
name their tokens differently — `/users/{id}` alongside `/users/{userId}/posts/{postId}` binds
correctly in both.

## Prefixing with `[BasePath]`

`[BasePath]` on the class prefixes every route in it:

```csharp
[BasePath("/binding")]
public class BindingController {
    [Get("/path/{id}")]        // → /binding/path/{id}
    public string FromPath(string id) => id;
}
```

Applied to the assembly, it prefixes every route in that assembly — which is what makes a
[library module](/guide/modules#splitting-an-application-into-libraries) able to own its own URL
space:

```csharp
[HardenedModule]
[HardenedWebModule]
[BasePath("/billing")]
public partial class BillingLibrary { }
```

## Return values and status codes

Return whatever the handler produces. A value is serialised to the response body; `Task` with no
result produces an empty body; `Task<T>` is awaited first.

```csharp
[Get("/test")]
public Task<string> TestValue() => Task.FromResult("value");
```

### Returning `null`

A handler that returns `null` produces a status chosen from the request's method:

| Method | Status for a `null` return |
|---|---|
| `GET` | `404` |
| `PUT` | `404` |
| `POST` | `200` |
| `DELETE` | `200` |
| Anything else, including `PATCH` | `200` |

So a lookup is a 404 without an `if`, and a delete of something already gone is a 200 rather than a
404 — which is the idempotent answer, and the one most clients want. A 404 is also logged at
information level with the method and path.

::: warning The status properties on the route attributes are not read
`[Get]`, `[Put]`, `[Delete]` and `[Patch]` each declare `SuccessStatus`, `NullReturnStatus`,
`ValidationErrorStatus` and `ErrorStatus`. The web generator does not currently read any of them —
it emits only the path and the method into the handler info, so the four properties compile and are
ignored, and the table above is what actually decides the status.

`[Post]` declares none of them, which is the same behaviour, stated honestly.

To control a status today, set `context.Response.Status` from an
[execution filter](/guide/execution-pipeline).
:::

## Response shape

Two attributes change what happens to the return value:

`[RawResponse]` writes it to the body without serialising, for handlers that produce their own
payload:

```csharp
[Get("/robots.txt")]
[RawResponse("text/plain")]
public string Robots() => "User-agent: *\nDisallow:";
```

`[Template]` renders the return value through a named template instead — see
[Templates](/guide/templates):

```csharp
[Get("/orders/{id}")]
[Template("order-detail")]
public OrderModel Order(string id) => _repository.Get(id);
```

## Caching

`[CacheControl]` sets the response's cache headers:

```csharp
[Get("/static/rates")]
[CacheControl(MaxAge = 3600, Type = CacheControlEnum.MaxAge | CacheControlEnum.Public)]
public RateTable Rates() => _rates.Current;
```

## Wiring routing into a host

Routing needs the web module, and the host needs the middleware. Under ASP.NET Core:

```csharp
[HardenedModule]
[AspNetCoreRuntime]
public partial class Application { }
```

```csharp
var app = builder.Build();

app.UseHardened();

app.Run();
```

`[AspNetCoreRuntime]` brings `[HardenedWebModule]` with it. A library that carries routes but is not
itself the host imports `[HardenedWebModule]` directly, which is also what the
[Lambda web runtime](/aws/lambda-web) sits on.

## Next

- [Parameter binding](/guide/parameter-binding) — where each argument comes from
- [The execution pipeline](/guide/execution-pipeline) — filters around the handler
- [Testing web handlers](/guide/testing-web) — driving these routes from a test
