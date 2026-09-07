# Routing

A route is an attribute on a method of a plain class. There is no base type and no registration.
The generator finds the attribute during the build and emits a handler bound to that method.

```csharp
using Hardened.Web.Runtime.Attributes;

[BasePath("/orders")]
public class OrderController {
    [Get("/")]                          // GET /orders
    public IReadOnlyList<Order> List() => _orders.All();

    [Get("/{id:int}")]                  // GET /orders/42
    public Order ById(int id) => _orders.Find(id);

    [Post("/", SuccessStatus = 201)]    // POST /orders, answers 201
    public Order Create(OrderModel model) => _orders.Add(model);
}
```

The class name means nothing. `Controller` is a convention, and the class is never registered as
a service. The generator instantiates it through the container when a request arrives.

## The route attributes

| Attribute | Method |
|---|---|
| `[Get(path)]` | `GET` |
| `[Post(path)]` | `POST` |
| `[Put(path)]` | `PUT` |
| `[Delete(path)]` | `DELETE` |
| `[Patch(path)]` | `PATCH` |

All five behave the same: the same path templates, the same binding, the same filters. Two routes
may share a path under different verbs, so `GET /orders/{id}` and `DELETE /orders/{id}` reach
different handlers.

Every verb attribute has `SuccessStatus`, the status a successful response answers with and the
[OpenAPI document](/guide/openapi-document) publishes. Unset means 200. The framework writes no
body for 204, 205 or 304 whatever the handler returned.

### HEAD, and 405

`HEAD` is `GET` without a body, so every `[Get]` route answers it. The handler, its filters and
its serializer all run. The bytes are counted and dropped rather than written, so
`Content-Length` is the real number.

A request whose path matches a route but whose verb has none gets `405 Method Not Allowed`, with
an `Allow` header listing the verbs the path does answer, `HEAD` included. A path nobody declared
is `404`.

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

A value the type cannot take, such as `/double/abc`, reaches the handler's binder and comes back
`400`.

### Constraining what a token matches

`{id:int}` matches only a segment that passes the test, and rejects the rest before any filter or
binder runs:

```csharp
[Get("/users/{id:int}")]
public User ById(int id) => _users.Find(id);
```

`/users/abc` is now a `404` rather than a `400`. Use a constraint when the segment is an identifier
and a wrong value means "no such URL". Leave it off when the value is input being validated and
`400` is the honest answer.

| Constraint | Matches |
|---|---|
| `guid` | a GUID in any form `Guid.TryParse` accepts |
| `date` | an ISO 8601 date, `yyyy-MM-dd` |
| `datetime` | an ISO 8601 date and time |
| `bool` | `true` or `false` |
| `int` | a 32-bit integer |
| `long` | a 64-bit integer |
| `decimal` | a decimal number |
| `hex` | `^[0-9a-fA-F]+$`: a hash, a sha, a request id |
| `alpha` | `^[A-Za-z]+$` |
| `slug` | `^[a-z0-9]+(-[a-z0-9]+)*$`. A leading, trailing or doubled hyphen does not match, and neither does upper case |

Parsing is invariant, so the same request matches on every machine. Where two constraints could
match one segment, the narrower is tried first, in the order of the table.

### Declaring your own constraint

```csharp
[RouteConstraint("isbn")]
public static bool IsIsbn(ReadOnlySpan<char> value) => Isbn().IsMatch(value);

[GeneratedRegex(@"^\d{13}$")]
private static partial Regex Isbn();
```

Then `[Get("/books/{code:isbn}")]`. The generator emits a direct static call, so there is no
allocation and no lookup per request. A declared constraint is tried after every built-in.

A `[GeneratedRegex]` is compiled at build time and `IsMatch(ReadOnlySpan<char>)` allocates
nothing. There is no `{code:regex(...)}` form. A method that is not a
`static bool(ReadOnlySpan<char>)` is a build error, and so is a constraint name nothing declares.

### Two routes that differ only by constraint

```csharp
[Get("/users/{id:int}")]   // and
[Get("/users/{name}")]     // HRDR001, a build error
```

Which handler a request reaches would depend on the content of the value, and the pair cannot be
written in an OpenAPI document. The same applies to `{name}` beside `{*name}`. A literal beside a
token, `/users/me` and `/users/{id}`, is fine.

`<HardenedAmbiguousRoutes>warning</HardenedAmbiguousRoutes>` allows it for a project, and
`dotnet_diagnostic.HRDR001.severity = warning` in `.editorconfig` for a file. Prefer `warning`
over `none`, so that under `TreatWarningsAsErrors` the opt-in stays a deliberate decision.

### How much a token matches

A token matches exactly one segment of at least one character. `/users/{id}` answers `/users/42`.
It does not answer `/users/42/posts`, which is deeper than the route declares, and it does not
answer `/users/`, because the empty string after a trailing slash is not a segment. Neither is the
nothing between the two slashes of `//`.

`{id?}` and `{id=5}` are build errors. For a default, give the C# parameter one. For an optional
segment, declare two routes.

Token names belong to the route, not to the position, so `/users/{id}` and
`/users/{userId}/posts/{postId}` bind correctly side by side. The name is what binds, whatever
else the token carries: `{*path}` and `{id:int}` bind to parameters named `path` and `id`.

### Matching the rest of the path

