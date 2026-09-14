# Routing

A route is an attribute on a method of a plain class. The class has no base type and no interface.
Nothing registers it. The build finds the method and compiles a matcher for it.

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

[BasePath("/todos")]
public class TodoController {

    [Get("/{id:int}")]
    public string ById(int id) => id.ToString();

    [Post("/", SuccessStatus = 201)]
    public string Create() => "created";
}
```

`GET /todos/7` reaches `ById` with `id` bound to 7. `GET /todos/abc` is a 404. `POST /todos`
answers 201.

## The route attributes

The route attributes live in `Hardened.Web.Runtime.Attributes`, in the `Hardened.Web.Runtime`
package.

| Attribute | Applies to | Verb it routes |
|---|---|---|
| `[Get(path)]` | a method | GET |
| `[Post(path)]` | a method | POST |
| `[Put(path)]` | a method | PUT |
| `[Patch(path)]` | a method | PATCH |
| `[Delete(path)]` | a method | DELETE |

The path argument is optional. An absent argument routes the method at `/`.

Each of the five declares one named property, `SuccessStatus`, and no other. See
[Status codes](#status-codes) below.

These four attributes change how a group of routes is matched or answered.

| Attribute | Applies to | What it does |
|---|---|---|
| `[BasePath(path)]` | a controller, or the entry point | Prefixes the routes below it |
| `[CaseInsensitiveRoutes]` | the entry point | Matches this module in any case |
| `[RouteConstraint(name)]` | a static method | Declares a constraint a template may use |
| `[CacheControl]` | a method or a controller | Sets the `Cache-Control` response header |

The entry point is the class that carries `[HardenedModule]`.

Two generators read `[BasePath]`, and the two positions compose. On a controller the handler
generator folds it into each route on that class. On the `[HardenedModule]` class the routing table
prefixes it onto every route in the compilation. A route under both answers only at the
concatenation of the two.

```csharp
[HardenedModule]
[BasePath("/module")]
public partial class Application;

[BasePath("/api")]
public class OrderController {

    [Get("/orders/{id}")]
    public string GetOrder(string id) => id;
}
```

That route answers at `/module/api/orders/7` and at nothing else. The handler reports
`/module/api/orders/{id}` as its own path.

A template of `/` under a base path contributes nothing to the route. `[BasePath("/collection")]`
with `[Get("/")]` answers at `/collection`. The boundary slash is collapsed and never doubled.

## What the build generates

The generator selects every method declaration that carries one of the five verb attributes. The
method must sit inside a type declaration that is not an interface.

For each selected method the generator emits a handler class in the controller's namespace plus
`.Generated`. The class is named after the controller and the method. It binds the parameters and
invokes the handler.

For each `[HardenedModule]` class the generator emits three more things.

| Generated source | What it holds |
|---|---|
| `{EntryPoint}.Routing` | A nested `RoutingTable` class, and the code that registers it |
| `{EntryPoint}.Links` | The `Routes` and `Links` types. See [Routes by name](#routes-by-name). |
| `{EntryPoint}.OpenApiDocument` | The document, when the entry point asked for one |

`RoutingTable` implements `IWebExecutionRequestHandlerProvider` and registers as a singleton. It
holds one method per shared path prefix. Each method compares characters of the request path
against a `ReadOnlySpan<char>`.

`[HardenedWebModule]` registers the service that asks the tables. The host attributes import it.

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Runtime.DependencyInjection;

namespace Todos;

[HardenedModule]
[HardenedWebModule]
public partial class TodosLibrary;

[HardenedModule]
[KestrelRuntime]
[TodosLibrary]
public partial class Application;
```

`[KestrelRuntime]` and `[AspNetCoreRuntime]` each declare `[HardenedWebModule]` themselves. A
module that declares it a second time is unaffected, because modules deduplicate by equality.

