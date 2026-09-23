# Authorization

`[AuthorizeGrants]` names the grants a handler requires. A grant is a string, such as `todos:read`.

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;
using ValidationModules.Constraints;

namespace Todos;

public class TodoController
{
    [Get("/{id}")]
    [AuthorizeGrants("todos:read")]
    public async Task<Response<Todo, NotFound>> ById(ITodoStore store, [Range(Min = 1)] int id)
    {
        var todo = await store.Find(id);

        if (todo is null)
        {
            return new NotFound("todo", $"No todo has id {id}.");
        }

        return todo;
    }
}
```

The examples on this page run in an application made with `dotnet new hardened-web -n Todos`. Its
routes answer under `/todos`, which the library module sets with `[BasePath("/todos")]`.

The caller's grants are the grants on the principal that authentication established for the
request. The application in the examples holds a principal source that accepts the bearer tokens in
this table. A request sends its token as `Authorization: Bearer <token>`.
[Authentication](/guide/authentication) covers principal sources.

| Token | The caller holds |
|---|---|
| none | No authenticated caller |
| `nobody` | No grants |
| `reader` | `todos:read` |
| `editor` | `todos:read` and `todos:write` |
| `admin` | `todos:admin` |
| `pia` | `todos:read`. Its subject is `pia` |
| `acme` | `todos:read`, and the claim `tenant` with the value `acme` |

```http
GET /todos/1
Authorization: Bearer reader

HTTP/1.1 200 OK
Content-Type: application/json

{"id":1,"title":"Read the generated code","done":true}
```

```http
GET /todos/1
Authorization: Bearer nobody

HTTP/1.1 403 Forbidden
Content-Type: application/json
WWW-Authenticate: Bearer error="insufficient_scope", scope="todos:read"

{"type":"AuthorizationException","message":"This request is not permitted.","details":""}
```

A caller that holds every required grant reaches the handler. An authenticated caller that lacks a
required grant gets 403. A request with no authenticated caller gets 401. The handler does not run
for a refused request.

## Namespaces and packages

The authorization types are in two namespaces:

| Namespace | Types |
|---|---|
| `Hardened.Requests.Runtime.Authorization` | `AuthorizeGrantsAttribute`, `AuthorizeAttribute`, `AllowAnonymousAttribute`, `RequireAuthorizationAttribute` and `IGrantProvider` |
| `Hardened.Requests.Abstract.Authorization` | `Requirement`, `AuthorizationPolicy` and the other authorization interfaces |

The template's library project references `Hardened.Requests.Runtime` and
`Hardened.Requests.Abstract` through `Hardened.Web.Runtime`.

## Requiring grants

`[AuthorizeGrants("todos:read", "todos:write")]` requires every grant it names.

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;

namespace Todos;

public class TodoController
{
    [Post("/")]
    [AuthorizeGrants("todos:read", "todos:write")]
    public async Task<Response<Created<Todo>, Conflict>> Create(ITodoStore store, NewTodo request)
    {
        if (await store.TitleExists(request.Title))
        {
            return new Conflict($"A todo titled '{request.Title}' already exists.");
        }

        var todo = await store.Add(request.Title);

        return new Created<Todo>(todo, $"/todos/{todo.Id}");
    }
}
```

