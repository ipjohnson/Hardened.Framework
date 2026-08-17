# Authorization

A handler says what it needs; the pipeline decides whether the caller has it.

There are several ways to say what a handler needs, because the situations differ — a route
generated from a specification is not written the way a hand-authored one is, and a rule covering
a hundred routes is not written on each of them. They all end in the same place, which is the part
worth knowing first.

## One requirement per handler

Every way of declaring authorization produces a `Requirement`. Every requirement on a handler is
**conjoined** into a single one, exposed as `IExecutionRequestHandlerInfo.Requirement`, and the
authorization filter reads only that.

```
[AuthorizeGrants("...")]    ─┐
[AuthorizeGrants<T>]         │
a derived attribute          ├──▶  one Requirement on the handler  ──▶  the authorization filter
[Authorize<TPolicy>]         │
IAuthorizationConvention    ─┘
```

Two consequences follow, and both are deliberate.

**Everything is required.** Attributes stack as *and*, whether they were written on the method,
written on the controller, inherited, or added by a convention. Adding a declaration can only
narrow what is admitted — never widen it. There is no spelling of "either of these will do" that
comes from writing two attributes.

**Alternatives live inside one declaration.** "Read *or* admin" is a real requirement; it is
written as one expression in one policy, not as two attributes. See [Policies](#policies).

## Requiring grants

A **grant** is a string — that is what an OAuth scope is on the wire. The simplest form names them:

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
spec and cannot be typos. Written by hand it has the problem every string has: nothing catches
`pets:raed` until a request is refused in an environment somebody happens to be testing in.

The next two sections are the two ways to stop writing strings.

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

Small amount of syntax, and it buys most of what a string costs you:

- The grants are a compile-time reference. Rename or drop a constant and every handler requiring it
  fails to build, rather than starting to refuse requests.
- The set is defined in one place. Widening `PetsReadWrite` is one edit, and every handler naming it
  follows — no grepping for a string across controllers.
- "Go to references" answers *what needs this?*, which a string cannot.

Nothing is generated and nothing is registered. The provider is constructed once, in the generated
handler's static initializer, when the attribute is built.

::: tip One provider, many handlers
This is a type per **set of grants**, not per handler. The win compounds with reuse — a set named by
twenty routes is twenty call sites that change together.
:::

::: warning The set is fixed at startup
A provider is read once when the attribute is constructed, so it must be a constant of the
application. Do not read configuration or a database in `Grants` — nothing re-reads it. Grants that
vary per caller or per tenant belong behind
[`IActivityAuthorizationHandler`](#resolving-grants-from-elsewhere), which is asked per request.
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

Same guarantees as a typed set — compile-time, one definition, findable. The difference is what you
get a type *for*:

| | A type per… | Reads as |
|---|---|---|
| `[AuthorizeGrants<PetsReadWrite>]` | set of grants | the set it requires |
| `[RequiresPetWrite]` | call-site spelling | a sentence about the route |

Prefer the typed set when several handlers need the same grants; prefer a named attribute when the
requirement wants a name of its own that reads well where it is written. Neither is generated, and
the pipeline cannot tell them apart — both are just an `IAuthorizeAttribute`.

::: tip Your own attributes are recognised
Anything implementing `IAuthorizeAttribute` is honoured, whether it derives from the framework's
attributes or not. The build diagnostic ([HAUTH001](#requiring-authorization-everywhere)) tests the
same interface, so an attribute of your own never has to be excused with `<NoWarn>`.
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
[Authorize<CanManagePets>]
public Task<Pet> Get(string petId) => ...;
```

`&` binds tighter than `|`, so the parentheses are documentation rather than necessity.

The reason `|` lives here and not between attributes: a policy is one author writing one expression
that is read as a whole. A rule where repeating an attribute means *or* makes the meaning depend on
how many attributes happen to be present — so a method-level attribute would weaken the guard its
controller wrote, which is the one thing an authorization system must not do quietly.

`Requirement` also has `Predicate` for a test over the caller and the request — "may this caller
edit *this* pet". Using one moves the whole requirement later in the pipeline, since it may read
bound parameters.

## Conventions

A convention answers "what does this *class* of handlers need", decided from what the handler is:

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
what the handler declared, so a convention can only narrow — it cannot weaken a handler that
declared its own requirement, and cannot be weakened by one.

The handler is passed in fully formed — path, method, handler type, parameters, metadata — so a
convention can key off any of them.

::: tip Why this beats copying an attribute onto every route
A rule written on each handler drifts the first time somebody adds one. A convention covers routes
that do not exist yet.
:::

Conventions are applied while each handler is constructed, before anything reads it, so a
convention's requirement is indistinguishable from an attribute's by the time the filter runs.

## Making a route public

`[AllowAnonymous]` is the opt-out, and it is the one thing that beats everything above — including a
convention:

```csharp
[Get("/health")]
[AllowAnonymous]
public string Health() => "healthy";
```

That precedence is deliberate. The alternative is a route that reads as public in the source and
refuses in production.

## Requiring authorization everywhere

By default a handler carrying nothing is public. `[RequireAuthorization]` on the module inverts
that — a handler declaring neither a requirement nor `[AllowAnonymous]` is denied:

```csharp
[HardenedModule]
[RequireAuthorization]
public partial class Application { }
```

It does two things. At run time, unannotated handlers require an authenticated caller. At build
time, the generator reports **HAUTH001** for every handler it can see that said nothing — so a
forgotten attribute is a warning while you are writing the handler rather than a 403 found later.

Both halves are needed: the diagnostic only sees the assembly being compiled, so handlers arriving
from a referenced assembly are guarded by the runtime backstop without ever being reported.

::: warning `<NoWarn>` is the only lever on HAUTH001
Neither `#pragma warning disable` nor an `.editorconfig` severity affects a diagnostic reported by a
source generator. Measured — both are inert.
:::

## Resolving grants from elsewhere

Everything above asks what the *operation* requires. `IActivityAuthorizationService` answers the
other half — whether the *caller* holds those grants — and the default reads them off the
credential synchronously.

Grants that live somewhere else — a permissions table, a per-tenant role expansion — come from an
`IActivityAuthorizationHandler`. It is asked once per request with the whole grant list, so a
handler backed by a store makes a single round trip, and it returns the subset held rather than a
verdict.

That distinction matters: a single yes/no over several grants could only mean "all of them", which
would turn a requirement of `a | b` into `a & b` and refuse a caller holding one.

## What a refusal looks like

| Situation | Status | `WWW-Authenticate` |
|---|---|---|
| No credential presented | 401 | `Bearer` — no `error`, since there is no token to have been wrong about |
| Credential invalid | 401 | `error="invalid_token"` |
| Credential valid but too weak | 401 | `error="insufficient_user_authentication"` |
| Authenticated, lacks the grants | 403 | `error="insufficient_scope"`, `scope="…"` naming what would have satisfied it |

The 403 names the grants it wanted, which turns "no" into "no, and here is what you would need".