On `[AspNetCoreRuntime]` the host's `Program.cs` installs the middleware with `UseHardened()`. The
call inserts the Hardened middleware and runs the registered startup services. It puts the routing
filter at the end of the chain. Nothing else installs it, so without the call no declared route
answers.

```csharp
using Hardened.Web.AspNetCore.Runtime;

var builder = Application.CreateBuilder(args);
var app = builder.Build();

app.UseHardened();

app.Run();
```

A test project on this host declares `[assembly: AspNetCoreTesting]`, and the default composition
is `app.UseHardened()` alone. A project whose `Program.cs` puts other middleware around Hardened
names an `IAspNetCoreTestComposition` in that attribute. See [testing web](/guide/testing-web).

`[KestrelRuntime]` takes no such call. `HardenedKestrelApplication.Create` builds the pipeline from
the populated service collection.

The generator ships as an analyzer in `Hardened.Web.SourceGenerator`. A project that references
only the runtime packages compiles routes into nothing, and every route answers 404. The build
reports that as `HRDR006`.

A verb attribute on an interface member compiles to no route. An interface member has no
implementation to call. The build reports `HRDR013` as a warning and names the attribute.

## Static handlers

A handler method can be static. The class holding it can be static too.

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

[BasePath("/todos")]
public static class TodoRoutes {

    [Get("/{id:int}")]
    public static string ById(ITodoStore store, int id) => store.Find(id);
}
```

A static handler takes its dependencies as parameters. `ITodoStore` above resolves from the request
scope, the same scope a constructor parameter would have resolved from. There is no constructor and
no field to hold one in.

Nothing else about the route changes. The path, the parameter binding, the filters, the status codes
and the published document are what they would be on an instance method.

The container never sees the declaring type. A class whose handlers are all static is not
registered, because nothing resolves it. A class holding both kinds is registered, for the instance
handlers.

A described handler cannot be static. The contract generates an interface, and the class
implementing it is built by the container. See [generating from Smithy](/guide/smithy) and
[generating from OpenAPI](/guide/openapi).

## Path tokens

A token is a name in braces. It binds to the handler parameter of the same name.

| Form | What it matches |
|---|---|
| `{name}` | Exactly one path segment |
| `{*name}` | The rest of the path, separators included |
| `{name:constraint}` | One segment that passes the constraint |

The name binds without the marker and without the constraint. `{*path}` binds a parameter called
`path`, and `{id:int}` binds a parameter called `id`.

A token matches at least one character. `/collection/` does not match `/collection/{id}`, and
`/binding/pair//second` does not match `/binding/pair/{first}/{second}`. Both answer 404. A
catch-all follows the same rule, so `/assets/` does not match `/assets/{*path}`.

A token binds by exact name, case included. `[Get("/{eventid}")]` beside a parameter called
`eventId` binds nothing, and the binder reads that parameter from the request body. The build
reports that as `HRDR005`.

These brace forms do not compile. Each one is `HRDR002`, an error, reported once per token.

| Written | Why it is refused |
|---|---|
| `{id?}` | An optional token is two routes. Declare both. |
| `{id=5}` | Give the handler's parameter a C# default instead. |
| `{id` or a stray `}` | The brace has no partner. |
| `{}` or `{:int}` | A token with no name binds nothing. |
| The same name twice in one route | Two tokens cannot both bind one parameter. |
| `{id:isbn}` with no `isbn` declared | Nothing declares that constraint. |

The message quotes the token as written and names the route and the handler. It carries no source
location.

## Route constraints

A constraint is part of the match. A value that fails it produces a 404, not a 400. An
unconstrained token behaves differently. `/path-typed/abc` with an `int` parameter reaches the
binder, which answers 400. `/path-constrained/abc` with `{count:int}` answers 404.

The generated table calls the test in `Hardened.Web.Runtime.Routing.RouteConstraints` directly.
Every test takes a `ReadOnlySpan<char>` and allocates nothing.

