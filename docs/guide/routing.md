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

### HEAD, and the verb that has no route

`HEAD` is `GET` without a body, so every `[Get]` route answers it. The handler, its filters and its
serializer all run exactly as they would for the `GET` — which is the point, because RFC 9110
requires a `HEAD` response to carry the header fields the `GET` would have carried. The bytes are
counted and dropped rather than written, so `Content-Length` is the real number.

A request whose path matches a route but whose verb has none gets `405 Method Not Allowed` with an
`Allow` header listing the verbs that path does answer, `HEAD` included. A path nobody declared is
still `404`. The distinction matters beyond tidiness: API Gateway and CloudFront cache the two
differently, and a generated client reads them differently.

**Changed 2026-08-15.** Both are new. `HEAD` used to match nothing, so every endpoint answered
`curl -I` with a 404, and a wrong verb on a real resource was indistinguishable from a URL that did
not exist.

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
back `400`. That is honest when the value is input being validated. When it is an identifier, and a
wrong value means "no such URL", say so with a constraint.

### Constraining what a token matches

`{name:int}` matches only a segment that passes the test:

```csharp
[Get("/users/{id:int}")]
public User ById(int id) => _users.Find(id);
```

`/users/abc` is now a `404` rather than a `400`. Both are defensible answers to a bad value, and
they say different things: `400` means you addressed a real endpoint incorrectly, `404` means there
is no resource at that URL. The constraint also rejects the value before any filter or binder runs.

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

Parsing is invariant, so the same request matches on every machine. `date` and `datetime` are ISO
8601 *only* — not `DateTime.TryParse`, which accepts a large and culture-sensitive grammar. A URL is
the same string in every locale, and a route that matched `12/06/2026` while disagreeing about which
number was the month would behave differently on different machines.

A slug is a canonical form, so a leading, trailing or doubled hyphen does not match, and neither does
upper case. Several URLs for one resource is the thing a slug exists to avoid.

**Rank** decides which constraint is tried first where two could match the same segment — lower is
narrower. The numbers are declared rather than derived, because this vocabulary mostly overlaps: a
lower-case GUID is a valid slug, `hex` overlaps both `int` and `alpha`, and `123` is a perfectly good
slug. An order you can look up beats one you have to work out.

The list is short on purpose. The rule is not "what can we convert" but "what can be tested on a
`ReadOnlySpan<char>` without allocating, and expressed in the contract" — a constraint runs on every
request that reaches the position it guards, including the ones it rejects, and a matching rule the
served document cannot describe is one a client has no way to know about.

**Use `:int` when the segment is an identifier and a wrong value means "no such URL". Leave it off
when the value is input being validated and `400` is the honest answer.**

### Declaring your own constraint

```csharp
[RouteConstraint("isbn")]
public static bool IsIsbn(ReadOnlySpan<char> value) => …
```

and then `[Get("/books/{code:isbn}")]`. The generator emits a direct static call — no allocation, no
reflection, no registry, nothing to look up per request.

A declared constraint ranks 90 — after every built-in — which is the answer that cannot make an
existing route unreachable when you add one.

For a shape a character loop cannot express, `[GeneratedRegex]` works here and costs nothing per
request:

```csharp
[RouteConstraint("isbn")]
public static bool IsIsbn(ReadOnlySpan<char> value) => Isbn().IsMatch(value);

[GeneratedRegex(@"^\d{13}$")]
private static partial Regex Isbn();
```

The regex is compiled at build time and `IsMatch(ReadOnlySpan<char>)` allocates nothing. There is no
`{code:regex(...)}` form, deliberately: a source generator cannot emit `[GeneratedRegex]` — its
output is not in the compilation the regex generator reads — so a regex written in a route template
could only fall back to a runtime `Regex`, which costs several hundred kilobytes on a native AOT
publish. Written here, in your own compilation, it is a normal compiled regex.

The signature is the rule rather than a preference, for the same reason the built-in list is short.
A method that is not a `static bool(ReadOnlySpan<char>)` is a build error, and so is a constraint
name nothing declares — a route that silently constrains nothing is the failure all of this exists
to prevent.

### Two routes that differ only by constraint

```csharp
[Get("/users/{id:int}")]   // and
[Get("/users/{name}")]     // -> HRDR001, a build error
```

Overloading by type makes which handler you reach depend on the *content* of a value. A user named
`12345` becomes unreachable, a client cannot reason about which endpoint it hit, caches cannot tell
the two apart, and the pair is unrepresentable in OpenAPI. The same applies to `{name}` beside
`{*name}`. A literal beside a token — `/users/me` and `/users/{id}` — is untouched.

The opinion can be overridden where it has to be:

```ini
# .editorconfig
dotnet_diagnostic.HRDR001.severity = warning
```

which is per file, so one legacy pair can be allowed in one controller without opening the gate
project-wide. `<HardenedAmbiguousRoutes>warning</HardenedAmbiguousRoutes>` sets the default for a
project. Prefer `warning` over `none`: silencing leaves no record that the codebase drifted, and CI
runs `TreatWarningsAsErrors`, so an opt-in still forces a deliberate decision.

### Brace forms that are not supported

`{id?}` and `{id=5}` are build errors. Neither was ever honoured — the whole brace body became the
token *name*, so `{id?}` was a mandatory segment called `id?` and bound nothing to `id`.

For a default, give the C# parameter one: the template controls matching, and C# supplies the value.
For an optional segment, declare the two paths as two routes.

Token names belong to the route, not to the position, so two routes may share a prefix and still
name their tokens differently — `/users/{id}` alongside `/users/{userId}/posts/{postId}` binds
correctly in both.

