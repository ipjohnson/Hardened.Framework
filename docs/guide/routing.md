# Routing

A route is an attribute on a method of a plain class. There is no base type to inherit and no
registration — the generator finds the attribute during the build and emits a handler bound to that
method.

```csharp
using Hardened.Web.Runtime.Attributes;

public class HomeController {
    [Get("/")]
    public string HelloWorld() => "Hello World";

    [Post("/hello")]
    public Task HelloWorldAsync() => Task.CompletedTask;
}
```

The class name means nothing. `Controller` is a convention, and the class is never registered as a
service — the generator instantiates it through the container when a request arrives.

## The route attributes

| Attribute | Method |
|---|---|
| `[Get(path)]` | `GET` |
| `[Post(path)]` | `POST` |
| `[Put(path)]` | `PUT` |
| `[Delete(path)]` | `DELETE` |
| `[Patch(path)]` | `PATCH` |

All five behave identically — same path templates, same binding, same filters:

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

### HEAD, and 405

`HEAD` is `GET` without a body, so every `[Get]` route answers it. The handler, its filters and its
serializer all run, and the bytes are counted and dropped rather than written, so `Content-Length`
is the real number.

A request whose path matches a route but whose verb has none gets `405 Method Not Allowed` with an
`Allow` header listing the verbs that path does answer, `HEAD` included. A path nobody declared is
`404`.

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

A value the parameter's type cannot take — `/double/abc` — reaches the handler's binder and comes
back `400`.

### Constraining what a token matches

`{name:int}` matches only a segment that passes the test, and rejects it before any filter or binder
runs:

```csharp
[Get("/users/{id:int}")]
public User ById(int id) => _users.Find(id);
```

`/users/abc` is now a `404` rather than a `400`.

| Constraint | Matches | Rank |
|---|---|---|
| `guid` | a GUID in any of the forms `Guid.TryParse` accepts | 10 |
| `date` | an ISO 8601 date — `yyyy-MM-dd` | 15 |
| `datetime` | an ISO 8601 date and time | 15 |
| `bool` | `true` or `false` | 20 |
| `int` | a 32-bit integer | 30 |
| `long` | a 64-bit integer | 35 |
| `decimal` | a decimal number | 40 |
| `hex` | `^[0-9a-fA-F]+$` — a hash, a sha, a request id | 50 |
| `alpha` | `^[A-Za-z]+$` | 60 |
| `slug` | `^[a-z0-9]+(-[a-z0-9]+)*$` | 70 |

Parsing is invariant, so the same request matches on every machine. `date` and `datetime` accept
ISO 8601 only. A slug is a canonical form: a leading, trailing or doubled hyphen does not match, and
neither does upper case.

**Rank** decides which constraint is tried first where two could match the same segment — lower is
narrower.

**Use `:int` when the segment is an identifier and a wrong value means "no such URL". Leave it off
when the value is input being validated and `400` is the honest answer.**

### Declaring your own constraint

```csharp
[RouteConstraint("isbn")]
public static bool IsIsbn(ReadOnlySpan<char> value) => …
```

and then `[Get("/books/{code:isbn}")]`. The generator emits a direct static call — no allocation, no
reflection, nothing to look up per request. A declared constraint ranks 90, after every built-in.

For a shape a character loop cannot express, use `[GeneratedRegex]`:

```csharp
[RouteConstraint("isbn")]
public static bool IsIsbn(ReadOnlySpan<char> value) => Isbn().IsMatch(value);

[GeneratedRegex(@"^\d{13}$")]
private static partial Regex Isbn();
```

The regex is compiled at build time and `IsMatch(ReadOnlySpan<char>)` allocates nothing. There is no
`{code:regex(...)}` form.

A method that is not a `static bool(ReadOnlySpan<char>)` is a build error, and so is a constraint
name nothing declares.

### Two routes that differ only by constraint

```csharp
[Get("/users/{id:int}")]   // and
[Get("/users/{name}")]     // -> HRDR001, a build error
```

Which handler a request reaches would depend on the *content* of the value, and the pair is
unrepresentable in OpenAPI. The same applies to `{name}` beside `{*name}`. A literal beside a
token — `/users/me` and `/users/{id}` — is fine.

Override it per file:

```ini
# .editorconfig
dotnet_diagnostic.HRDR001.severity = warning
```

`<HardenedAmbiguousRoutes>warning</HardenedAmbiguousRoutes>` sets the default for a project. Prefer
`warning` over `none` — CI runs `TreatWarningsAsErrors`, so an opt-in stays a deliberate decision.

### Brace forms that are not supported

`{id?}` and `{id=5}` are build errors. For a default, give the C# parameter one. For an optional
segment, declare the two paths as two routes.

Token names belong to the route, not to the position, so two routes may share a prefix and still
name their tokens differently — `/users/{id}` alongside `/users/{userId}/posts/{postId}` binds
correctly in both. The name is what binds, whatever else the token carries: `{*path}` and `{id:int}`
bind to parameters called `path` and `id`.

### How much a token matches

**A token matches exactly one segment.** `/users/{id}` answers `/users/42` and not `/users/42/posts`
— a path deeper than the route declares returns 404.

**And exactly one segment means at least one character.** `/users/` does not answer `/users/{id}`,
because the empty string after a trailing slash is not a segment. Neither is the nothing between the
two slashes of `//`.