A handler requires every authorization declaration that applies to it: each attribute on its method,
each attribute on its class, and the requirement of each convention, which
[Conventions](#conventions) covers. Two `[AuthorizeGrants]` on one method combine the same way.

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;
using ValidationModules.Constraints;

namespace Todos;

[AuthorizeGrants("todos:read")]
public class TodoController
{
    [Delete("/{id}")]
    [AuthorizeGrants("todos:write")]
    public async Task<Response<NoContent, NotFound>> Remove(
        ITodoStore store,
        [Range(Min = 1)] int id
    )
    {
        if (await store.Find(id) is null || !await store.Remove(id))
        {
            return new NotFound("todo", $"No todo has id {id}.");
        }

        return new NoContent();
    }
}
```

```http
DELETE /todos/2
Authorization: Bearer reader

HTTP/1.1 403 Forbidden
Content-Type: application/json
WWW-Authenticate: Bearer error="insufficient_scope", scope="todos:write todos:read"

{"type":"AuthorizationException","message":"This request is not permitted.","details":""}
```

```http
DELETE /todos/2
Authorization: Bearer editor

HTTP/1.1 204 No Content
```

`Requirement.AnyOf`, or `|`, offers a choice between grants inside one requirement. A policy or an
attribute of your own can hold such a requirement. [Attributes of your own](#attributes-of-your-own)
and [Policies](#policies) cover them. A contract's `security` list also offers a choice, which
[Generating from OpenAPI](/guide/openapi) covers. Separate attributes never offer a choice.

Authorization attributes are read from the handler's method and from the class declaration that
contains the method. [Limits](#limits) lists the places they are not read. A route registered with
a lambda reads them from the lambda, which [Registered routes](/guide/registered-routes) covers.

A grant matches only the identical string. `CallerPrincipal` compares grants case-sensitively.
Nothing expands a wildcard. A caller that holds `todos:*` or `TODOS:READ` does not satisfy
`todos:read`.

## Grant sets

A class that implements `IGrantProvider` names a set of grants in its `Grants` property. The class
needs a public parameterless constructor. `[AuthorizeGrants<T>]` requires every grant in the set.

```csharp
using Hardened.Requests.Runtime.Authorization;

namespace Todos;

public sealed class TodoEditors : IGrantProvider
{
    public string[] Grants => ["todos:read", "todos:write"];
}
```

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;

namespace Todos;

public class TodoController
{
    [Post("/")]
    [AuthorizeGrants<TodoEditors>]
    public async Task<Response<Created<Todo>, Conflict>> Create(ITodoStore store, NewTodo request)
    {
        if (await store.TitleExists(request.Title))
        {
            return new Conflict($"A todo titled '{request.Title}' already exists.");
        }

        var todo = await store.Add(request.Title);

        return new Created<Todo>(todo, $"/todos/{todo.Id}");
    }
}
```

`Grants` is read once for each handler that names the set, when the first request reaches that
handler. A set that changes while the application runs belongs in an
`IActivityAuthorizationHandler`, which [Grants from a store](#grants-from-a-store) covers.

An `[AuthorizeGrants]` that names no grant, or a set whose `Grants` is empty, compiles. The handler
then answers 500 to every request. The log names the attribute.

## Attributes of your own

An attribute that derives from `AuthorizeGrantsAttribute` requires the grants its constructor
passes. `[RequiresTodoWrite]` on a handler requires `todos:read` and `todos:write`.

```csharp
using Hardened.Requests.Runtime.Authorization;

namespace Todos;

public sealed class RequiresTodoWriteAttribute : AuthorizeGrantsAttribute
{
    public RequiresTodoWriteAttribute()
        : base("todos:read", "todos:write") { }
}
```

An attribute that implements `IAuthorizeAttribute` requires the `Requirement` it returns. That
requirement can hold alternatives. `[RequiresEditOrAdmin]` admits a caller that holds `todos:read`
and `todos:write`, or a caller that holds `todos:admin`.

```csharp
using Hardened.Requests.Abstract.Authorization;

namespace Todos;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresEditOrAdminAttribute : Attribute, IAuthorizeAttribute
{
    public Requirement Requirement { get; } =
        (Requirement.Grant("todos:read") & Requirement.Grant("todos:write"))
        | Requirement.Grant("todos:admin");
}
```

Every attribute that implements `IAuthorizeAttribute`, directly or through a base attribute class,
counts as an authorization attribute. The handler requires what the attribute returns. `HAUTH001`,
which [Deny by default](#deny-by-default) covers, does not report the handler.

## Policies

A class that derives from `AuthorizationPolicy` builds a requirement in `Define`.
`[Authorize<TScheme, TPolicy>]` on a handler requires it. The first type argument is an
authentication scheme, a type that implements `IAuthenticationScheme`.
[Authentication](/guide/authentication) covers schemes.

```csharp
using Hardened.Requests.Abstract.Authorization;

namespace Todos;

[HttpAuthenticationScheme("bearer")]
public sealed class BearerAuth : IAuthenticationScheme;

public sealed class CanEditTodos : AuthorizationPolicy
{
    protected override Requirement Define() =>
        (Grant("todos:read") & Grant("todos:write")) | Grant("todos:admin");
}
```

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;
using ValidationModules.Constraints;

namespace Todos;

public class TodoController
{
    [Delete("/{id}")]
    [Authorize<BearerAuth, CanEditTodos>]
    public async Task<Response<NoContent, NotFound>> Remove(
        ITodoStore store,
        [Range(Min = 1)] int id
    )
    {
        if (await store.Find(id) is null || !await store.Remove(id))
        {
            return new NotFound("todo", $"No todo has id {id}.");
        }

        return new NoContent();
    }
}
```

```http
DELETE /todos/2
Authorization: Bearer admin

HTTP/1.1 204 No Content
```

```http
DELETE /todos/2
Authorization: Bearer reader

HTTP/1.1 403 Forbidden
Content-Type: application/json
WWW-Authenticate: Bearer error="insufficient_scope", scope="todos:read todos:write todos:admin"

{"type":"AuthorizationException","message":"This request is not permitted.","details":""}
```

The `scope` in the 403 names every grant the requirement names, across every alternative and
including grants the caller holds.

`[Authorize<TScheme, TPolicy>]` also requires an authenticated caller. A handler whose policy admits
every caller still answers 401 to a request with no authenticated caller. With one type argument,
`[Authorize<TScheme>]` requires an authenticated caller and nothing more.
[Authentication](/guide/authentication) covers it.

`Define` builds the requirement with the helpers `Grant`, `AllOf`, `AnyOf` and `Predicate`, or with
the `&` and `|` operators. The members of `Requirement` admit a caller in these cases:

| Member | Admits a caller when |
|---|---|
| `Requirement.Grant("todos:read")` | It holds `todos:read` |
| `Requirement.AllOf(a, b)`, or `a & b` | Every requirement listed admits it |
| `Requirement.AnyOf(a, b)`, or `a \| b` | At least one requirement listed admits it |
| `Requirement.Authenticated()` | It is authenticated, whatever grants it holds |
| `Requirement.Predicate((caller, context) => ..., "description")` | The function returns true. [Requirements that read the request](#requirements-that-read-the-request) covers it |

`[Authorize<TScheme, TPolicy>]` creates the policy with its public parameterless constructor, not
from the container. `Grant` with an empty string, and `AllOf` or `AnyOf` with nothing to combine,
throw `ArgumentException`.

## Requirements that read the request

`Predicate` takes a function of the caller, an `ICallerPrincipal`, and the request's
`IExecutionContext`. The function reads the handler's bound parameters through
`context.Request.Parameters`. The example compares the route's `tenant` with the caller's `tenant`
claim. The `acme` token's caller carries that claim. [Authentication](/guide/authentication) covers
claims.

```csharp
using Hardened.Requests.Abstract.Authorization;

namespace Todos;

public sealed class SameTenant : AuthorizationPolicy
{
    protected override Requirement Define() =>
        Predicate(
            (caller, context) =>
                caller.TryGetClaim("tenant", out var tenant)
                && context.Request.Parameters is { } parameters
                && parameters.TryGetParameter("tenant", out var requested)
                && Equals(requested, tenant),
            "the caller's tenant"
        );
}
```

`BearerAuth` is the scheme from [Policies](#policies).

```csharp
using Hardened.Requests.Runtime.Authorization;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class TodoController
{
    [Get("/tenants/{tenant}")]
    [Authorize<BearerAuth, SameTenant>]
    public Task<IReadOnlyList<Todo>> ForTenant(ITodoStore store, string tenant) => store.All();
}
```

```http
GET /todos/tenants/acme
Authorization: Bearer acme

HTTP/1.1 200 OK
Content-Type: application/json

[{"id":1,"title":"Read the generated code","done":true},{"id":2,"title":"Add an endpoint","done":false}]
```

```http
GET /todos/tenants/globex
Authorization: Bearer acme

HTTP/1.1 403 Forbidden
Content-Type: application/json
WWW-Authenticate: Bearer error="insufficient_scope"

{"type":"AuthorizationException","message":"This request is not permitted.","details":""}
```

A requirement that names no grant, such as a predicate, refuses an authenticated caller with 403 and
`WWW-Authenticate: Bearer error="insufficient_scope"`, with no `scope`.

A requirement with no predicate, such as grants alone or an authenticated caller, is checked at
`FilterOrder.GrantAuthorization`, before the request body is read. A caller it refuses gets 401 or
403 whatever the body holds.

A requirement that contains a predicate is checked at `FilterOrder.Authorization`, after the
request is bound and validated. The whole requirement is checked there, including the authenticated
caller that `[Authorize<TScheme, TPolicy>]` adds. A request with an invalid body then gets the
validation 400 before any refusal, even with no credentials.

[The execution pipeline](/guide/execution-pipeline) covers the two positions.

## Conventions

A class that implements `IAuthorizationConvention` adds a requirement to the handlers it chooses.
`[SingletonService]` registers it. `Apply` returns a `Requirement` for a handler, or null to add
nothing.

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;

namespace Todos;

[SingletonService]
public class WritesNeedTodoWrite : IAuthorizationConvention
{
    public Requirement? Apply(IExecutionRequestHandlerInfo handlerInfo) =>
        handlerInfo.Method == "GET" ? null : Requirement.Grant("todos:write");
}
```

`Create`, the handler for `POST /todos`, carries no authorization attribute in this example:

```http
POST /todos
Authorization: Bearer reader
Content-Type: application/json

{"title":"Write the docs"}

HTTP/1.1 403 Forbidden
Content-Type: application/json
WWW-Authenticate: Bearer error="insufficient_scope", scope="todos:write"

{"type":"AuthorizationException","message":"This request is not permitted.","details":""}
```

`Apply` receives the handler's `IExecutionRequestHandlerInfo`: its `Path`, `Method`, `HandlerType`,
`Parameters` and `Metadata`, and the `Requirement` it declared. `Path` is the route template with
the module's base path, such as `/todos/{id}`. `Method` is the verb in capitals, such as `GET`.

`Apply` runs once for each handler in the application, when the first request reaches that handler.
The handlers include the health endpoints, `/openapi.json` and `/docs`. [Hosts](/guide/hosts) and
[The OpenAPI document](/guide/openapi-document) cover those.

A handler requires what a convention returns as well as its own attributes. Every registered
convention applies.

## Public routes

`[AllowAnonymous]` on a handler's method or class makes the handler public. No requirement applies
to it.

```csharp
using Hardened.Requests.Runtime.Authorization;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

[AuthorizeGrants("todos:read")]
public class TodoController
{
    [Get("/")]
    [AllowAnonymous]
    public Task<IReadOnlyList<Todo>> All(ITodoStore store) => store.All();
}
```

```http
GET /todos

HTTP/1.1 200 OK
Content-Type: application/json

[{"id":1,"title":"Read the generated code","done":true},{"id":2,"title":"Add an endpoint","done":false}]
```

`[AllowAnonymous]` overrides every requirement. In each of these cases the handler is public:

| `[AllowAnonymous]` on | The requirement it overrides |
|---|---|
| The method | `[AuthorizeGrants]` on the same method |
| The method | `[AuthorizeGrants]` on the class |
| The class | `[AuthorizeGrants]` on the method |
| The method | A convention's requirement |
| The method | `[RequireAuthorization]` on a module |

On a class, `[AllowAnonymous]` covers every handler in the class.

Without `[RequireAuthorization]`, a handler that carries no authorization attribute, and that no
convention guards, is already public.

## Deny by default

`[RequireAuthorization]` on a `[HardenedModule]` class changes what every unannotated handler in the
application requires. An unannotated handler carries neither an authorization attribute nor
`[AllowAnonymous]`. When no convention guards it, the handler then requires an authenticated caller,
whatever grants the caller holds. A handler that declares a requirement keeps it.

```csharp
using Hardened.Requests.Runtime.Authorization;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Runtime.OpenApi;

namespace Todos.Host;

[HardenedModule]
[KestrelRuntime]
[RequireAuthorization]
[HardenedOpenApiUi(Title = "Todos", Environments = "development")]
[TodosLibrary]
public partial class Application;
```

`All`, the handler for `GET /todos`, carries no authorization attribute:

```http
GET /todos

HTTP/1.1 401 Unauthorized
Content-Type: application/json
WWW-Authenticate: Bearer

{"type":"AuthorizationException","message":"This request requires authentication.","details":""}
```

```http
GET /todos
Authorization: Bearer nobody

HTTP/1.1 200 OK
Content-Type: application/json

[{"id":1,"title":"Read the generated code","done":true},{"id":2,"title":"Add an endpoint","done":false}]
```

`[RequireAuthorization]` also covers the health endpoints, `/openapi.json` and `/docs`.
[Hosts](/guide/hosts) and [The OpenAPI document](/guide/openapi-document) cover them.

In the project of the module that carries `[RequireAuthorization]`, the build reports warning
`HAUTH001` for each unannotated handler compiled there. The warning points at the handler's method
name. `HAUTH001` counts an attribute on the method or on the class that contains it. A convention
does not count. A handler that only a convention guards is still reported.

`[RequireAuthorization]` can go on the application module, on the library module or on the
assembly:

| `[RequireAuthorization]` on | An unannotated handler at run time | `HAUTH001` is reported for |
|---|---|---|
| The application module, `Application` in `src/Todos.Host` | Requires an authenticated caller | Unannotated handlers compiled in `src/Todos.Host`. The template has none there |
| The library module, `TodosLibrary` in `src/Todos` | Requires an authenticated caller | Unannotated handlers compiled in `src/Todos` |
| The assembly, as `[assembly: RequireAuthorization]`, in either project | Stays public | No handler |

`services.RequireAuthorization()` in a module's `ConfigureServices` turns on the same run-time rule.
The build then reports no `HAUTH001`. The method is in `Hardened.Requests.Runtime.Authorization`.

The project that reports `HAUTH001` can change it with these settings:

| Setting | Effect |
|---|---|
| `<NoWarn>$(NoWarn);HAUTH001</NoWarn>` | Silences it |
| `<WarningsAsErrors>$(WarningsAsErrors);HAUTH001</WarningsAsErrors>` | Fails the build |
| A `.globalconfig` file holding `is_global = true` and `dotnet_diagnostic.HAUTH001.severity = none` | Silences it. `= error` fails the build |
| `#pragma warning disable HAUTH001` in the handler's file | None |
| `dotnet_diagnostic.HAUTH001.severity = none` in a `[*.cs]` section of `.editorconfig` | None |

With `[RequireAuthorization]` on `TodosLibrary`, this property in `src/Todos/Todos.csproj` fails the
build with one `error HAUTH001` for each unannotated handler:

```xml
<PropertyGroup>
  <WarningsAsErrors>$(WarningsAsErrors);HAUTH001</WarningsAsErrors>
</PropertyGroup>
```

## Grants from a store

A class that implements `IActivityAuthorizationHandler` vouches for grants the credential does not
carry, such as grants kept in a permissions table. `[SingletonService]` registers it. In the example
a dictionary stands in for the store.

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;

namespace Todos;

[SingletonService]
public class StoredGrants : IActivityAuthorizationHandler
{
    private static readonly Dictionary<string, string[]> GrantsBySubject = new()
    {
        ["pia"] = ["todos:write"],
    };

    public ValueTask<GrantResolution> Resolve(
        IExecutionContext context,
        IReadOnlyList<string> grants
    )
    {
        var subject = context.CallerPrincipal.Subject;

        if (subject is null || !GrantsBySubject.TryGetValue(subject, out var held))
        {
            return ValueTask.FromResult(GrantResolution.Abstained);
        }

        return ValueTask.FromResult(GrantResolution.Granting(grants.Intersect(held)));
    }
}
```

`Remove`, the handler for `DELETE /todos/{id}`, carries
`[AuthorizeGrants("todos:read", "todos:write")]` in this example:

```http
DELETE /todos/2
Authorization: Bearer pia

HTTP/1.1 204 No Content
```

The authorization handler is asked only when the caller's own grants do not satisfy the
requirement. It cannot take away a grant the credential carries. It is not asked for a requirement
that names no grant, such as a predicate alone. It is asked once per request, with every grant the
requirement names.

| `Resolve` returns | Meaning |
|---|---|
| `GrantResolution.Granting(...)` | Vouches for the grants listed |
| `GrantResolution.Abstained` | Has nothing to say. When every authorization handler abstains, the caller's own grants decide, as if no authorization handler were registered |
| `GrantResolution.Refusing(AuthorizationDecision.Deny)` | Refuses the request with 403 |
| `GrantResolution.Refusing(AuthorizationDecision.DenyInsufficientAuthentication)` | Refuses the request with 401 and `WWW-Authenticate: Bearer error="insufficient_user_authentication"` |

The requirement is then checked against the caller's own grants and the vouched grants together.

An application can register several authorization handlers. The grants they vouch for are combined.
One `Deny` refuses the request whatever the others answered. Hardened registers an authorization
handler of its own, which vouches for the grants on the caller's principal.

An authorization handler is asked for a request with no authenticated caller too. The caller's
`Subject` is then null. A grant the authorization handler vouches for admits the request.

A handler can take `IActivityAuthorizationService` and the request's `IExecutionContext` as
parameters. [Parameter binding](/guide/parameter-binding) covers the context. The service's
`Authorize(context, "todos:write")` asks the same authorization handlers and returns an
`AuthorizationDecision`. The decision is `Allow` only when every grant named is vouched for.

## Refusal responses

A refused request gets a JSON body that holds `type`, `message` and `details`. `type` is
`AuthorizationException`. `WWW-Authenticate` names the `Bearer` scheme on every refusal, whatever
scheme the application declares.

| The request | Status | `WWW-Authenticate` | `message` |
|---|---|---|---|
| Has no authenticated caller | 401 | `Bearer` | `This request requires authentication.` |
| Has a caller the requirement refuses | 403 | `Bearer error="insufficient_scope", scope="..."`, naming every grant the requirement names | `This request is not permitted.` |
| Has a caller refused by a requirement that names no grant | 403 | `Bearer error="insufficient_scope"` | `This request is not permitted.` |
| Has a caller for whom an `IActivityAuthorizationHandler` answered `DenyInsufficientAuthentication` | 401 | `Bearer error="insufficient_user_authentication"` | `This request requires authentication.` |

A request with no credentials gets this 401:

```http
GET /todos/1

HTTP/1.1 401 Unauthorized
Content-Type: application/json
WWW-Authenticate: Bearer

{"type":"AuthorizationException","message":"This request requires authentication.","details":""}
```

A request whose credential no principal source accepts has no authenticated caller. It gets the same
401. A principal source can refuse such a credential itself. [Authentication](/guide/authentication)
covers that.

In an application with no principal source, no request has an authenticated caller. Every guarded
handler then answers 401.

## Limits

::: warning
An authorization attribute on a base class, on another declaration of a `partial` class or on a
module class compiles and is not read. `[RequireAuthorization]` on the assembly compiles and is not
read either. Each handler is guarded as if the attribute were absent. Put an authorization attribute
on the handler's method or on the class declaration that contains it, and `[RequireAuthorization]`
on a module class.
:::

The build reports none of these. Under `[RequireAuthorization]`, `HAUTH001` reports the affected
handlers as unannotated.

## Next

| Page | Covers |
|---|---|
| [Authentication](/guide/authentication) | Schemes, principal sources and the caller's grants |
| [The execution pipeline](/guide/execution-pipeline) | Where the two authorization positions sit among the filters |
| [The OpenAPI document](/guide/openapi-document) | The 401 and 403 the attributes publish |
| [Sending requests](/guide/testing-web) | Sending a test request as a caller holding grants |
| [Response caching](/guide/response-caching) | Caching the response of a guarded handler |