| Name | Matches | Does not match |
|---|---|---|
| `int` | A 32-bit integer, invariant culture | `abc`, `4.5`, `1,000` |
| `long` | A 64-bit integer, invariant culture | `abc` |
| `guid` | A GUID | `not-a-guid` |
| `bool` | `true` or `false` | `yes` |
| `decimal` | A sign and a decimal point, nothing else | `abc`, `1,000` |
| `date` | `yyyy-MM-dd` exactly | `2026-8-17`, `12/06/2026` |
| `datetime` | One of four ISO 8601 forms, listed below | `2026-08-17 09:30` |
| `alpha` | One or more ASCII letters | `beta2` |
| `hex` | One or more ASCII hex digits | `0g9` |
| `slug` | Lower-case ASCII words joined by single hyphens | `My-First-Post`, `-lead`, `a--b` |

`datetime` accepts `yyyy-MM-ddTHH:mm:ssK`, `yyyy-MM-ddTHH:mm:ss.FFFFFFFK`, `yyyy-MM-ddTHH:mmK` and
`yyyy-MM-dd`. Every constraint parses under the invariant culture.

Seven more names take whole-number arguments. Nothing else is a valid argument.

| Name | Matches |
|---|---|
| `length(n)` | Exactly n characters |
| `length(min,max)` | Between min and max characters, inclusive |
| `minlength(n)` | n characters or more |
| `maxlength(n)` | n characters or fewer |
| `min(n)` | An integer of n or more |
| `max(n)` | An integer of n or less |
| `range(min,max)` | An integer between min and max, inclusive |

A name used at an arity it does not have is `HRDR002`. The message says which arities it takes.

A token may carry several constraints, separated by colons. The chain is a conjunction and every
term must pass. `{id:int:min(1)}` matches `42` and rejects `0` and `abc`.

### Declaring a constraint

Write a static method, give it `[RouteConstraint]`, and name the constraint.

```csharp
public static class Codes {

    [RouteConstraint("code")]
    public static bool IsCode(ReadOnlySpan<char> value) {
        if (value.Length != 3) {
            return false;
        }

        foreach (var character in value) {
            if (character < 'A' || character > 'Z') {
                return false;
            }
        }

        return true;
    }
}
```

Then write `[Get("/items/{id:code}")]`. The table calls `Codes.IsCode` directly, on every request
that reaches that position.

The method must be `static`, return `bool`, and take one `ReadOnlySpan<char>`. A declaration that
is not that shape is `HRDR003`, an error, and the generator emits no call to it. Names are compared
in lower case. A declared constraint takes no arguments, so `{id:code(3)}` is `HRDR002`.

`[RouteConstraint]` is repeatable, so one method can serve under more than one name.

## Matching order

A literal segment beats a token in the same position, at every depth. The table tries the literal
branches first and consults the token branch only when they all decline.

```csharp
[Get("/r/fixed")]      // answers /r/fixed
[Get("/r/{id}")]       // answers /r/other, and /r/fixedly with id = "fixedly"
[Get("/r/fixed/sub")]  // answers /r/fixed/sub
[Get("/r/{id}/sub")]   // answers /r/other/sub
```

A literal also beats a catch-all. `/assets/index` reaches `[Get("/assets/index")]` beside
`[Get("/assets/{*path}")]`.

A token stops at the next segment boundary. `/files/{name}/download` matches `/files/a/download`
and does not match `/files/a/b/c/download`. A path deeper than any route declares matches nothing.
A route may put a literal other than `/` after a token: `/f/{id}.json` binds `id` to `7` for
`/f/7.json`.

A catch-all takes everything that is left. `/assets/{*path}` binds `path` to `a/b/c.png` for
`/assets/a/b/c.png`. It still matches a single segment.

Two routes that match the same paths and differ only in what their token accepts are `HRDR001`, an
error. `/users/{id:int}` beside `/users/{name}` is the case, and so is `{path}` beside `{*path}`.
The handler a request reaches then depends on the value in it. Lower the severity per file or per
project when an existing pair has to stay.

