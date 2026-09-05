# Authentication

Authentication decides who the caller is. A handler names the scheme it requires, and a request
that presents nothing is refused before the handler runs:

```csharp
[HttpAuthenticationScheme("bearer", BearerFormat = "JWT")]
public sealed class BearerAuth : IAuthenticationScheme;

public class PetController {
    [Post("/pets")]
    [Authorize<BearerAuth>]
    public Task<Pet> Create(Pet pet) => ...;
}
```

```
POST /pets
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer
```

The scheme is a type, and using it anywhere declares it. The generator writes every scheme the
handlers name into the document's `components.securitySchemes`, with each operation's
requirement beside it.

## Establishing the caller

The framework ships the seam and not the credential reader. An `IPrincipalSource<TScheme>` reads
the request and returns the caller, and the framework's authentication middleware runs it ahead
of the whole handler chain:

```csharp
[SingletonService]
public class BearerPrincipalSource : IPrincipalSource<BearerAuth> {

    public async ValueTask<ICallerPrincipal?> Authenticate(IExecutionContext context) {
        if (!context.Request.Headers.TryGetValue("Authorization", out var header)) {
            return null;
        }

        var value = header.ToString();

        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var token = await _tokens.Validate(value["Bearer ".Length..]);

        return token is null
            ? null
            : new CallerPrincipal("bearer", token.Scopes, token.Subject, token.Issuer);
    }
}
```

The principal it returns is what `IExecutionContext.CallerPrincipal` holds for the rest of the
request, and what the authorization filter judges. An application that registers no source pays
nothing: no middleware is installed, and every request stays `AnonymousCallerPrincipal.Instance`.

::: warning No credential reader ships
There is no `BearerPrincipalSource` in the box. Terminating a token is twenty lines and one
dependency on whatever validates it, and that is yours to write. `TestGrantsPrincipalSource`
ships in `Hardened.Requests.Testing` for tests.
:::

### Returning null, and refusing

Null means "this request carries nothing of mine": no `Authorization` header, no cookie, whatever
the source reads. The next source is asked, and a request no source answers for stays anonymous.

A credential that is present and invalid is the source's own decision. Return null to let the
request continue anonymously and be refused by authorization with a fresh challenge, or throw
`AuthorizationException` to refuse it immediately with a specific one.

Sources run in registration order until one answers, so more than one is how an application
accepts a bearer token and an API key at the same door.

`IPrincipalSource<TScheme>` names the scheme its source implements. The runtime does not dispatch
on the type parameter, so implementing the plain `IPrincipalSource` works identically. The
generic form exists so that "go to references" connects the operations requiring a scheme with
the source that implements it.

## Declaring the scheme

Three kinds of scheme ship:

| Attribute | Becomes |
|---|---|
| `[HttpAuthenticationScheme(scheme)]` | An HTTP scheme: `bearer`, `basic`, `digest`. `BearerFormat` is a documentation hint |
| `[ApiKeyAuthenticationScheme(name, location)]` | A key in a header, query value or cookie |
| `[OAuth2AuthenticationScheme(flow)]` | An OAuth2 flow, with `AuthorizationUrl`, `TokenUrl` and `RefreshUrl` |

OAuth2 is the only kind that carries scopes. Grants required beside it become the requirement's
scope list in the published document. Beside an HTTP or API-key scheme they reach the document
as "authenticated via this scheme" and nothing more, which is the same rule the OpenAPI reader
applies in the other direction.

## Reading the caller in a handler

A code-first handler takes an `IExecutionContext` parameter. A contract-first handler implements
a generated interface, so its signature is fixed. Take `ICurrentCaller` instead:

```csharp
[Handler]
public class OrderService(ICurrentCaller caller, IOrderStore store) : IOrderService {

    public async Task<Order> GetOrder(string orderId) {
        var order = await store.Get(orderId);

        if (order.OwnerSubject != caller.Principal.Subject) {
            throw new Forbidden("Not your order.").AsException();
        }

        return order;
    }
}
```

It is scoped, present on every request, and registered by `HardenedRequestModule`. A request no
principal source answered for carries `AnonymousCallerPrincipal.Instance`, so a handler reads the
same shape either way and never checks for null.

This is not a substitute for a declaration. What a caller may do belongs on the operation and is
judged before the handler runs. `ICurrentCaller` is for the decisions only the handler can make,
such as whether this caller owns this row.

::: tip An ownership check changes how the response may be cached
A handler that filters by caller is `CacheScope.PerCaller`. See
[who the answer is for](/guide/response-caching#say-who-the-answer-is-for).
:::

## What a principal carries

| Member | |
|---|---|
| `AuthenticationScheme` | The scheme that authenticated the caller. `IsAuthenticated` is derived from it being non-null |
| `Subject` | Who the caller is |
| `Issuer` | Who says so |
| `Grants` | What they hold, as a set of strings |
| `TryGetClaim(name, out value)` | Anything else the credential carried |

`CallerPrincipal` is the shipped implementation and requires a scheme name, because a principal
authenticated by nothing is `AnonymousCallerPrincipal.Instance` instead.

Establishing the caller is all a source does. A source that already knows the grants puts them on
the principal it builds, which is what the testing source does. Grants that live elsewhere are
resolved per request; see
[Resolving grants from elsewhere](/guide/authorization#resolving-grants-from-elsewhere).

## Next

- [Authorization](/guide/authorization): what the caller may do
- [Credentials](/guide/testing-credentials): the caller a test sends as
- [The OpenAPI document](/guide/openapi-document#what-a-guard-on-the-operation-publishes): the 401 the document carries
