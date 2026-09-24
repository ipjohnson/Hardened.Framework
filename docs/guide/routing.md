# Routing

A route attribute such as `[Get]` makes a method a handler for the HTTP method it names. The attribute's argument is the path template.

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

[BasePath("/labels")]
public class LabelController
{
    [Get("/{id:int}")]
    public string ById(int id) => $"label {id}";

    [Post("/", SuccessStatus = 201)]
    public string Create() => "created";
}
```

```http
GET /todos/labels/7

HTTP/1.1 200 OK
Content-Type: application/json

"label 7"
```

```http
GET /todos/labels/abc

HTTP/1.1 404 Not Found
Content-Length: 0
```

```http
POST /todos/labels

HTTP/1.1 201 Created
Content-Type: application/json

"created"
```

The class needs no base type, no interface and no registration. The build registers the class. It also compiles every route in the project into a routing table. [From scratch](/guide/from-scratch) lists the generated files.

The examples on this page run in a project made with `dotnet new hardened-web -n Todos`. Their files are in the library project, `src/Todos`, unless the text names another project. The library module's `[BasePath("/todos")]` and the class's `[BasePath("/labels")]` put `LabelController` under `/todos/labels`.

`{id:int}` matches only an integer, so `GET /todos/labels/abc` answers 404 and the handler does not run. `SuccessStatus = 201` makes `Create` answer 201.

## Route attributes

| Attribute | Routes |
|---|---|
| `[Get(path)]` | GET, and [HEAD](#head-and-a-wrong-method) |
| `[Post(path)]` | POST |
| `[Put(path)]` | PUT |
| `[Patch(path)]` | PATCH |
| `[Delete(path)]` | DELETE |

The five attributes are in the `Hardened.Web.Runtime.Attributes` namespace, in the `Hardened.Web.Runtime` package. `[BasePath]`, `[CaseInsensitiveRoutes]` and `[RouteConstraint]` are in the same namespace.

The path argument is optional. `[Get]` with no argument uses the template `/`. A template without a leading `/` gets one. Each attribute has one named property, `SuccessStatus`. [Status codes](#status-codes) covers it.

The build routes one route attribute per method. It ignores a second one on the same method, and reports nothing. A route attribute on an interface member compiles no route. The build reports `HRDR013`, a warning.

## Base paths

| `[BasePath]` on | Prefixes |
|---|---|
| A controller class | The routes declared in that class |
| A `[HardenedModule]` class | Every route compiled in that module's project |

A route under both answers only at the module's base path, then the class's, then the route's template. The `hardened-web` template puts `[BasePath("/todos")]` on its library module, `TodosLibrary`. A route with the template `/` answers at the base path, with no trailing slash. Where two parts join, the build keeps one slash. `[BasePath("/api/")]` with `[Get("/x")]` answers `GET /todos/api/x`.

`[BasePath]` and `[CaseInsensitiveRoutes]` on a module apply only to the routes in the module's own project. In `src/Todos.Host`, the `hardened-web` template's `Application` module declares no routes. The two attributes change nothing on it, and the build reports nothing. `[assembly: BasePath("/asm")]` compiles and changes no route.

::: warning
`[BasePath]` or `[CaseInsensitiveRoutes]` on the application module changes no route, and neither does `[BasePath]` on the assembly. The build reports nothing. Put the attributes on the module that holds the routes.
:::

## Path tokens

| Written | Matches |
|---|---|
| `{name}` | One path segment |
| `{*name}` | The rest of the path, `/` included |
| `{name:constraint}` | One segment that passes the constraint |

A token binds the handler parameter of the same name. The name must match exactly, case included. The name leaves out the `*` and the constraint, so `{*path}` binds `path` and `{id:int}` binds `id`.

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

[BasePath("/files")]
public class FileController
{
    [Get("/{*path}")]
    public string Read(string path) => path;
}
```

```http
GET /todos/files/a/b/c.png

HTTP/1.1 200 OK
Content-Type: application/json

"a/b/c.png"
```