```ini
# .editorconfig
dotnet_diagnostic.HRDR001.severity = warning
```

```xml
<PropertyGroup>
  <HardenedAmbiguousRoutes>warning</HardenedAmbiguousRoutes>
</PropertyGroup>
```

The rule leaves a literal beside a token alone. It leaves one shape under two different verbs
alone as well.

One path is one node however many verbs answer at it. A constraint declared by any route at a
position constrains that position for every verb there. `[Get("/items/{id:int}")]` beside
`[Delete("/items/{id}")]` makes `/items/abc` a 404 under both verbs.

The web pipeline consults routing tables in reverse registration order. A route an application
declares therefore shadows one a framework module registered earlier. It asks a provider that
implements `IFallbackRequestHandlerProvider` after every ordinary one.

## What a route publishes

The published path template carries no routing syntax. The document writer strips the constraint
and the catch-all marker from every token. `/todos/{id:int}` publishes as `/todos/{id}`, and
`/receipts/{*path}` as `/receipts/{path}`. No published path contains `:` or `*`.

A catch-all therefore does not survive a round trip. A route generated from a specification takes
one segment, because a path template has no way to say otherwise.

A constrained token adds a 404 to the operation. That response carries no body. Its description
reads "The path did not name a resource: a token failed its route constraint." An operation that
declares its own 404 keeps that description and gains a sentence saying the same thing.

The constraint also removes the 400 the document would otherwise publish. `{id:int}` makes the
same test the converter makes, so the converter can no longer refuse that value. A handler taking
an `int id` at `/todos/{id:int}` publishes 200 and 404 and no 400. The same handler at
`/todos/{id}` publishes the 400.

A route registered with a lambda is documented before its path exists, so it cannot be documented
both ways. The build states the constraint each token needs and registration is refused without
one:

```
'/acme/orders/{id}' does not constrain the token '{id}', which the registered handler reads.
Register it as '{id:int}' - or as '{id:range}', so a value it cannot read answers 404 rather
than reaching the handler's binder
```

Any constraint that makes the converter's test will do, in any position in the chain:
`{id:int}`, `{id:range(1,10)}` and `{id:min(1):int}` all satisfy an `int` parameter. A token the
lambda reads as a `string` is held to nothing, because a string binds as itself and there is no
400 to remove.

## Case and trailing slashes

Routes match by exact case. `/orders/summary` does not answer `/Orders/Summary`. A route declared
in mixed case matches its own spelling.

`[CaseInsensitiveRoutes]` on the `[HardenedModule]` class matches that module's routes in any case.
The attribute carries no state. The comparison is compiled into the table, so the attribute is the
only switch.

A trailing slash is part of the path. `/orders` and `/orders/` are two routes, and a route declared
as one does not answer the other. An empty segment is a segment, so `/a//b` and `/a/b` are two
paths as well.

`IWebRoutingConfiguration.TrailingSlash` changes what a path that differs only by a trailing slash
gets. The value is `TrailingSlash.Strict` unless the application sets it.

| Value | What the other spelling gets |
|---|---|
| `Strict` | Nothing. Only the declared spelling answers. |
| `Normalise` | The route, with no difference visible to the client. |
| `Redirect` | `308 Permanent Redirect`, with `Location` set to the declared path. |

The root has no other spelling, so every value leaves `/` alone. The policy runs after static
content and before the 405.

## Verbs and status codes

The five verb attributes are the whole set. No attribute declares a HEAD route.

HEAD reaches the GET handler. The table emits a `case "HEAD":` beside every `case "GET":`. The
handler, its filters and its serializer all run. The framework counts the body and discards it on
the way out, and `Content-Length` reports the count. The status and every header match the GET.

A request whose path matches under another verb gets 405. The response carries `Allow` and no
body. `Allow` lists the verbs declared at that path, sorted. It includes HEAD wherever GET is
declared.

```console
$ curl -i -X PUT localhost:5080/todos/7
HTTP/1.1 405 Method Not Allowed
Allow: GET, HEAD
```