Prefix the last token with `*` to take everything that remains, separators included:

```csharp
[Get("/assets/{*path}")]
public Stream Asset(string path) => _files.Open(path);   // /assets/img/logo.png -> "img/logo.png"
```

A literal in the same position still wins, so `/assets/index` reaches an `[Get("/assets/index")]`
handler if one exists.

A catch-all cannot be written in an OpenAPI document. A route generated from a specification is
always single-segment, and a document generated from a `{*path}` route describes it as `{path}`.

## Case and trailing slashes

A path is matched as written. `/Orders` and `/orders` are different URLs:

```csharp
[HardenedModule]
[CaseInsensitiveRoutes]          // match without regard to case
public partial class Application { }
```

`/orders` and `/orders/` are also different URLs, and strict is the default:

```csharp
services.Configure<WebRoutingConfiguration>(config =>
    config.TrailingSlash = TrailingSlash.Redirect);
```

| Setting | A request for the other spelling gets |
|---|---|
| `Strict` | whatever it would have got, usually a 404 |
| `Normalise` | the route, with no difference visible to the client |
| `Redirect` | `308 Permanent Redirect` to the declared path. 308 rather than 301, so the method and body survive |

## Prefixing with `[BasePath]`

`[BasePath]` on the class prefixes every route in it. A route of `/` is the base path itself,
which is how a collection is served from its own address:

```csharp
[BasePath("/orders")]
public class OrderController {
    [Get("/")]                 // /orders
    public IReadOnlyList<Order> List() => _orders.All();

    [Get("/{id}")]             // /orders/{id}
    public Order ById(string id) => _orders.Find(id);

    [Get("/items/")]           // /orders/items/, the trailing slash kept
    public IReadOnlyList<Item> Items() => _items.All();
}
```

On the assembly, `[BasePath]` prefixes every route in that assembly, which is how a
[library module](/guide/modules#composing-modules) owns its URL space:

```csharp
[HardenedModule]
[HardenedWebModule]
[BasePath("/billing")]
public partial class BillingLibrary { }
```

## Return values and status codes

Return whatever the handler produces. A value is serialized to the response body. `Task` with no
result produces an empty body. `Task<T>` is awaited first.

### Returning `null`

| Method | Status for a `null` return |
|---|---|
| `GET`, `PUT` | `404` |
| `POST`, `DELETE`, `PATCH` and anything else | `200` |

So a lookup is a 404 without an `if`, and a delete of something already gone is a 200. A 404 is
logged at information level with the method and path.

To control an error status, set `context.Response.Status` from an
[execution filter](/guide/execution-pipeline), or declare the set with
[a response type](/guide/responses). The `NullReturnStatus`, `ValidationErrorStatus` and
`ErrorStatus` properties the verb attributes once carried are gone.

### Response shape

`[RawResponse]` writes the return value to the body without serializing it:

```csharp
[Get("/robots.txt")]
[RawResponse("text/plain")]
public string Robots() => "User-agent: *\nDisallow:";
```

`[Output<T>]` hands the response to a view:

```csharp
[Get("/orders/{id}")]
[Output<Views.OrderDetail>]
public OrderModel Order(string id) => _repository.Get(id);
```

Declaring an output takes the response out of negotiation. The output answers what the client
asked for, or the request gets `406`. See [Views](/guide/templates).

### Caching

`[CacheControl]` sets the response's cache headers and stores nothing:

```csharp
[Get("/static/rates")]
[CacheControl(MaxAge = 3600, Type = CacheControlEnum.MaxAge | CacheControlEnum.Public)]
public RateTable Rates() => _rates.Current;
```

To store the response on the server and serve it again without running the handler, see
[Response caching](/guide/response-caching). To answer a caller who already holds the response
with a 304, see [Conditional requests](/guide/conditional-requests).

## The web module

Routing needs `[HardenedWebModule]`. `[KestrelRuntime]`, `[AspNetCoreRuntime]` and
`[LambdaWebModule]` each bring it, so an application names its runtime and nothing else. A
library that carries routes and is not the host imports `[HardenedWebModule]` itself. Declaring it
twice is harmless.

Under ASP.NET Core the host also installs the middleware:

```csharp
var app = builder.Build();

app.UseHardened();

app.Run();
```

## Links to your own routes

Every module gets two generated types built from the routes it declares:

```csharp
ApplicationRoutes.Orders.Order("42")        // "/orders/42": the path, from anywhere
_links.Orders.Order("42")                   // what a client should call
_links.Orders.OrderAbsolute("42")           // with a scheme and host, for a Location header
```

The names come from the controller and the method, so a rename is a compile error at every call
site, including inside a view:

```razor
<a href="@Links.Orders.Order(Model.Id)">@Model.Reference</a>
```

`ApplicationLinks` is in the container, so a handler can take it as a constructor parameter. It
resolves through an `ILinkContext`, which keeps a link correct on a host that strips a prefix
before the application sees the path, such as API Gateway's stage:

```csharp
services.Configure<LinkConfiguration>(config => {
    config.BasePath = "/prod";
    config.Scheme = "https";
    config.Host = "api.example.com";
});
```

## Next

- [Parameter binding](/guide/parameter-binding): where each argument comes from
- [Declared responses](/guide/responses): more than one status in the signature
- [Sending requests](/guide/testing-web): driving these routes from a test