The name is what binds, whatever else the token carries: `{*path}` and `{id:int}` bind to parameters
called `path` and `id`.

### How much a token matches

**A token matches exactly one segment.** `/users/{id}` answers `/users/42` and not `/users/42/posts`
— a path deeper than the route declares is not a match, and returns 404.

That is what makes a route's shape mean something: an API serves the paths it declares and nothing
else, so a client can tell a real endpoint from a typo, and a generated OpenAPI document describes
the same set of paths the router accepts.

**And exactly one segment means at least one character.** `/users/` does not answer `/users/{id}`,
because the empty string after a trailing slash is not a segment. Neither is the nothing between
the two slashes of `//`.

**Changed 2026-08-16.** Both used to match, binding the token to `""`. The request reached the
handler's binder, which rejected the empty value and answered `400` — telling a client it had
addressed a real endpoint incorrectly, about a URL that addresses no endpoint at all. `404` is the
truthful answer, and the distinction matters beyond tidiness: API Gateway and CloudFront cache the
two differently, and a generated client reads them differently.

This is also why `{id?}` is a build error rather than a token that may match nothing — an optional
segment is two routes.

### Matching the rest of the path

Prefix a token with `*` to take everything that remains, separators included. It has to be the last
token in the route:

```csharp
[Get("/assets/{*path}")]
public Stream Asset(string path) => _files.Open(path);   // /assets/img/logo.png -> "img/logo.png"
```

The asterisk says how much to match, not what to call it: `{*path}` binds to a parameter named
`path`. A literal in the same position still wins, so `/assets/index` reaches an `[Get("/assets/index")]`
handler if one exists.

A catch-all cannot be written in an OpenAPI document — a path template expression is a parameter
name and nothing more — so a route generated from a specification is always single-segment, and a
document generated from a `{*path}` route describes it as `{path}`.

**Changed 2026-08-15.** Every token used to match the rest of the path whether or not it was marked,
so `/users/{id}` accepted `/users/42/anything/at/all` and no route could describe a single segment.
A route that was relying on it needs the `*`.

## Case and trailing slashes

**A path is matched as written.** `/Orders` and `/orders` are different URLs, which is what
RFC 3986 says a path is and what an OpenAPI document describes — the format has no notion of a
case-insensitive path. It also removes the duplicate-URL problem that comes of one resource
answering at every spelling, and halves the matcher's work.

```csharp
[HardenedModule]
[CaseInsensitiveRoutes]          // the old behaviour, for URLs being tidied up rather than rewritten
public partial class Application { }
```

**Changed 2026-08-15.** Matching used to accept either case for every letter.

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

`308` rather than `301` because a redirect must not change the method, and most clients rewrite a
`301` on a `POST` to `GET`, silently dropping the body.

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

**Changed 2026-08-16.** The base path and the route were concatenated, so `[Get("/")]` under
`[BasePath("/orders")]` produced `/orders/` — and since a path is matched as written and strict is
the default, the collection did not answer at the address its own controller declares. The same
composition fed the generated links and the served OpenAPI document, so a generated client called
`/orders/` and a `@Links` expression built it, which is why nothing looked wrong from inside. A base
path written with a trailing slash also produced `//` in the middle of every route under it.

Both are now collapsed at the boundary. A trailing slash you write on a real segment is still
yours — `[Get("/items/")]` is `/orders/items/` — because that is a URL you chose; `/` alone means
"no segment of my own".

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

`[Output<T>]` hands the response to something that writes it — a view, most often — instead of
serialising it. See [Templates](/guide/templates):

```csharp
[Get("/orders/{id}")]
[Output<Views.OrderDetail>]
public OrderModel Order(string id) => _repository.Get(id);
```

Declaring one takes the response out of negotiation: the output either answers what the client
asked for or the request gets `406`. It never falls back to JSON, because a view usually renders a
subset of what its model holds.

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

`[AspNetCoreRuntime]` brings `[HardenedWebModule]` with it, and so does `[KestrelRuntime]`. A library
that carries routes but is not itself the host imports `[HardenedWebModule]` directly, which is also
what the [Lambda web runtime](/aws/lambda-web) sits on.

**Changed 2026-08-16.** Neither host module actually carried the import, so an application shaped
like the snippet above — the shape this page and the README both document — threw on its first
request with `No service for type 'IWebExecutionHandlerService' has been registered`, naming a
framework internal rather than the missing module. Both in-repo web samples hid it: one declares
`[HardenedWebModule]` explicitly, the other inherits it from a library referenced for unrelated
reasons, so a green integration suite never covered the documented shape. `[LambdaWebModule]` had
the same omission, fixed the day before. Declaring the module yourself as well remains correct and
costs nothing — modules deduplicate by equality.

## Links to your own routes

Every module gets two generated types built from the routes it declares:

```csharp
ApplicationRoutes.Orders.Order("42")        // "/orders/42" — the path, from anywhere
_links.Orders.Order("42")                   // what a client should call
_links.Orders.OrderAbsolute("42")           // with a scheme and host, for a Location header
```

The names come from the controller and the method rather than from route names you declare, so a
rename is a compile error at every call site. In a view, where RazorBlade compiles `@` expressions
at build time with exact line and column information, that means a route change breaks the template
rather than the page:

```razor
<a href="@Links.Orders.Order(Model.Id)">@Model.Reference</a>
```

`ApplicationLinks` is in the container, so a handler can take it as a constructor parameter. It goes
through an `ILinkContext`, which is what makes a link correct on a host that strips a prefix before
the application sees the path — API Gateway's stage. Configure one with `LinkConfiguration`:

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