A path no table declares gets 404. A 405 therefore tells a client that the resource exists.

### Status codes

A successful response carries 200. `SuccessStatus` on the verb attribute changes it.

```csharp
[Post("/created", SuccessStatus = 201)]
public string CreateItem() => "created";

[Delete("/emptied", SuccessStatus = 204)]
public string EmptyItem() => "this body is not written";
```

`SuccessStatus = 200` and an unset `SuccessStatus` mean the same thing. The framework writes no
body for 204, 205 or 304, whatever the handler returned.

A handler that returns null gets a status from the request method.

| Method | Status for a null return |
|---|---|
| GET | 404 |
| PUT | 404 |
| POST | 200 |
| DELETE | 200 |
| Anything else | 200 |

A declared `SuccessStatus` replaces that status only where it is already 2xx. A null return on a
GET stays 404, and a null return on a POST declaring 201 answers 201.

## The Cache-Control header

`[CacheControl]` writes the `Cache-Control` header on a handler's responses. On a controller it
applies to every handler on the class. It takes two named properties.

| Property | Default | What it carries |
|---|---|---|
| `MaxAge` | `0` | Seconds for `max-age` |
| `Type` | `CacheControlEnum.MaxAge \| CacheControlEnum.Public` | The directives to write |

`CacheControlEnum` has `MaxAge`, `NoCache`, `NoStore`, `NoTransform`, `Public` and `Private`. The
header is written in that order: `private` or `public`, then `no-cache`, `no-store`, `max-age`,
`no-transform`. `Private` wins over `Public` when both are set. `max-age` appears only when the
`MaxAge` flag is set, so the value alone does not put it there.

```csharp
[Get("/default")]
[CacheControl]                                    // public, max-age=0
public string Default() => "default";

[Get("/none")]
[CacheControl(Type = CacheControlEnum.NoStore)]   // no-store
public string None() => "none";

[Get("/private")]
[CacheControl(MaxAge = 60, Type = CacheControlEnum.MaxAge | CacheControlEnum.Private)]
public string Private() => "private";             // private, max-age=60
```

A handler without the attribute sends no `Cache-Control` header. The filter writes every flag that
is set, including combinations a cache reads as contradictory.

This attribute writes a header and stores nothing. To store a handler's answer, see
[response caching](/guide/response-caching).

## Routes by name

The build emits a static path builder and a link builder for every entry point. Both are nested in
the entry point class. A rename of a controller or a handler is then a compile error at every call
site.

```csharp
// [Get("/items/{id:int}")] on ItemController.Read
var path = Application.Routes.Item.Read(42);   // "/items/42"
```

The group is the controller's name with a `Controller` suffix removed, or what `[Tag]` declared.
The method is the handler's name. Where the two are equal the verb is appended, so a `Health`
handler on `HealthController` becomes `HealthGet`.

A string argument is escaped with `Uri.EscapeDataString`. A catch-all argument is not, because it
is a path fragment. Anything else is formatted with `Convert.ToString` under the invariant culture.

`Application.Links` answers the same routes as links a client can call. It is registered as a
transient service. `Links.Item.Read(42)` prefixes `BasePath`, and `Links.Item.ReadAbsolute(42)`
also prefixes the scheme and the host. Half an origin falls back to the relative link.

`LinkConfiguration`, in `Hardened.Web.Runtime.Links`, carries the three values. `BasePath` is empty
by default, which is right for Kestrel and for ASP.NET Core. A host that strips a prefix before the
application sees the path has to put that prefix back here. API Gateway's stage is the case this
exists for. `LinkContext` trims a trailing slash off `BasePath`, so both spellings build one link.