```http
GET /todos/files/

HTTP/1.1 404 Not Found
Content-Length: 0
```

A controller in `src/Todos` with `[BasePath("/tokens")]` declares the other routes in this section, so they answer under `/todos/tokens`.

A token matches at least one character. `GET /todos/files/` does not match `/files/{*path}`, and `/pair//b` does not match `/pair/{first}/{second}`. Both answer 404. One segment is enough for the catch-all `{*name}`.

`{name}` ends at the next `/`. `/files/{name}/download` matches `/files/a/download` and does not match `/files/a/b/c/download`. A literal can follow a token inside one segment. `/f/{id}.json` binds `id` to `7` for `/f/7.json`, and to `a.b` for `/f/a.b.json`.

On the Kestrel host, a token's value arrives percent-decoded, except `%2F`, which stays as sent. `/one/a%20b` binds `a b`, and `/one/a%2Fb` binds `a%2Fb`.

A token that no parameter binds is allowed. [Parameter binding](/guide/parameter-binding) covers where the other parameters come from, and how a token's text converts to the parameter's type.

The build refuses these brace forms with `HRDR002`, an error:

| Written | What the `HRDR002` message says |
|---|---|
| `{id?}` | An optional token is not supported. Declare the two paths as two routes |
| `{id=5}` | A default in the template is not supported. Give the parameter a C# default instead |
| `{id`, or a `}` with no `{` | The brace has no partner |
| `{}` or `{:int}` | A token with no name binds nothing |
| The same name twice, `/{id}/{id}` | The name is declared twice in this route |
| `{id:isbn}`, a constraint nothing declares | Nothing declares that constraint. It lists the built-in names |
| `{id:length(1,2,3)}`, a wrong argument count | `'length' takes 'length(n)' or 'length(n,n)', but was given 3 arguments` |

The build reports `HRDR002` once for each refused token. The message quotes the route, the handler and the token.

## Route constraints

A constraint is part of the match. A value that fails it answers 404, and the handler does not run. Without a constraint the route matches, and a value that does not convert to the parameter's type answers 400. The `hardened-web` template's `ById` handler is `[Get("/{id}")]` with an `int id`:

```http
GET /todos/abc

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"id","code":"invalid","message":"id is not a valid Int32."}]}
```

These are the built-in constraints:

| Name | Matches | Does not match |
|---|---|---|
| `int` | A 32-bit integer with an optional sign: `42`, `-5`, `+5` | `abc`, `4.5`, `1,000`, `2147483648` |
| `long` | A 64-bit integer: `2147483648` | `9223372036854775808`, `abc` |
| `guid` | A GUID, with or without hyphens, braces or parentheses | `not-a-guid` |
| `bool` | `true` or `false`, in any case | `yes`, `1` |
| `decimal` | Digits with an optional sign and decimal point: `4.5`, `-4.5`, `.5`, `45` | `1,000`, `1e3`, `abc` |
| `date` | `yyyy-MM-dd`: `2026-08-17` | `2026-8-17`, `12/06/2026`, `2026-08-17T09:30:00Z` |
| `datetime` | One of the four ISO 8601 forms below | `2026-08-17 09:30` |
| `alpha` | ASCII letters in either case: `beta`, `Beta` | `beta2`, `café` |
| `hex` | ASCII hex digits in either case: `0af9`, `0AF9` | `0g9` |
| `slug` | Lower-case ASCII letters and digits, in words joined by single hyphens: `my-post-2`, `123` | `My-First-Post`, `-lead`, `trail-`, `a--b` |

`datetime` accepts `yyyy-MM-ddTHH:mm:ssK`, `yyyy-MM-ddTHH:mm:ss.FFFFFFFK`, `yyyy-MM-ddTHH:mmK` and `yyyy-MM-dd`. The `K` is `Z`, an offset such as `+02:00`, or nothing.

These constraints take arguments:

| Name | Matches |
|---|---|
| `length(n)` | Exactly n characters |
| `length(min,max)` | From min to max characters |
| `minlength(n)` | n characters or more |
| `maxlength(n)` | n characters or fewer |
| `min(n)` | An integer of n or more |
| `max(n)` | An integer of n or less |
| `range(min,max)` | An integer from min to max |

