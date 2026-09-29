# Authentication

`[Authorize<TScheme>]` on a handler requires an authenticated caller. An `IPrincipalSource` reads a
request's credential and returns the caller.

`TScheme` is a class that names the authentication scheme:

```csharp
using Hardened.Requests.Abstract.Authorization;

namespace Todos;

[HttpAuthenticationScheme("bearer", BearerFormat = "JWT")]
public sealed class BearerAuth : IAuthenticationScheme;
```

A handler names the class in the attribute:

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;

namespace Todos;

public class TodoController
{
    [Operation("createTodo")]
    [Post("/")]
    [Authorize<BearerAuth>]
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

```http
POST /todos
Content-Type: application/json

{"title":"Write the docs"}

HTTP/1.1 401 Unauthorized
Content-Type: application/json
WWW-Authenticate: Bearer

{"type":"AuthorizationException","message":"This request requires authentication.","details":""}
```

An anonymous request to a handler that requires a caller gets 401 with `WWW-Authenticate: Bearer`.
The handler does not run. A request that no principal source authenticates is anonymous. Until the
application registers a source, every request is anonymous.

## Declaring a scheme

A scheme is a class that implements `IAuthenticationScheme`, an interface with no members. One
attribute on the class says what kind of scheme it is. The attribute's arguments become the scheme's
entry in `components.securitySchemes` in the OpenAPI document. The entry's key is the class name,
such as `BearerAuth`.

::: v-pre

| Attribute | Arguments | Entry in `components.securitySchemes` |
|---|---|---|
| `[HttpAuthenticationScheme(scheme)]` | `scheme`: the HTTP authentication scheme, such as `bearer`, `basic` or `digest`. `BearerFormat`: a hint, such as `JWT` | `{"type": "http", "scheme": "bearer", "bearerFormat": "JWT"}` |
| `[ApiKeyAuthenticationScheme(name, location)]` | `name`: the header, query parameter or cookie that carries the key. `location`: `ApiKeyLocation.Header`, `Query` or `Cookie` | `{"type": "apiKey", "name": "X-Api-Key", "in": "header"}` |
| `[OAuth2AuthenticationScheme(flow)]` | `flow`: `OAuth2Flow.AuthorizationCode`, `ClientCredentials`, `Implicit` or `Password`. `AuthorizationUrl`, `TokenUrl` and `RefreshUrl` | `{"type": "oauth2", "flows": {"clientCredentials": {"tokenUrl": "https://login.example.com/oauth/token", "scopes": {}}}}` |

:::

Each attribute also takes `Description`, which becomes the entry's `description`. An OAuth2 entry's
`scopes` is always empty. The grants that an operation requires under an OAuth2 scheme are listed on
the operation. [The OpenAPI document](/guide/openapi-document) covers what an operation publishes.

`IAuthenticationScheme` and the three scheme attributes are in
`Hardened.Requests.Abstract.Authorization`. `[Authorize<TScheme>]` is in
`Hardened.Requests.Runtime.Authorization`. Both namespaces come with `Hardened.Web.Runtime`.

A scheme reaches the document only when a handler names it in `[Authorize<TScheme>]`. A scheme class
without one of the three attributes is still enforced: `[Authorize<TScheme>]` answers 401 to an
anonymous caller. The document gets no entry for it. The operation of a handler that names it lists
the 401 and no `security`.

One of the three attributes on a handler method, a controller class or a module class publishes
nothing and enforces nothing. The build reports warning `HRDSC001`.
`[HttpAuthenticationScheme("bearer")]` on the `TodosLibrary` module gives this warning:

```console
CSC : warning HRDSC001: 'TodosLibrary' carries [HttpAuthenticationScheme], which nothing reads in this position. The attribute describes an authentication scheme type: declare a class implementing IAuthenticationScheme, put the attribute on it, and name it as [Authorize<TScheme>] on the handler or controller. Where it is now, it publishes no securitySchemes and enforces nothing.
```

## Requiring a caller

`[Authorize<TScheme>]` on a method covers that handler. On a controller class, it covers every
handler in the class. On a `[HardenedModule]` class, it covers every handler compiled with the
module. A method or a class takes one `[Authorize<TScheme>]`. A second one that names another scheme
fails the build with `CS0579`, `Duplicate 'Authorize<>' attribute`.

The attribute requires only an authenticated caller. It does not check which scheme or which source
authenticated the caller. A caller that an API-key source authenticated passes
`[Authorize<BearerAuth>]`.

A handler with no authorization attribute stays public. `[RequireAuthorization]` on the application
requires a caller on every handler. [Authorization](/guide/authorization) covers
`[RequireAuthorization]`, `[AuthorizeGrants]`, policies and the 403.
[The OpenAPI document](/guide/openapi-document) covers what `[Authorize<TScheme>]` publishes: the
operation's `security`, a 401 and a 403.

On a module class, the attribute acts as though every controller class compiled with the module
carried it. Each operation lists the scheme under `security`, with the 401 and the 403. A handler
with `[AllowAnonymous]` on its method or its class stays public, and its operation lists none of
them. `[AuthorizeGrants]` and `[Authorize<TScheme, TPolicy>]` on a module class work the same way.

```csharp
using Hardened.Requests.Runtime.Authorization;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.OpenApi;

namespace Todos;

[HardenedModule]
[Authorize<BearerAuth>]
[Enable<OpenApiDocumentPublishing>]
public partial class TodosLibrary;
```

The module's attribute covers only the handlers compiled in its project. A host's handlers are not
covered by a library module's attribute. The framework's own routes, such as the health endpoints
and `/openapi.json`, are not covered either. [Authorization](/guide/authorization) lists the places
where the build does not read an authorization attribute.

## JWT bearer tokens

`Hardened.Requests.Jwt` authenticates a bearer token as a JWT. `[JwtBearerAuthentication<TScheme>]`
on a module turns it on for a scheme. In a project from the `hardened-web` template, it goes on the
library module:

```csharp
using Hardened.Requests.Jwt;
using Hardened.Shared.Runtime.Attributes;

namespace Todos;

[HardenedModule]
[JwtBearerAuthentication<BearerAuth>]
public partial class TodosLibrary;
```

The host's `Application` imports the library module with `[TodosLibrary]`, so the running host
authenticates tokens too. On `Application`, the attribute would authenticate the running host and
not the tests. [Testing with tokens](#testing-with-tokens) says why.

The package is the only one that brings `Microsoft.IdentityModel.JsonWebTokens` and its
dependencies, so an application that does not reference it does not get them.

These environment variables configure it. `JwtBearerConfiguration`, in `Hardened.Requests.Jwt`, is
the configuration model, so an application can amend each value in code:

| Variable | Default | What it sets |
|---|---|---|
| `JWT_ISSUER` | Required | The `iss` a token must carry |
| `JWT_AUDIENCE` | Required | The `aud` a token must carry |
| `JWT_AUTHORITY` | None | An OpenID Connect issuer. The signing keys are read from the `jwks_uri` its `/.well-known/openid-configuration` names |
| `JWT_JWKS_URL` | None | The JWKS the signing keys are read from, when `JWT_AUTHORITY` is not set |
| `JWT_GRANT_CLAIM` | `scope` | The claim a caller's grants are read from. A string value is split on spaces, and an array gives one grant for each element |
| `JWT_CLOCK_SKEW_SECONDS` | `60` | How far a token's `exp` and `nbf` may be off |

An application without `JWT_ISSUER`, `JWT_AUDIENCE`, or one of `JWT_AUTHORITY` and `JWT_JWKS_URL`
stops at startup, with a message that names what is missing. So does one whose `JWT_AUTHORITY` or
`JWT_JWKS_URL` is not `https`. A loopback address may use `http`.

A token is accepted when all of these hold:

- Its signature verifies against a key from the JWKS, with an `RS`, `PS` or `ES` algorithm. A token
  signed with an `HS` algorithm, or not signed, is refused.
- Its `iss` and `aud` are the configured ones.
- It carries an `exp`, and its `exp` and `nbf` hold within the skew.

An accepted token becomes a `CallerPrincipal`. Its scheme is `bearer`, its subject is the `sub`,
its issuer is the `iss`, and its grants come from the `JWT_GRANT_CLAIM` claim. `TryGetClaim` reads
every other claim.

A refused token leaves the request anonymous, as [Rejecting a credential](#rejecting-a-credential)
describes. A handler that requires a caller answers 401, and the challenge says why:

```http
POST /todos
Authorization: Bearer eyJhbGciOiJSUzI1NiIsImtpZCI6...
Content-Type: application/json

{"title":"Write the docs"}

HTTP/1.1 401 Unauthorized
Content-Type: application/json
WWW-Authenticate: Bearer error="invalid_token", error_description="The token expired at 2026-09-28T14:52:00.0000000Z."

{"type":"AuthorizationException","message":"This request requires authentication.","details":""}
```

A handler that requires nothing runs with an anonymous caller, as it does for a request with no
token.

| The token | `error_description` |
|---|---|
| Has expired | `The token expired at <time>.` |
| Is not valid yet | `The token is not valid until <time>.` |
| Has an `nbf` after its `exp` | `The token is invalid.` |
| Has no `exp` | `The token has no expiry.` |
| Is for another audience | `The token is not for this audience.` |
| Is from another issuer | `The token's issuer is not accepted.` |
| Has a `kid` the JWKS does not hold, or has no `kid` and no key verifies it | `No known key signed the token.` |
| Has a `kid` the JWKS holds, and that key does not verify it | `The token's signature is invalid.` |
| Is not signed | `The token's signature is invalid.` |
| Is not a JWT | `The token is not a JWT.` |
| Has three segments like a JWT, but they do not decode | `The token is malformed.` |

The signing keys are fetched at startup and kept for 24 hours. A token that names a key the set does
not hold fetches them again, at most once a minute, which is how a rotated key is picked up. A fetch
that fails keeps the keys already held.

A fetch that fails at startup logs a warning, and the application starts. Until a fetch succeeds, a
request that carries a token fails with 500, and the keys are fetched again at most once a minute. A
request with no token does not need the keys.

### A contract-first project

In a contract-first project, the contract declares the scheme and each operation's `security`. The
build reads the requirement from the contract, so the handlers carry no `[Authorize<TScheme>]`.
[Security](/guide/openapi#security) covers what each declaration requires. A contract that
requires a bearer token on every operation declares:

```yaml
components:
  securitySchemes:
    bearerAuth:
      type: http
      scheme: bearer
      bearerFormat: JWT

security:
  - bearerAuth: []
```

`[JwtBearerAuthentication<TScheme>]` still needs a scheme class to name:

```csharp
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Jwt;
using Hardened.Shared.Runtime.Attributes;

namespace Todos;

public sealed class BearerAuth : IAuthenticationScheme;

[HardenedModule]
[JwtBearerAuthentication<BearerAuth>]
public partial class TodosLibrary;
```

Nothing ties `BearerAuth` to the contract's `bearerAuth`. An operation checks only that the caller
is authenticated, not which scheme authenticated it, so the JWT source serves every operation whose
`security` names `bearerAuth`. The class needs no `[HttpAuthenticationScheme]`, because the served
document is the contract.

An operation whose `security` lists scopes under an `oauth2` or `openIdConnect` scheme requires
those scopes as grants. The JWT source reads a caller's grants from the `JWT_GRANT_CLAIM` claim,
which is `scope` unless configured otherwise.

### Testing with tokens

`Hardened.Requests.Jwt.Testing` signs tokens for a test. `[JwtTestIssuer]` makes the application
under test trust the test's key in place of its JWKS, so a test runs the real validator. Where the
application sets no `JWT_ISSUER` or `JWT_AUDIENCE`, the attribute sets `https://issuer.hardened.test`
and `hardened-test`. No JWKS is fetched.

`[JwtTestIssuer]` replaces the key source that `[JwtBearerAuthentication<TScheme>]` registers. It
does not register the principal source, so the application under test needs the attribute on a
module it loads. A test application is built from the module that
`[assembly: HardenedTestEntryPoint]` names, and the template's tests name the library module.
There, the tests see the attribute. On the host's `Application`, the tests authenticate no token,
and every handler that requires a caller answers 401.

A test takes `TestJwtIssuer` as a parameter. `Token` signs a token the application accepts, and its
optional argument changes what the test is testing:

```csharp
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Jwt.Testing;

[assembly: JwtTestIssuer]

namespace Todos.Tests;

public class TodoAuthenticationTests
{
    [ModuleTest]
    public async Task AnExpiredTokenIsToldItIsInvalid(ITestWebApp app, TestJwtIssuer issuer)
    {
        var token = issuer.Token(jwt => jwt.Expires = DateTime.UtcNow.AddMinutes(-10));

        var response = await app.Post(
            new NewTodo("Write the docs"),
            "/todos",
            request => request.Headers[KnownHeaders.Authorization] = "Bearer " + token
        );

        Assert.Equal(401, response.StatusCode);
    }
}
```

`TestJwt` has these members:

| Member | Starts as |
|---|---|
| `Subject` | `integration-test`. Null leaves out `sub` |
| `Grants` | Empty. Written into the `JWT_GRANT_CLAIM` claim, joined with spaces |
| `Claims` | Empty. Any other claims |
| `Issuer`, `Audience` | The ones the application accepts |
| `Expires` | An hour from now |
| `NotBefore` | Now, or an hour before `Expires` when that has passed |
| `SignedByUntrustedKey` | `false`. `true` signs with a key the application does not trust |

## Writing a principal source

`IPrincipalSource<TScheme>` has one method, `Authenticate(IExecutionContext context)`, which returns
`ValueTask<ICallerPrincipal?>`. A source returns a `CallerPrincipal` for a credential that it
accepts, and `AnonymousCallerPrincipal.Rejected` for one that it refuses. It returns null for a
request that carries no credential it reads.

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;

namespace Todos;

[SingletonService]
public class BearerPrincipalSource(ITokenValidator tokens) : IPrincipalSource<BearerAuth>
{
    public async ValueTask<ICallerPrincipal?> Authenticate(IExecutionContext context)
    {
        if (!context.Request.Headers.TryGetValue("Authorization", out var header))
        {
            return null;
        }

        var value = header.ToString();

        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = await tokens.Validate(value["Bearer ".Length..]);

        if (token is null)
        {
            return AnonymousCallerPrincipal.Rejected("The token is not valid.");
        }

        return new CallerPrincipal("bearer", token.Scopes, token.Subject, token.Issuer);
    }
}
```

The example validates the token through `ITokenValidator`, an interface of the application's own.
`DevelopmentTokenValidator` is a stand-in. It accepts one token, `ada-token`:

```csharp
using DependencyModules.Runtime.Attributes;

namespace Todos;

public record ValidatedToken(string Subject, string Issuer, IReadOnlyList<string> Scopes);

public interface ITokenValidator
{
    ValueTask<ValidatedToken?> Validate(string token);
}

[SingletonService]
public class DevelopmentTokenValidator : ITokenValidator
{
    public ValueTask<ValidatedToken?> Validate(string token) =>
        ValueTask.FromResult(
            token == "ada-token"
                ? new ValidatedToken("ada", "https://login.example.com", ["todos:write"])
                : null
        );
}
```

With the token, `POST /todos` reaches the handler:

```http
POST /todos
Authorization: Bearer ada-token
Content-Type: application/json

{"title":"Write the docs"}

HTTP/1.1 201 Created
Content-Type: application/json
Location: /todos/3

{"id":3,"title":"Write the docs","done":false}
```

The principal that a source returns is `IExecutionContext.CallerPrincipal` for the rest of the
request. Every filter and the handler see it. [The execution pipeline](/guide/execution-pipeline)
covers where authentication runs.

`[SingletonService]` registers the example as `IPrincipalSource<BearerAuth>`. A source registered as
`IPrincipalSource<TScheme>` or as `IPrincipalSource` is asked. The two interfaces work the same. The
type argument says which scheme the source serves. Nothing checks it.

The sources are resolved once, at startup. Every request uses the same instances, whatever lifetime
they are registered with.

A source runs for every request, before routing, including a request that no route matches, a CORS
preflight and a request to a health endpoint. `context.HandlerInfo` is null when a source runs.

`BearerPrincipalSource<TScheme>`, in `Hardened.Requests.Runtime.Authorization`, reads the
`Authorization` header the way the example does and passes the token to a delegate. The delegate
returns the caller, or null for a token it refuses, which becomes `AnonymousCallerPrincipal.Rejected`.
A request with no bearer token is not the source's, and the next source is asked:

```csharp
services.AddSingleton<IPrincipalSource<BearerAuth>>(provider =>
{
    var tokens = provider.GetRequiredService<ITokenValidator>();

    return new BearerPrincipalSource<BearerAuth>(async (token, _) =>
        await tokens.Validate(token) is { } valid
            ? new CallerPrincipal("bearer", valid.Scopes, valid.Subject, valid.Issuer)
            : null
    );
});
```

The [JWT source](#jwt-bearer-tokens) is this source with a JWT validator as its delegate.

## Rejecting a credential

A source returns `AnonymousCallerPrincipal.Rejected(description)` for a credential that it
rejects, such as an expired token. The request continues as anonymous. If the handler requires a
caller, the request gets 401 with `error="invalid_token"`, and the description as
`error_description`:

```http
POST /todos
Authorization: Bearer expired-token
Content-Type: application/json

{"title":"Write the docs"}

HTTP/1.1 401 Unauthorized
Content-Type: application/json
WWW-Authenticate: Bearer error="invalid_token", error_description="The token is not valid."

{"type":"AuthorizationException","message":"This request requires authentication.","details":""}
```

A handler that requires nothing runs with an anonymous caller. The rejection is an answer, so no
later source is asked. A source that returns null for a credential it rejects leaves the next source
to answer, and the request gets the plain `WWW-Authenticate: Bearer` of a request with no
credential.

A source cannot refuse a request itself. Any exception from a source fails the request,
`AuthorizationException` included. The caller gets 500 with an empty body. The request is logged as
failed.

## Several sources

An application can register several sources, such as one for each scheme. `ApiKeyPrincipalSource`
serves an API-key scheme:

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;

namespace Todos;

[ApiKeyAuthenticationScheme("X-Api-Key", ApiKeyLocation.Header)]
public sealed class ApiKeyAuth : IAuthenticationScheme;

[SingletonService]
public class ApiKeyPrincipalSource : IPrincipalSource<ApiKeyAuth>
{
    public ValueTask<ICallerPrincipal?> Authenticate(IExecutionContext context)
    {
        if (
            !context.Request.Headers.TryGetValue("X-Api-Key", out var key)
            || key.ToString() != "build-server-key"
        )
        {
            return ValueTask.FromResult<ICallerPrincipal?>(null);
        }

        return ValueTask.FromResult<ICallerPrincipal?>(
            new CallerPrincipal("api-key", ["todos:write"], subject: "build-server")
        );
    }
}
```

With `[Authorize<BearerAuth>]` still on `Create`, a request with the key reaches the handler:

```http
POST /todos
X-Api-Key: build-server-key
Content-Type: application/json

{"title":"Tag the release"}

HTTP/1.1 201 Created
Content-Type: application/json
Location: /todos/3

{"id":3,"title":"Tag the release","done":false}
```

The sources are asked in order. The first source to return a principal authenticates the request.
The rest are not asked. A source that returns null passes the request to the next. A request that
carries two credentials is authenticated by the first source that accepts one.

`[SingletonService]` registers the sources of one project in order of class name, so
`ApiKeyPrincipalSource` is asked before `BearerPrincipalSource`. To choose the order, take
`[SingletonService]` off the sources. Register each one in `ConfigureServices` as `IPrincipalSource`,
in the order in which to ask them:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Abstract.Authorization;
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
[Server("http://localhost:5080", "Local")]
[Enable<OpenApiDocumentPublishing>]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);

        services.AddSingleton<IPrincipalSource, BearerPrincipalSource>();
        services.AddSingleton<IPrincipalSource, ApiKeyPrincipalSource>();
    }
}
```

With this module, a request that carries both `Authorization: Bearer ada-token` and
`X-Api-Key: build-server-key` is authenticated as `ada`. With `[SingletonService]` on both sources,
the same request is authenticated as `build-server`.

Sources registered under different interfaces are asked one interface at a time, in the order each
interface was first registered:

| Registered | Interface | Asked |
|---|---|---|
| First | `IPrincipalSource` | First |
| Second | `IPrincipalSource<ApiKeyAuth>` | Third |
| Third | `IPrincipalSource` | Second |

## The 401 response

A principal source never answers 401. `AuthorizationFilter` answers it when the handler has a
requirement and the caller is anonymous. An anonymous caller gets the same 401 from every
requirement: `[Authorize<TScheme>]`, `[AuthorizeGrants]`, a policy, and `[RequireAuthorization]` on
a handler with no attribute.

For `[Authorize<TScheme>]` and for grants, the filter decides before the request body is read. A
request with no credential and a malformed body gets 401, not 400. A requirement that reads bound
parameters is decided after the body is read. [Authorization](/guide/authorization) covers such
requirements.

The body of the 401 is an `ErrorModel` whose message is "This request requires authentication." The
request in the first example is logged at `Warning` in the category
`Hardened.Requests.Runtime.Logging.RequestLogger`, with event id 78004 and this message:

```text
POST /todos refused with 401: This request requires authentication.
```

An authenticated caller gets a 401 too when a grant handler refuses it with
`AuthorizationDecision.DenyInsufficientAuthentication`. An authenticated caller without the grants
gets 403. [Authorization](/guide/authorization) covers grant handlers and the 403.

The challenge names `Bearer` whatever scheme the handler names. The filter's challenges carry no
`realm`. `AuthorizationChallenge`, in `Hardened.Requests.Abstract.Authorization`, has a factory for
each challenge:

| Factory | Status | `WWW-Authenticate` | Hardened sends it when |
|---|---|---|---|
| `AuthenticationRequired()` | 401 | `Bearer` | The caller is anonymous, and no source rejected its credential |
| `InsufficientAuthentication()` | 401 | `Bearer error="insufficient_user_authentication"` | A grant handler refuses an authenticated caller with `DenyInsufficientAuthentication` |
| `InvalidToken()` | 401 | `Bearer error="invalid_token"` | The caller's source returned `AnonymousCallerPrincipal.Rejected`. Its description is the `error_description` |
| `InsufficientScope(grants)` | 403 | `Bearer error="insufficient_scope", scope="todos:admin"` | An authenticated caller lacks a grant |

Each factory takes an optional `realm`. `InvalidToken` and `InsufficientAuthentication` also take a
`description`. The header carries them as `realm="..."` and `error_description="..."`.

A handler sends a challenge by returning `Unauthorized` with it.
`new Unauthorized("The token expired.", AuthorizationChallenge.InvalidToken(realm: "todos", description: "The token expired."))`
answers 401 with
`WWW-Authenticate: Bearer realm="todos", error="invalid_token", error_description="The token expired."`.
[Declared responses](/guide/responses) covers `Unauthorized`.

## Reading the caller

`ICurrentCaller.Principal` is the request's caller. A handler takes `ICurrentCaller` in its class's
constructor or as a method parameter. `HardenedRequestModule` registers it as a scoped service. The
application does not need to register it. A handler that takes an `IExecutionContext` parameter
reads the same caller from `context.CallerPrincipal`.

```csharp
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class ProfileController(ICurrentCaller caller)
{
    [Operation("getProfile")]
    [Get("/profile")]
    [Authorize<BearerAuth>]
    public string Profile() =>
        $"{caller.Principal.Subject} ({caller.Principal.AuthenticationScheme})";

    [Operation("whoAmI")]
    [Get("/whoami")]
    public string WhoAmI(IExecutionContext context) =>
        context.CallerPrincipal.Subject ?? "anonymous";
}
```

```http
GET /todos/profile
Authorization: Bearer ada-token

HTTP/1.1 200 OK
Content-Type: application/json

"ada (bearer)"
```

```http
GET /todos/whoami

HTTP/1.1 200 OK
Content-Type: application/json

"anonymous"
```

For a request that no source authenticated, the principal is `AnonymousCallerPrincipal.Instance`. It
is never null.

A contract-first handler class implements a generated interface, so its method signatures are fixed.
It takes `ICurrentCaller` in its constructor.

## The principal

An `ICallerPrincipal`, in `Hardened.Requests.Abstract.Authorization`, has these members:

| Member | On a `CallerPrincipal` | On `AnonymousCallerPrincipal.Instance` |
|---|---|---|
| `AuthenticationScheme` | The scheme name it was built with, such as `bearer` | null |
| `IsAuthenticated` | true | false |
| `Subject` | Who the caller is | null |
| `Issuer` | Who vouched for the caller | null |
| `Grants` | The grants it was built with | Empty |
| `TryGetClaim(name, out value)` | A claim it was built with | Always false |

`IsAuthenticated` is true when `AuthenticationScheme` is not null.

The `CallerPrincipal(authenticationScheme, grants, subject, issuer, claims)` constructor requires
only the scheme name. An empty scheme name throws `ArgumentException`. The scheme name is a string
that the source chooses. It need not match the name of the scheme class. The example's source writes
`bearer` for `BearerAuth`.

Grant names and claim names are compared case-sensitively, so `todos:read` and `Todos:Read` are two
grants. A claim name given twice keeps its last value.

`[AuthorizeGrants]` first checks the grants that a source puts on the principal. A grant handler
resolves grants held elsewhere, such as in a database, for each request.
[Authorization](/guide/authorization) covers both.

## ASP.NET Core authentication

On the ASP.NET Core host, ASP.NET Core's authentication does not set the caller. A handler reads an
anonymous caller even when `HttpContext.User` is authenticated. A principal source in the host
project can read `HttpContext.User` through `IHttpContextAccessor`:

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Microsoft.AspNetCore.Http;

namespace Todos.Host;

[SingletonService]
public class AspNetCoreUserSource(IHttpContextAccessor accessor) : IPrincipalSource
{
    public ValueTask<ICallerPrincipal?> Authenticate(IExecutionContext context)
    {
        var user = accessor.HttpContext?.User;

        if (user?.Identity is not { IsAuthenticated: true } identity)
        {
            return ValueTask.FromResult<ICallerPrincipal?>(null);
        }

        return ValueTask.FromResult<ICallerPrincipal?>(
            new CallerPrincipal(
                identity.AuthenticationType ?? "aspnetcore",
                subject: user.FindFirst("sub")?.Value
            )
        );
    }
}
```

`src/Todos.Host/Program.cs` registers the accessor:

```csharp
builder.Services.AddHttpContextAccessor();
```

`app.UseAuthentication()` goes before `app.UseHardened()`:

```csharp
app.UseAuthentication();

app.UseHardened();
```

Placed after `app.UseHardened()`, `app.UseAuthentication()` has not run when the source reads the
user. The caller is then anonymous. The application configures ASP.NET Core authentication with
`AddAuthentication`, as in any ASP.NET Core application.

## Cookies and cross-site requests

A browser sends a site's cookies with requests that pages on other sites make, as far as each
cookie's `SameSite` attribute allows. A credential that travels in a cookie, such as an API key
declared with `ApiKeyLocation.Cookie`, therefore reaches a handler in a request that another site's
page started. That is cross-site request forgery. A page on another site can send a `POST` with a
url-encoded, multipart or plain-text body without a CORS preflight. CORS then stops the page from
reading the answer, not the request from reaching the handler.

Hardened ships no anti-forgery check. An application that authenticates by cookie, and has handlers
that change state, needs one of these:

- `SameSite=Strict` or `SameSite=Lax` on the credential's cookie. `Lax` still sends the cookie
  with a top-level `GET` from another site, so a `GET` handler must not change state.
- A request header such as `X-Requested-With`, required on every handler that changes state. A
  browser sends it from another site only after a preflight, and [CORS](/guide/cors) passes a
  preflight only for the origins it allows.
- An `Origin` header that names the application's own site, required on every handler that changes
  state.

A credential in the `Authorization` header, such as a bearer token or an API key sent as a header,
is not sent by the browser on its own, and none of this applies to it.

## Limits

Hardened ships one principal source for production credentials, the JWT source in
`Hardened.Requests.Jwt`. It has no cookie or API-key reader, and the application writes its own.

The JWT source reads one configuration. Two schemes named with `[JwtBearerAuthentication<TScheme>]`
accept the same issuer and audience. It cannot verify a token signed with a shared secret.

## Next

| Page | Covers |
|---|---|
| [Authorization](/guide/authorization) | Grants, policies, deny by default and the 403 |
| [The OpenAPI document](/guide/openapi-document) | What `[Authorize<TScheme>]` publishes in the document |
| [Sending requests](/guide/testing-web) | Sending a test request as a caller |
| [The execution pipeline](/guide/execution-pipeline) | The middleware and filters around a handler |
| [CORS](/guide/cors) | Requests from a browser on another origin |