```csharp
public void ConfigureServices(IServiceCollection services) {
    services.AddSingleton<IConfigurationPackage>(
        new SimpleConfigurationPackage(
            Array.Empty<IConfigurationValueProvider>(),
            new IConfigurationValueAmender[] {
                new SimpleConfigurationValueAmender<LinkConfiguration>(
                    (_, links) => {
                        links.BasePath = "/prod";
                        links.Scheme = "https";
                        links.Host = "api.example.com";

                        return links;
                    })
            }));
}
```

`Links.Item.Read(42)` then answers `/prod/items/42`, and `Links.Item.ReadAbsolute(42)` answers
`https://api.example.com/prod/items/42`. A host that reads the stage off each request registers its
own `ILinkContext`. The default registration steps aside for it.

A route that `HRDR002` refused gets no link.

## Routes registered at startup

A route whose path the application computes is registered by a type that implements
`IRouteRegistration`. The path is the only part that comes from run time. The handler behind it is
a handler declared the ordinary way, and the build generated it from that declaration.

```csharp
using Hardened.Web.Runtime.Routing;

public class TenantRoutes : IRouteRegistration {

    private readonly ITenantCatalog _catalog;

    public TenantRoutes(ITenantCatalog catalog) => _catalog = catalog;

    public async ValueTask Register(IRouteRegistry routes, CancellationToken cancellationToken) {
        foreach (var tenant in await _catalog.Active(cancellationToken)) {
            routes.Get($"/{tenant.Slug}/orders/{{id:int}}",
                       typeof(OrderController), nameof(OrderController.Get));
        }
    }
}
```

`OrderController.Get` is declared `[Get("/orders/{id:int}")]` and answers there as well. The
registration serves the same generated handler at one more path per tenant.

Implementing the interface is the whole declaration. The build finds the type and registers it, so
there is no attribute to add and nothing to remember. It is constructed once, and its dependencies
arrive through its constructor. `IRouteRegistry.ServiceProvider` is there for a dependency the
constructor cannot take. An abstract class that declares the interface for its subclasses is not
registered.

The entry point can implement it too, which is the shape that needs no class of its own.

```csharp
[HardenedModule]
[HardenedWebModule]
public partial class Application : IRouteRegistration {

    public async ValueTask Register(IRouteRegistry routes, CancellationToken cancellationToken) {
        var catalog = routes.ServiceProvider.GetRequiredService<ITenantCatalog>();

        foreach (var tenant in await catalog.Active(cancellationToken)) {
            routes.Get($"/{tenant.Slug}/orders/{{id:int}}",
                       typeof(OrderController), nameof(OrderController.Get));
        }
    }
}
```

An entry point is a module rather than a service, so it takes its dependencies from
`ServiceProvider` rather than through a constructor.

`IRouteRegistry` has `Get`, `Post`, `Put`, `Patch`, `Delete` and `Map`. Each returns the registry,
so calls chain. Each takes the path, then either the controller type and the handler method name, or
a lambda. `Map` takes the verb first.

The template language is the one the attributes use. The same tokens, the same constraints
including the ones `[RouteConstraint]` declares, the same catch-all. `[BasePath]` on the entry point
prefixes a registered path exactly as it prefixes an attribute route. `[CaseInsensitiveRoutes]`
applies to both.

### A lambda instead of a controller

```csharp
routes.Get($"/{tenant.Slug}/orders/{{id:int}}",
           (int id, IOrderService orders) => orders.Get(tenant.Id, id));
```

The build reads the lambda where it is written: its parameters, its return type, and any attribute
on it. It emits a handler from them and rewrites the call to one that carries it, so the handler is
compiled code and the path is still the only part that comes from run time.

The lambda closes over `tenant`, and that closure is the point. It cannot be replaced by a call to a
method, and nothing resolves it from a container.

Parameters are classified by type, because there is no template to classify them against.

| Parameter | Binds from |
|---|---|
| A binding attribute | As written |
| `IExecutionContext`, `IExecutionRequest`, `IExecutionResponse` | The execution context |
| `IServiceProvider` | The container itself |
| `CancellationToken` | The request's lifetime |
| Any interface | The container |
| A type that can be read from a string | The path token of that name |
| Anything else | The request body |