The bounds are inclusive. Arguments are whole numbers, so `{id:min(1.5)}` fails the build with `HRDR002`. `min`, `max` and `range` parse the value as a 64-bit integer themselves. `{v:min(1)}` does not match `abc`, and it matches `99999999999`.

A token can carry several constraints separated by colons. Every one must pass. `{v:int:min(1)}` matches `42` and does not match `0` or `abc`.

Constraint names ignore case, so `{v:INT}` works as `{v:int}`. Every built-in constraint parses under the invariant culture. The routing table calls each test directly for every request that reaches the token.

A constraint also changes what the OpenAPI document publishes for the operation. [The OpenAPI document](/guide/openapi-document) covers it.

## Declaring a constraint

`[RouteConstraint("code")]` on a method declares a constraint named `code`:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public static class Codes
{
    [RouteConstraint("code")]
    public static bool IsCode(ReadOnlySpan<char> value)
    {
        if (value.Length != 3)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character < 'A' || character > 'Z')
            {
                return false;
            }
        }

        return true;
    }
}
```

A template in the same project can use it:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

[BasePath("/countries")]
public class CountryController
{
    [Get("/{code:code}")]
    public string ByCode(string code) => code;
}
```

```http
GET /todos/countries/GBR

HTTP/1.1 200 OK
Content-Type: application/json

"GBR"
```

```http
GET /todos/countries/gbr

HTTP/1.1 404 Not Found
Content-Length: 0
```

The method must be `static`, return `bool` and take one `ReadOnlySpan<char>`. Any other signature fails the build with `HRDR003`. The method must also be `public` or `internal`. A `private` one fails the build with `CS0122` in the generated routing table. The routing table calls the method directly, as `global::Todos.Codes.IsCode(charSpan.Slice(index))`.

The constraint name ignores case, in the attribute and in the template. A declared constraint takes no arguments, so `{code:code(3)}` fails the build with `HRDR002`. The attribute can be repeated on one method to declare several names.

A constraint serves the routes of its own project only. A route in `src/Todos.Host` that uses the `code` constraint from `src/Todos` fails the build with `HRDR002`: "Nothing declares a route constraint called 'code'."

## Matching order

The routing table tries a literal segment before a token in the same position, at every depth. It tries the token only when no literal route matches the path. It also tries a literal before a catch-all.

One controller in `src/Todos` declares these routes, shown as full paths:

| Path | Methods |
|---|---|
| `/todos/r/fixed` | GET, POST |
| `/todos/r/{id}` | GET, DELETE |
| `/todos/r/fixed/sub` | GET |
| `/todos/r/{id}/sub` | GET |
| `/todos/r/assets/index` | GET |
| `/todos/r/assets/{*path}` | GET |
| `/todos/r/items/{id:int}` | GET |
| `/todos/r/items/{id}` | DELETE |

The table shows which route each request reaches:

| Request | Reaches |
|---|---|
| `GET /todos/r/fixed` | `GET /todos/r/fixed` |
| `GET /todos/r/fixedly` | `GET /todos/r/{id}`, with `id` = `fixedly` |
| `GET /todos/r/fixed/sub` | `GET /todos/r/fixed/sub` |
| `GET /todos/r/other/sub` | `GET /todos/r/{id}/sub`, with `id` = `other` |
| `GET /todos/r/assets/index` | `GET /todos/r/assets/index` |
| `GET /todos/r/assets/index/more` | `GET /todos/r/assets/{*path}`, with `path` = `index/more` |
| `DELETE /todos/r/other` | `DELETE /todos/r/{id}` |
| `DELETE /todos/r/fixed` | Nothing: 405 with `Allow: GET, HEAD, POST` |
| `GET /todos/r/items/abc` | Nothing: 404 |
| `DELETE /todos/r/items/abc` | Nothing: 404 |
| `DELETE /todos/r/items/5` | `DELETE /todos/r/items/{id}` |

