# Authorization

A handler says what it needs, and the pipeline decides whether the caller has it. A grant is a
string, and `[AuthorizeGrants]` names the grants an operation requires:

```csharp
[Get("/pets")]
[AuthorizeGrants("pets:read")]
public Task<Pet[]> List() => ...;
```

```
GET /pets
Authorization: Bearer <a token holding orders:read>
HTTP/1.1 403 Forbidden
WWW-Authenticate: Bearer error="insufficient_scope", scope="pets:read"
```

Every declaration on a handler is conjoined into one `Requirement`, exposed as
`IExecutionRequestHandlerInfo.Requirement`, and the authorization filter reads only that. Two
rules follow. Everything is required: attributes stack as *and*, whether written on the method,
on the controller, inherited, or added by a convention, so adding a declaration can only narrow
what is admitted. Alternatives live inside one declaration: "read *or* admin" is one expression
in one [policy](#policies), not two attributes.

## Requiring grants

```csharp
[Get("/pets/{petId}")]
[AuthorizeGrants("pets:read", "pets:write")]   // both required
public Task<Pet> Update(string petId, Pet pet) => ...;
```

This is the form a generator emits from an OpenAPI specification, where the strings came out of
the spec. Written by hand, nothing catches `pets:raed` until a request is refused. The next two
sections are the two ways to stop writing strings.

### Typed grant sets

`IGrantProvider` names a set of grants once. `[AuthorizeGrants<T>]` requires that set:

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

The grants become a compile-time reference. Renaming or dropping a constant breaks the build
rather than starting to refuse requests, widening the set is one edit, and "go to references"
answers what needs it. The provider is constructed once, in the generated handler's static
initializer. This is a type per set of grants, not per handler.

::: warning The set is fixed at startup
A provider is read once when the attribute is constructed, so it must be a constant of the
application. Do not read configuration or a database in `Grants`. Grants that vary per caller or
per tenant belong behind [`IActivityAuthorizationHandler`](#resolving-grants-from-elsewhere),
which is asked per request.
:::

Sets conjoin like anything else, so naming two requires both:

```csharp
[AuthorizeGrants<PetsReadWrite>]
[AuthorizeGrants<TenantMember>]   // and this as well
public Task<Pet> Create(Pet pet) => ...;
```

### Named attributes

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

Same guarantees as a typed set. Prefer the typed set when several handlers need the same grants,
and a named attribute when the requirement wants a name of its own. The pipeline cannot tell them
apart: both are an `IAuthorizeAttribute`, and so is any attribute of your own that implements it.
The build diagnostic [HAUTH001](#requiring-authorization-everywhere) tests the same interface, so
an attribute of your own never has to be excused with `<NoWarn>`.

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

The first type argument is the [authentication scheme](/guide/authentication), not the policy.
`[Authorize<T>]` with one argument requires an authenticated caller established through `T`, and
the policy rides second. A policy written in the first position fails the constraint at the call
site, which names `IAuthenticationScheme`.

`[Authorize<TAuth, TPolicy>]` conjoins `Requirement.Authenticated()` with the policy's own, so a
policy that only reads the request still demands a caller. The policy is constructed once per
closed type and its requirement built once, so writing it on a hundred handlers costs one
instance and one tree.

`Requirement` also has `Predicate` for a test over the caller and the request, such as "may this
caller edit this pet". Using one moves the whole requirement later in the pipeline, since it may
read bound parameters.

## Conventions

A convention answers what a class of handlers needs, decided from what the handler is:

```csharp
public class AdminRoutesAreAdminOnly : IAuthorizationConvention {
    public Requirement? Apply(IExecutionRequestHandlerInfo handler) =>
        handler.Path.StartsWith("/admin") ? Requirement.Grant(Grants.Admin.Access) : null;
}
```

```csharp
services.AddSingleton<IAuthorizationConvention, AdminRoutesAreAdminOnly>();
```

Returning `null` is the normal answer for most handlers. What a convention returns is conjoined
with what the handler declared, so it can only narrow. The handler is passed in fully formed, with
its path, method, handler type, parameters and metadata, so a convention can key off any of them,
including routes that do not exist yet. Conventions are applied while each handler is
constructed, before anything reads it.

## Making a route public

`[AllowAnonymous]` is the opt-out, and it beats everything above, including a convention:

```csharp
[Get("/health")]
[AllowAnonymous]
public string Health() => "healthy";
```

## Requiring authorization everywhere

By default a handler carrying nothing is public. `[RequireAuthorization]` on the module inverts
that. A handler declaring neither a requirement nor `[AllowAnonymous]` is denied:

```csharp
[HardenedModule]
[RequireAuthorization]
public partial class Application { }
```

At run time, unannotated handlers require an authenticated caller. At build time, the generator
reports `HAUTH001` for every handler in the compilation that said nothing. Handlers arriving from
a referenced assembly are covered by the runtime half only, since the diagnostic sees one
assembly.

::: warning `<NoWarn>` is the only lever on HAUTH001
Neither `#pragma warning disable` nor an `.editorconfig` severity affects a diagnostic reported by
a source generator.
:::

## Where the caller comes from

Everything above assumes a caller. Establishing one is `IPrincipalSource`, and an application
that registers none stays anonymous on every request. See
[Authentication](/guide/authentication).

## Resolving grants from elsewhere

Everything above asks what the operation requires. `IActivityAuthorizationService` answers
whether the caller holds those grants, and the default reads them off the credential.

Grants that live somewhere else, a permissions table or a per-tenant role expansion, come from an
`IActivityAuthorizationHandler`. It is asked once per request with the whole grant list, so a
handler backed by a store makes a single round trip, and it returns the subset held rather than a
verdict.

## What a refusal looks like

| Situation | Status | `WWW-Authenticate` |
|---|---|---|
| No credential presented | 401 | `Bearer`, with no `error`, since there is no token to have been wrong about |
| Credential invalid | 401 | `error="invalid_token"` |
| Credential valid but too weak | 401 | `error="insufficient_user_authentication"` |
| Authenticated, lacks the grants | 403 | `error="insufficient_scope"`, `scope="…"` naming what would have satisfied it |

## Next

- [Authentication](/guide/authentication): the scheme and the principal source
- [Credentials](/guide/testing-credentials): sending a test as a caller holding grants
- [Response caching](/guide/response-caching#say-who-the-answer-is-for): what a guarded handler may cache