The last two are the difference from an attribute route, where the template answers the question.
A type that can be read from a string is the set `StringConverterService` converts, plus any enum.
`byte[]` is not in it: a `byte[]` parameter is the request body.

The token names travel with the handler, so a template that does not declare one of them is a
startup failure rather than a route that binds nothing.

C# has allowed attributes on a lambda since version 10, so `[Compress]`, `[CacheResponse]` and
`[RequireAuthorization]` attach here and a registered route keeps the declarative surface a
controller method has.

Two shapes the build cannot read, both `HRDR014`:

```csharp
Func<int, string> handler = id => id.ToString();

routes.Get("/orders/{id:int}", handler);   // the lambda has to be written here
routes.Map(verb, "/orders", () => "ok");   // the verb has to be a constant
```

Neither ships a route that silently binds nothing. The declared method throws, and the diagnostic
points at the call site, which is where the thing to change is.

### What runs when

`Register` runs during startup, before the host serves anything. Startup services all run
concurrently, so a registration that depends on another startup service's side effect has no
ordering against it. Depend on a service the container can build.

On Lambda this is the init path of every cold start. Whatever I/O `Register` does is paid there.

The table is built once and is immutable from then on. Registering after startup throws and names
the route.

### What is checked, and when

A route written as an attribute is a build error when it is wrong. A registered path does not exist
until the application runs, so these checks move to startup. Every failure is collected and thrown
together.

| Check | When |
|---|---|
| Handler shape, binding, declared responses | Build |
| A token the template declares twice | Startup |
| A constraint name nothing declares | Startup |
| Two registered routes at one path under one verb | Startup |
| A template that does not declare a token the handler reads | Startup |

The last one is the one to know about. A binder reads the token called `id`, not token 0, which is
what lets a handler compiled for `/orders/{id}` answer at `/acme/orders/{id}`. Registered under a
template spelling it `orderId`, the same handler would bind nothing. That is a startup failure
rather than a 400 on every request.

### The document follows

A registered route is in the OpenAPI document, at the path it was registered at.

Nothing writes an operation at run time. Everything in one is known at build time except its path
key, so the build writes the operation and the application writes the path around it, once, when
registration closes. No JSON is parsed and no schema is written.

Two verbs at one path are one path item with two operations. The path key carries no routing syntax,
so `/acme/orders/{id:int}` is published as `/acme/orders/{id}` — a constraint is how the router
decides what matches, and a document has no way to say it. A model a registered route binds is in
`components/schemas` like any other.

The cost is one document-sized string in the assembly: the compiled document is embedded compressed,
and the two halves the application splices between are embedded uncompressed beside it. Only an
application that declares an `IRouteRegistration` carries them.

### What a registered route does not do

`Application.Routes` and `Application.Links` do not carry it. Both are emitted at build time from
routes the build can see, and a computed path is not one of them.

An attribute route wins. Routes registered at startup are asked after the generated table, so a
path declared both ways answers from the declaration.

## Routing diagnostics

| Code | Reports | Severity |
|---|---|---|
| `HRDR001` | Two routes differing only in what their token accepts | Error |
| `HRDR002` | A brace form the build does not compile | Error |
| `HRDR003` | A `[RouteConstraint]` that is not a `static bool(ReadOnlySpan<char>)` | Error |
| `HRDR005` | A route token that binds no parameter, beside one read from the body | Error |
| `HRDR006` | Routes in an assembly with no routing generator referenced | Error |
| `HRDR013` | A verb attribute on an interface member | Warning |
| `HRDR014` | A lambda registration the build cannot read | Error |

## Next

- [Parameter binding](/guide/parameter-binding) for what a handler's other parameters bind from.
- [Responses](/guide/responses) for declaring more than one status on a handler.
- [Response caching](/guide/response-caching) for storing a handler's answer.
- [OpenAPI](/guide/openapi) for the document these routes publish.