A literal route takes its path under every method. `DELETE /todos/r/fixed` answers 405 and does not reach `DELETE /todos/r/{id}`.

A constraint at a token position applies to every method at that position. Under `/todos/r/items`, GET declares `{id:int}` and DELETE declares `{id}`. `DELETE /todos/r/items/abc` answers 404.

Two routes that match the same paths under the same method, and differ only in what one token accepts, fail the build with `HRDR001`. `/amb/{id:int}` beside `/amb/{name}` fails, and so does `/files/{path}` beside `/files/{*path}`. The build reports neither a literal beside a token nor one path shape under two methods, as in the routes above.

When the application module's project and an imported module's project declare the same method and path, the application's route answers. The build checks each project's routes separately, so it reports nothing.

## Case sensitivity

By default, paths match with exact case, the base path included. `[CaseInsensitiveRoutes]` on a module matches that module's routes in any case. Here `TodosLibrary` carries the attribute:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization.Metadata;

namespace Todos;

[HardenedModule]
[HardenedWebModule]
[BasePath("/todos")]
[CaseInsensitiveRoutes]
[Server("http://localhost:5080", "Local")]
[Enable<OpenApiDocumentPublishing>]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);
    }
}
```

```http
GET /Todos/Labels/7

HTTP/1.1 200 OK
Content-Type: application/json

"label 7"
```

The attribute is a build-time setting. The build compiles the comparison into the routing table. The attribute changes the matching of literals only. A token's value keeps its case. A constraint tests the value as sent.

## Trailing slashes

A trailing slash is part of the path. By default, a request for `/todos/labels/7/` does not reach the route at `/todos/labels/7`. An empty segment between two slashes also counts as a segment, so `/todos/api//x` does not match `/todos/api/x`.

The `TrailingSlash` property of `WebRoutingConfiguration` decides what a request gets when its path differs from a route only by a trailing slash. Its default is `TrailingSlash.Strict`. The class and the `TrailingSlash` enum are in the `Hardened.Web.Runtime.Configuration` namespace.

| `TrailingSlash` | The other spelling gets |
|---|---|
| `Strict` | Nothing from this route. It answers 404 unless another route matches it |
| `Normalise` | The route, as though the declared spelling had been sent |
| `Redirect` | `308 Permanent Redirect`, with `Location` set to the declared spelling |

An `AppConfig` amendment sets the property. [Configuration](/guide/configuration) covers `AppConfig`. `src/Todos.Host/ApplicationConfiguration.cs`, a second file of the `partial class Application`, puts the amendment in the application module:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Web.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Todos.Host;

public partial class Application : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        var config = new AppConfig();

        config.Amend((WebRoutingConfiguration routing) => routing.TrailingSlash = TrailingSlash.Redirect);

        services.AddSingleton<IConfigurationPackage>(config);
    }
}
```

```http
GET /todos/labels/7/

HTTP/1.1 308 Permanent Redirect
Content-Length: 0
Location: /todos/labels/7
```

This line, in place of the `Amend` line, selects `Normalise`:

```csharp
config.Amend((WebRoutingConfiguration routing) => routing.TrailingSlash = TrailingSlash.Normalise);
```

```http
GET /todos/labels/7/

HTTP/1.1 200 OK
Content-Type: application/json

"label 7"
```

Under `Redirect`, every method gets 308, `POST` included. The redirect's `Location` carries the path only. `GET /todos/labels/7/?page=2` answers 308 with `Location: /todos/labels/7`. A route declared with a trailing slash works the same way in reverse. For `[Get("/lines/")]` under `Redirect`, a request for `/todos/ts/lines` answers 308 with `Location: /todos/ts/lines/`.

The setting never changes a request for the root path `/`. At the other spelling, a method that the route does not declare answers 404. At the declared spelling, the same method answers 405.

## HEAD and a wrong method

No attribute declares a HEAD route. A HEAD request runs the GET handler at that path, with its filters and serializer. The response has no body. `Content-Length` gives the length of the dropped body:

```http
HEAD /todos/labels/7

