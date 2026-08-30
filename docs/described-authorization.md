# Described authorization

What a description says an operation requires of its caller, and what Hardened does with it.

## The short version

A description carries **authorization**. It does not carry **authentication**.

Hardened reads **scopes and ignores schemes**. The scheme decides one thing: whether the entry
carries scopes at all. Which issuer, token format or JWKS endpoint a caller proves themselves
against stays application configuration.

## What is read

| declaration | becomes |
|---|---|
| `oauth2: ["pets:read"]` | `Requirement.Grant("pets:read")` |
| `oauth2: ["pets:read", "pets:write"]` | `Grant("pets:read") & Grant("pets:write")` |
| `apiKey: []` — any scheme with no scopes | `Requirement.Authenticated()` |
| `[{ oauth2: [...] }, { apiKey: [] }]` | `AnyOf(...)` — the array is an OR |
| `{ oauth2: [...], apiKey: [] }` | `AllOf(...)` — keys in one entry are an AND |
| `security: []` on an operation | **nothing** |
| no `security` at all | the document-level default, or nothing |

Only `oauth2` and `openIdConnect` may carry scopes. The specification requires every other type to
declare an empty array, so those say "be someone" and nothing more.

A scope name is read as a grant name, unchanged. No prefixing and no namespacing.

### An empty array means two different things

- **`{ oauth2: [] }`** — an empty *scope* array. The caller must be authenticated and needs no
  particular permission.
- **`security: []`** — an empty *security* array. The specification's way of opting one operation
  out of a document-level default. It derives **nothing**.

### An unscoped entry is a requirement, not the absence of one

```yaml
security:
  - oauth2: ["pets:write"]
  - apiKey: []
```

The second entry becomes `Authenticated()`, so the alternative stays an alternative. Reading it as
"requires nothing" would satisfy the OR for everybody, making a document that reads as protective
weaker than declaring none at all.

## It narrows, and can never open

A described requirement arrives as one more entry in the handler's metadata, alongside anything the
implementation declared. `IExecutionRequestHandlerInfo.RequirementFrom` conjoins every
`IAuthorizeAttribute` it finds there, so:

- A contract can **narrow** a route.
- A contract can **never widen** one. `security: []` does not strip an `[AuthorizeGrants]` somebody
  wrote on the implementation.
- `[AllowAnonymous]` remains the single thing that cancels it — the same rule an attribute or a
  convention is held to.

`security: []` therefore derives nothing rather than `[AllowAnonymous]`. An author who wants a route
anonymous says so in code, where whoever reads the handler can see it.

## Enforcement needs no opt-in

`AuthorizationFilterProvider`'s `requireAuthorization` flag decides what happens to a handler that
declares **nothing**. A handler that declares something is guarded either way, so a contract that
names a scope protects its route on the next build.

> **This makes adding `security` to an existing contract a breaking change for its callers.** An
> operation that answered 200 to an anonymous request answers 401 once the description says it needs
> a scope. That is the correct behaviour and it is not a quiet one — it is worth a release note when
> it happens to a published contract.

## Smithy carries less, and that is the language

Smithy has no equivalent of an OAuth scope. A model can say a caller must be authenticated; it
cannot say what they must hold.

| declaration | becomes |
|---|---|
| service declares `@httpBearerAuth` (or any scheme) | `Requirement.Authenticated()` |
| operation carries `@optionalAuth` | nothing |
| `@auth([])` on the operation or the service | nothing |
| service declares no scheme | nothing |

To require particular grants on a Smithy-generated route, put `[AuthorizeGrants]` on the
implementation. A contract can narrow a route and never widen one, so that composes.

## Diagnostics

A `security` entry naming a scheme that `components.securitySchemes` does not declare is reported at
build time. The operation falls back to requiring an authenticated caller and **none of the
permissions it names**.

---

# Design note: publishing security into a generated document

Not user documentation. Code-first does not emit `security` into the document it generates, and this
records why and what the shape would be.

## It must be opt-in

Publishing OAuth scopes is normal — a client has to request them at authorization time, and
RFC 6750 goes as far as telling a 403 to name the scope it wanted. That argument does **not**
transfer to code-first.

**Hardened grants are not OAuth scopes.** They are an arbitrary internal vocabulary. A contract-first
application that wrote `oauth2: ["pets:read"]` has already declared those strings as OAuth scopes and
published them by authorship. A code-first application that wrote
`[AuthorizeGrants("billing:write", "feature:experimental-pricing")]` has declared nothing of the
sort — and those names can disclose an unreleased feature, a capability that exists, or the shape of
an internal permission tree.

The codebase already treats document content as a deliberate exposure: the template gates its
reference page to development because "the page describes every operation this service exposes …
neither of which a deployed API obviously wants".

## The opt-in is the scheme declaration, not a flag

A document cannot carry `security` without `components.securitySchemes`, and nothing in code-first
source says what the scheme is:

```yaml
security:
  - ???: ["billing:write"]     # scheme name? type? token URL? flows?
```

`[AuthorizeGrants]` does not say whether the application is behind OAuth, an API key, or a gateway.
The author has to describe the scheme regardless — so **that declaration is the opt-in**:

- **Declare a scheme** → you have said what to emit against, and said it deliberately.
- **Declare nothing** → nothing is emitted, because nothing valid can be.

Opt-in by construction rather than by flag. There is no default to get wrong and no way to switch it
on by accident.

## Once opted in, publish every grant

**Decided.** Declaring the scheme publishes the grants of every operation.

The alternative — opting each grant in individually — is the kind of thing people forget, and what it
leaves behind is a document that is confidently wrong about which endpoints are protected. A document
that is complete or absent is more useful than one that is quietly partial.

## The grant-to-scope mapping lives in the same place

Reading a scope name as a grant name assumes one vocabulary. That holds when you own both and fails
when scopes are `https://api.example.com/pets.read` and grants are `pets:read`.

The scheme declaration is the natural home for that mapping too — an author writing "my scheme is
`oauth2`, here is its token URL, here are the scopes it issues" is already writing the thing that
says how a grant is spelled on the wire. Both open items are one feature rather than two.
