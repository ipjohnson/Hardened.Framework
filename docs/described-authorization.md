# Described authorization

What a description says an operation requires of its caller, and what Hardened does with it.

## The short version

A description carries **authorization**. It does not carry **authentication**.

Which scheme a caller proves themselves with — which issuer, which token format, which JWKS
endpoint — is configuration this application already owns, and a description cannot know it. Which
*permissions* an operation needs is a fact about the operation, belongs beside it, and maps onto
`Requirement` without an intermediary.

So Hardened reads **scopes and ignores schemes**. The scheme decides only one thing: whether the
entry carries scopes at all.

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

A scope name is read as a grant name, unchanged. No prefixing, no namespacing: prefixing would
impose a naming convention on every application to solve a collision almost none of them have.

### An empty array means two different things

They are opposites and conflating them would be a bad defect.

- **`{ oauth2: [] }`** — an empty *scope* array. The caller must be authenticated and needs no
  particular permission.
- **`security: []`** — an empty *security* array. The specification's way of opting one operation
  out of a document-level default. It derives **nothing**.

### Why an unscoped entry is a requirement, not the absence of one

This is the load-bearing rule.

```yaml
security:
  - oauth2: ["pets:write"]
  - apiKey: []
```

Read the second entry as "requires nothing" and the OR is satisfied by everybody — a document that
reads as protective would generate a requirement **weaker than declaring none at all**. It becomes
`Authenticated()`, which is a real requirement, so the alternative stays an alternative.

## It narrows, and can never open

A described requirement arrives as one more entry in the handler's metadata, alongside anything the
implementation declared. `IExecutionRequestHandlerInfo.RequirementFrom` conjoins every
`IAuthorizeAttribute` it finds there, so:

- A contract can **narrow** a route.
- A contract can **never widen** one. `security: []` does not strip an `[AuthorizeGrants]` somebody
  wrote on the implementation.
- `[AllowAnonymous]` remains the single thing that cancels it — the same rule an attribute or a
  convention is held to.

This is why `security: []` derives nothing rather than deriving `[AllowAnonymous]`. An author who
wants a route anonymous says so in code, where it is visible to whoever reads the handler.

It is also why the requirement is metadata rather than the `requirement` parameter on
`ExecutionRequestHandlerInfo`: that reads `requirement ?? RequirementFrom(Metadata)`, so passing it
there would have **silenced** an attribute on the implementation instead of composing with it.

## Enforcement needs no opt-in

`AuthorizationFilterProvider`'s `requireAuthorization` flag decides what happens to a handler that
declares **nothing**. A handler that declares something is guarded either way.

So a contract that names a scope protects its route on the next build, rather than on the next time
somebody remembers to turn a posture on.

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

The auth traits were previously classified `Ignorable`, on the grounds that "authentication is
Hardened's own story rather than the IDL's". That is right about the *scheme* and wrong about
whether an operation needs one at all, which is a fact about the operation.

Making a stock Smithy model carry permissions would need a custom trait — `@grants(["pets:read"])`.
Smithy's trait system is built for that and it would be small, but it puts a Hardened-specific
extension in a model whose portability is usually the reason Smithy was chosen. Not done; nothing
about the design forecloses it.

## Diagnostics

A `security` entry naming a scheme that `components.securitySchemes` does not declare is reported.
The reference is dangling — the document's own error — and reading its scope list would invent
authorization out of a name that resolves to nothing. The operation falls back to requiring an
authenticated caller and **none of the permissions it names**, which is a downgrade nobody asked
for, so it is a build message rather than a discovery in production.

---

# Not built: publishing security into a generated document

Code-first does not emit `security` into the document it generates. This is the other half of the
gap and it is **deliberately unbuilt**, with the design settled.

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