HTTP/1.1 200 OK
Content-Length: 9
Content-Type: application/json
```

A HEAD request answers with the GET's status and headers. [Limits](#limits) has the exception, a GET handler that returns null.

A request whose path matches a route under another method answers 405, with `Allow` and no body. `Allow` lists the methods declared at that path in alphabetical order, with HEAD wherever GET is declared:

```http
PUT /todos/labels/7

HTTP/1.1 405 Method Not Allowed
Content-Length: 0
Allow: GET, HEAD
```

An `OPTIONS` request with no CORS headers answers 405 in the same way. A request for a path that no route matches answers 404 with no body.

## Status codes

A handler answers 200 unless its route attribute sets `SuccessStatus`. A 204, 205 or 304 response carries no body, whatever the handler returns:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

[BasePath("/drafts")]
public class DraftController
{
    [Delete("/{id:int}", SuccessStatus = 204)]
    public string Remove(int id) => "this body is not written";
}
```

```http
DELETE /todos/drafts/3

HTTP/1.1 204 No Content
```

A handler that returns null gets a status from the request method, and no body:

| Method | A null return answers |
|---|---|
| GET | 404 |
| PUT | 404 |
| POST | 200 |
| PATCH | 200 |
| DELETE | 200 |

`SuccessStatus` replaces a null return's status only when that status is 2xx. A POST handler that declares 201 answers 201 for null. A GET handler that declares 201 answers 404 for null.

[Declared responses](/guide/responses) covers a handler that answers more than one status.

## Static handlers

A handler method can be `static`, and so can its class. A static handler takes its services as method parameters. `ITodoStore` here is the store from the `hardened-web` template:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public static class StatsRoutes
{
    [Get("/stats/count")]
    public static async Task<int> Count(ITodoStore store) => (await store.All()).Count;
}
```

```http
GET /todos/stats/count

HTTP/1.1 200 OK
Content-Type: application/json

2
```

A class whose handlers are all static is not registered in the container. A class with at least one instance handler is registered.

## Diagnostics

| Code | Severity | Reports |
|---|---|---|
| `HRDR001` | Error | Two routes under one method that differ only in what one token accepts |
| `HRDR002` | Error | A brace form the build does not compile, or a constraint nothing declares |
| `HRDR003` | Error | A `[RouteConstraint]` method that is not a `static bool` taking one `ReadOnlySpan<char>` |
| `HRDR005` | Error | A token that binds no parameter, while a parameter meant for it is read from the request body |
| `HRDR013` | Warning | A route attribute on an interface member |

`HRDR005` covers two cases of a token that binds nothing while a parameter is read from the request body in its place. In one, the parameter's name differs from the token only by case. In the other, the method is GET, HEAD, OPTIONS or TRACE. This controller does not build:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

[BasePath("/events")]
public class EventController
{
    [Get("/{eventid}")]
    public string ById(string eventId) => eventId;
}
```

```text
error HRDR005: Route '/events/{eventid}' on 'EventController.ById' declares '{eventid}', which no parameter binds. 'eventId' differs from it only by case, so it is read from the request body instead. Route tokens bind by exact name: spell the token '{eventId}', or rename the parameter to 'eventid'.
```

`HRDR013` names the interface member and points at it in the source. The other four carry no file location. The [Diagnostics](/reference/diagnostics) reference lists every code.

## Limits

A GET handler that returns null answers GET with 404 and HEAD with 200.

A controller must be declared in a namespace. A controller in the global namespace stops the web generator with warning `CS8785` ("Sequence contains no elements"). The web generator then writes no route for the project.

## Next

| Page | Covers |
|---|---|
| [Route links](/guide/route-links) | Building paths and links to these routes from generated members |
| [Registered routes](/guide/registered-routes) | Routes whose paths are computed at startup |
| [Parameter binding](/guide/parameter-binding) | Where a handler's other parameters come from |
| [Declared responses](/guide/responses) | A handler that answers more than one status |
| [The execution pipeline](/guide/execution-pipeline) | The filters that run around a handler |
