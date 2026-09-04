# Authorization

A handler says what it needs; the pipeline decides whether the caller has it.

Every way of declaring authorization produces a `Requirement`. Every requirement on a handler is
**conjoined** into a single one, exposed as `IExecutionRequestHandlerInfo.Requirement`, and the
authorization filter reads only that.

```
[AuthorizeGrants("...")]    ─┐
[AuthorizeGrants<T>]         │
a derived attribute          ├──▶  one Requirement on the handler  ──▶  the authorization filter
[Authorize<TAuth, TPolicy>]  │
IAuthorizationConvention    ─┘
```

Two rules follow. **Everything is required**: attributes stack as *and*, whether written on the
method, written on the controller, inherited, or added by a convention, so adding a declaration can
only narrow what is admitted. **Alternatives live inside one declaration**: "read *or* admin" is one
expression in one [policy](#policies), not two attributes.

## Requiring grants

A **grant** is a string. The simplest form names them:

```csharp
[Get("/pets")]
[AuthorizeGrants("pets:read")]
public Task<Pet[]> List() => ...;
```

```csharp
[Get("/pets/{petId}")]
[AuthorizeGrants("pets:read", "pets:write")]   // both required
public Task<Pet> Update(string petId, Pet pet) => ...;
```

This is the form a generator emits from an OpenAPI specification, where the strings came out of the
spec. Written by hand, nothing catches `pets:raed` until a request is refused. The next two sections
are the two ways to stop writing strings.

## Typed grant sets

`IGrantProvider` names a set of grants once. `[AuthorizeGrants<T>]` requires that set.

```csharp
public sealed class PetsReadWrite : IGrantProvider {
    public string[] Grants => [Grants.Pets.Read, Grants.Pets.Write];
}
```

```csharp
[Post("/pets")]
[AuthorizeGrants<PetsReadWrite>]
public Task<Pet> Create(Pet pet) => ...;
```

The grants become a compile-time reference: renaming or dropping a constant breaks the build rather
than starting to refuse requests, widening the set is one edit, and "go to references" answers what
needs it. Nothing is generated and nothing is registered — the provider is constructed once, in the
generated handler's static initializer.

::: tip One provider, many handlers
This is a type per **set of grants**, not per handler.
:::

::: warning The set is fixed at startup
A provider is read once when the attribute is constructed, so it must be a constant of the
application. Do not read configuration or a database in `Grants`. Grants that vary per caller or per
tenant belong behind [`IActivityAuthorizationHandler`](#resolving-grants-from-elsewhere), which is
asked per request.
:::

Sets conjoin like anything else, so naming two requires both:

```csharp
[AuthorizeGrants<PetsReadWrite>]
[AuthorizeGrants<TenantMember>]   // ...and this as well
public Task<Pet> Create(Pet pet) => ...;
```

## Named attributes

The other way to avoid strings is to derive an attribute, which reads as a name at the call site:

```csharp
public sealed class RequiresPetWriteAttribute : AuthorizeGrantsAttribute {
    public RequiresPetWriteAttribute() : base(Grants.Pets.Read, Grants.Pets.Write) { }
}
```

```csharp
[Post("/pets")]
[RequiresPetWrite]
public Task<Pet> Create(Pet pet) => ...;
```

Same guarantees as a typed set. The difference is what you get a type *for*:

| | A type per… | Reads as |
|---|---|---|
| `[AuthorizeGrants<PetsReadWrite>]` | set of grants | the set it requires |
| `[RequiresPetWrite]` | call-site spelling | a sentence about the route |

Prefer the typed set when several handlers need the same grants; prefer a named attribute when the
requirement wants a name of its own. The pipeline cannot tell them apart — both are an
`IAuthorizeAttribute`.

::: tip Your own attributes are recognised
Anything implementing `IAuthorizeAttribute` is honoured. The build diagnostic
([HAUTH001](#requiring-authorization-everywhere)) tests the same interface, so an attribute of your
own never has to be excused with `<NoWarn>`.
:::

## Policies

A policy is a named requirement with structure in it, and it is the only place alternatives are
expressible:

```csharp
public class CanManagePets : AuthorizationPolicy {
    protected override Requirement Define() =>
        (Grant(Grants.Pets.Read) & Grant(Grants.Pets.Write)) | Grant(Grants.Admin.All);
}
```

```csharp
[Get("/pets/{petId}")]
[Authorize<BearerAuth, CanManagePets>]
public Task<Pet> Get(string petId) => ...;
```

`&` binds tighter than `|`, so the parentheses are documentation rather than necessity.

**The first type argument is the authentication scheme, not the policy.** `[Authorize<T>]` with one
argument requires an authenticated caller established through `T`, and the policy rides second. A
policy written in the first position fails the constraint at the call site, which names
`IAuthenticationScheme`.

The scheme is one line, and using it anywhere is what declares it:

```csharp
[HttpAuthenticationScheme("bearer")]
public sealed class BearerAuth : IAuthenticationScheme;
```

```csharp
[Post("/pets")]
[Authorize<BearerAuth>]
public Task<Pet> Create(Pet pet) => ...;
```

The generator collects every scheme the handlers name into the document's
`components.securitySchemes`, and each operation carries its requirement. See
[Authentication](/guide/authentication) for the scheme kinds and for what establishes the caller.

`[Authorize<TAuth, TPolicy>]` conjoins `Requirement.Authenticated()` with the policy's own, so a
policy that only reads the request still demands a caller. The policy is constructed once per closed
type and its requirement built once, so writing it on a hundred handlers costs one instance and one
tree.

`Requirement` also has `Predicate` for a test over the caller and the request — "may this caller
edit *this* pet". Using one moves the whole requirement later in the pipeline, since it may read
bound parameters.

## Conventions

A convention answers what a *class* of handlers needs, decided from what the handler is:

```csharp
public class AdminRoutesAreAdminOnly : IAuthorizationConvention {
    public Requirement? Apply(IExecutionRequestHandlerInfo handler) =>
        handler.Path.StartsWith("/admin") ? Requirement.Grant(Grants.Admin.Access) : null;
}
```

```csharp
services.AddSingleton<IAuthorizationConvention, AdminRoutesAreAdminOnly>();
```

Returning `null` is the normal answer for most handlers. What a convention returns is conjoined with
what the handler declared, so it can only narrow. The handler is passed in fully formed — path,
method, handler type, parameters, metadata — so a convention can key off any of them, including
routes that do not exist yet.

Conventions are applied while each handler is constructed, before anything reads it.

## Making a route public

`[AllowAnonymous]` is the opt-out, and it beats everything above, including a convention:

```csharp
[Get("/health")]
[AllowAnonymous]
public string Health() => "healthy";
```

## Requiring authorization everywhere

By default a handler carrying nothing is public. `[RequireAuthorization]` on the module inverts
that — a handler declaring neither a requirement nor `[AllowAnonymous]` is denied:

```csharp
[HardenedModule]
[RequireAuthorization]
public partial class Application { }
```

At run time, unannotated handlers require an authenticated caller. At build time, the generator
reports **HAUTH001** for every handler in the compilation that said nothing. Handlers arriving from
a referenced assembly are covered by the runtime half only, since the diagnostic sees one assembly.

::: warning `<NoWarn>` is the only lever on HAUTH001
Neither `#pragma warning disable` nor an `.editorconfig` severity affects a diagnostic reported by a
source generator.
:::

## Where the caller comes from

Everything above assumes a caller. Establishing one is `IPrincipalSource`, and an application that
registers none stays anonymous on every request. See [Authentication](/guide/authentication).

## Resolving grants from elsewhere

Everything above asks what the *operation* requires. `IActivityAuthorizationService` answers whether
the *caller* holds those grants, and the default reads them off the credential synchronously.

Grants that live somewhere else — a permissions table, a per-tenant role expansion — come from an
`IActivityAuthorizationHandler`. It is asked once per request with the whole grant list, so a
handler backed by a store makes a single round trip, and it returns the subset held rather than a
verdict.

## What a refusal looks like

| Situation | Status | `WWW-Authenticate` |
|---|---|---|
| No credential presented | 401 | `Bearer` — no `error`, since there is no token to have been wrong about |
| Credential invalid | 401 | `error="invalid_token"` |
| Credential valid but too weak | 401 | `error="insufficient_user_authentication"` |
| Authenticated, lacks the grants | 403 | `error="insufficient_scope"`, `scope="…"` naming what would have satisfied it |
