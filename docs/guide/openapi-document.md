# The OpenAPI document

Every Hardened web application can serve an OpenAPI document and a reference page from it. Where the
document comes from is the one thing that differs, and it follows from how the application was
written:

| | The document is | Served by |
|---|---|---|
| **Code-first** | generated from your handlers during the build | `[Enable<OpenApiDocumentPublishing>]` |
| **IDL-first** | the contract you already wrote, embedded verbatim | `PublishUrl` on the spec item |

Both end at the same two endpoints, and the reference page is the same module either way.

## Serving a generated document

Code-first, the document is emitted from the routing table — the paths, the verbs, the bound
parameters and the response schemas the generator already worked out. Enable it on the module:

```csharp
[HardenedModule]
[HardenedWebModule]
[Enable<OpenApiDocumentPublishing>]
[KestrelRuntime]
public partial class Application { }
```

That serves the document at `/openapi.json`.

**The marker gates the emit, not just the route.** Without it no document is generated and none is
carried in the assembly, so an application that does not publish one does not pay for it. This
matters beyond the endpoint: the document is what a contract lint reads, so a build that runs
Spectral over its own API has to enable publishing to have anything to lint.

To serve it somewhere else, declare your own marker carrying the path and enable that instead:

```csharp
[OpenApiDocumentPath("/spec.json")]
public sealed class SpecEndpoint { }

[Enable<SpecEndpoint>]
public partial class Application { }
```

The attribute's presence is what turns the document on; its value is where the route goes.

## Serving a reference page

`[HardenedOpenApiUi]` renders a page against a published document:

```csharp
[HardenedModule]
[HardenedWebModule]
[Enable<OpenApiDocumentPublishing>]     // serves /openapi.json
[HardenedOpenApiUi(Title = "Contoso Orders")]
[KestrelRuntime]
public partial class Application { }
```

Two attributes because they are two decisions. The document is worth serving alone — it is what a
client generator consumes — and the page is worth nothing without one.

| Property | Default |
|---|---|
| `Path` | `/docs` |
| `Title` | `API Reference` |
| `DocumentPath` | `/openapi.json` |
| `ScriptUrl` | a version-pinned Scalar build on jsDelivr |
| `ScriptIntegrity` | the `sha384` hash for that exact build |

**Install it more than once for more than one page.** The module's identity is its `Path`, so two
installs at different paths both load and two at the same path collapse into one:

```csharp
[HardenedOpenApiUi(Path = "/docs",          DocumentPath = "/openapi.json")]
[HardenedOpenApiUi(Path = "/docs/internal", DocumentPath = "/internal.json", Title = "Internal")]
public partial class Application { }
```

### The script, and serving it yourself

The UI loads from a CDN under subresource integrity, so no third-party asset ships inside the
package and a swapped one fails to execute rather than running. The version is pinned because the
integrity hash is a hash of exact bytes, which a floating tag could not carry.

An application behind a VPC, or with a `script-src` policy that will not name a CDN, points
`ScriptUrl` at a copy it already serves and states that there is no hash:

```csharp
[HardenedOpenApiUi(ScriptUrl = "/assets/api-reference.js", ScriptIntegrity = "")]
```

`ScriptIntegrity = ""` says there is none. Leaving it null says nothing, which leaves the default in
place — and the default hash against your own file would fail to execute.

### It is not anonymous by default

There is deliberately no `[AllowAnonymous]` on the page. Without one it inherits default-deny where
[`[RequireAuthorization]`](/guide/authorization) is on, stays public where no authorization is
configured, and is gate-able by convention everywhere else. Three behaviours, nothing to configure.

## Serving from a contract

IDL-first — [OpenAPI or Smithy](/guide/openapi) — the document is a build input, so where it
publishes is a fact about the file rather than something to restate in C#. Say it on the item that
declares the spec:

```xml
<ItemGroup>
    <HardenedOpenApiSpec Include="Specs\petstore.yaml">
        <PublishUrl>/openapi.yaml</PublishUrl>
        <UiUrl>/docs</UiUrl>
    </HardenedOpenApiSpec>
</ItemGroup>
```

Nothing is registered in code. The document is embedded verbatim and served at `PublishUrl` with the
content type its file extension implies — a `.yaml` spec is served as `application/yaml`, not
converted to JSON. The reference page at `UiUrl` goes through the same `HardenedOpenApiUi` module an
attribute-routed application applies as an attribute, and it reads the document that was published,
because both URLs come from the same item and cannot disagree.

`UiEnvironments` limits which [environments](/guide/environments) serve the page. Empty means all of
them:

```xml
<UiEnvironments>Development;Staging</UiEnvironments>
```

The same metadata works on `HardenedSmithyAst`. Smithy adds `$(HardenedSmithyServiceShapeId)` and
the AST step; publishing is identical.

## What the response model changes

This is the reason the [response model](/guide/responses) is worth choosing deliberately. The same
handler, the same 404, and a different contract:

**Standard** — the signature names one success type and reaches its other statuses by throwing. The
generator has one status to write:

```csharp
[Get("/todos/{id}")]
public Todo ById(ITodoStore store, int id) { /* throws NotFound */ }
```

```json
"responses": {
  "200": { "description": "OK", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Todo" } } } }
}
```

The 404 a client receives on every miss is not in the document, so a generated client has no branch
for it.

**Response or Union** — the set is in the return type, so it is in the contract:

```csharp
[Get("/todos/{id}")]
public Response<Todo, NotFound> ById(ITodoStore store, int id) { /* returns NotFound */ }
```

```json
"responses": {
  "200": { "description": "OK",        "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Todo" } } } },
  "404": { "description": "Not Found", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Problem" } } } }
}
```

One entry per status, written in status order — a document is diffed against the last one as often
as it is read, so an operation whose responses moved for no reason is a diff someone has to work out
is empty.

Two cases at the same status become a `oneOf` rather than the last one silently winning. That is a
real declaration: `Response<Todo, Archived>` where both are 200 means two shapes under one status.

::: tip Code-first success is 200
The `SuccessStatus` property on the route attributes is not read — see
[Routing](/guide/routing#return-values-and-status-codes). A code-first operation publishes 200 for
its success unless a response set case carries its own `[HttpStatus]`, which is how `Created<T>`
publishes 201. IDL-first takes the success status from the contract, so a `201` in the description
is a 201 in the document.
:::

## What else reaches the document

| Attribute | Effect |
|---|---|
| [`[Tag(name)]`](/reference/attributes) | the tag an operation groups under. Defaults to the class name minus `Controller` |
| [`[Server(url, description?)]`](/reference/attributes) | a base URL listed under `servers`. Repeatable, and valid on the assembly |

A handler's XML documentation comment carries into the operation: `<summary>` becomes `summary` and
`<remarks>` becomes `description`, which is the split the two constructs already have — one line
saying what it does, the prose saying the rest.

Schemas carry the constraints their validation attributes state, so a property
annotated `[Min]`, `[Max]`, `[Pattern]`, `[ItemCount]`, `[MultipleOf]` or `[AllowedValues]` publishes
`minimum`, `maximum`, `pattern`, `minItems`/`maxItems`, `multipleOf` or `enum` alongside its type.
The constraint a client is validated against is the one the document advertises, because they are
written from the same annotation.

## Next

- [Declared responses](/guide/responses) — the three response models in full
- [Generating from OpenAPI](/guide/openapi) — going the other direction, contract to code