### Matching the rest of the path

Prefix a token with `*` to take everything that remains, separators included. It has to be the last
token in the route:

```csharp
[Get("/assets/{*path}")]
public Stream Asset(string path) => _files.Open(path);   // /assets/img/logo.png -> "img/logo.png"
```

A literal in the same position still wins, so `/assets/index` reaches an `[Get("/assets/index")]`
handler if one exists.

A catch-all cannot be written in an OpenAPI document, so a route generated from a specification is
always single-segment, and a document generated from a `{*path}` route describes it as `{path}`.

## Case and trailing slashes

**A path is matched as written.** `/Orders` and `/orders` are different URLs.

```csharp
[HardenedModule]
[CaseInsensitiveRoutes]          // match without regard to case
public partial class Application { }
```

`/orders` and `/orders/` are also different URLs, and strict is the default. One knob changes that
for a module:

```csharp
services.Configure<WebRoutingConfiguration>(config =>
    config.TrailingSlash = TrailingSlash.Redirect);
```

| Setting | A request for the other spelling gets |
|---|---|
| `Strict` | whatever it would have got: usually a 404 |
| `Normalise` | the route, with no difference visible to the client |
| `Redirect` | `308 Permanent Redirect` to the declared path |

`308` rather than `301`, so the method and body survive the redirect.

## Prefixing with `[BasePath]`

`[BasePath]` on the class prefixes every route in it:

```csharp
[BasePath("/binding")]
public class BindingController {
    [Get("/path/{id}")]        // → /binding/path/{id}
    public string FromPath(string id) => id;
}
```

A route of `/` is the base path itself, which is how a collection is served from its own address:

```csharp
[BasePath("/orders")]
public class OrderController {
    [Get("/")]                 // → /orders
    public IReadOnlyList<Order> List() => _orders.All();

    [Post("/")]                // → /orders
    public Order Create(OrderModel model) => _orders.Add(model);

    [Get("/{id}")]             // → /orders/{id}
    public Order ById(string id) => _orders.Find(id);
}
```

A trailing slash on a real segment is kept — `[Get("/items/")]` is `/orders/items/`. `/` alone means
"no segment of my own".

Applied to the assembly, `[BasePath]` prefixes every route in that assembly, which is how a
[library module](/guide/modules#splitting-an-application-into-libraries) owns its own URL space:

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

So a lookup is a 404 without an `if`, and a delete of something already gone is a 200. A 404 is
logged at information level with the method and path.

::: tip `SuccessStatus` names the success
Every verb attribute declares `SuccessStatus`, the status a successful response answers with.
`[Post("/todos", SuccessStatus = 201)]` answers 201 and publishes it in
[the OpenAPI document](/guide/openapi-document). Unset means 200. The framework writes no body for
204, 205 or 304 whatever the handler returned.

The `NullReturnStatus`, `ValidationErrorStatus` and `ErrorStatus` properties the attributes once
carried are gone; the table above is what decides a `null` return's status. To control an error
status, set `context.Response.Status` from an
[execution filter](/guide/execution-pipeline), or declare the set with
[a response type](/guide/responses).
:::

## Response shape

`[RawResponse]` writes the return value to the body without serialising it:

```csharp
[Get("/robots.txt")]
[RawResponse("text/plain")]
public string Robots() => "User-agent: *\nDisallow:";
```

`[Output<T>]` hands the response to something that writes it — a view, most often. See
[Views](/guide/templates):

```csharp
[Get("/orders/{id}")]
[Output<Views.OrderDetail>]
public OrderModel Order(string id) => _repository.Get(id);
```

Declaring an output takes the response out of negotiation: the output either answers what the client
asked for, or the request gets `406`.

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

`[AspNetCoreRuntime]` brings `[HardenedWebModule]` with it, and so does `[KestrelRuntime]`. A
library that carries routes but is not the host imports `[HardenedWebModule]` directly, which is
also what the [Lambda web runtime](/aws/lambda-web) sits on. Declaring the module yourself as well
is harmless — modules deduplicate by equality.

## Links to your own routes

Every module gets two generated types built from the routes it declares:

```csharp
ApplicationRoutes.Orders.Order("42")        // "/orders/42" — the path, from anywhere
_links.Orders.Order("42")                   // what a client should call
_links.Orders.OrderAbsolute("42")           // with a scheme and host, for a Location header
```

The names come from the controller and the method, so a rename is a compile error at every call
site — including inside a view, where RazorBlade compiles `@` expressions at build time:

```razor
<a href="@Links.Orders.Order(Model.Id)">@Model.Reference</a>
```

`ApplicationLinks` is in the container, so a handler can take it as a constructor parameter. It
resolves through an `ILinkContext`, which is what keeps a link correct on a host that strips a
prefix before the application sees the path, such as API Gateway's stage:

```csharp
services.Configure<LinkConfiguration>(config => {
    config.BasePath = "/prod";
    config.Scheme = "https";
    config.Host = "api.example.com";
});
```

## Next

- [Parameter binding](/guide/parameter-binding) — where each argument comes from
- [The execution pipeline](/guide/execution-pipeline) — filters around the handler
- [Testing web handlers](/guide/testing-web) — driving these routes from a test
