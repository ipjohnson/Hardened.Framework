# The OpenAPI document

Every Hardened web application can serve an OpenAPI document and a reference page from it. Where the
document comes from follows from how the application was written:

| | The document is | Served by |
|---|---|---|
| **Code-first** | generated from your handlers during the build | `[Enable<OpenApiDocumentPublishing>]` |
| **IDL-first** | the contract you already wrote, embedded verbatim | `PublishUrl` on the spec item |

Both end at the same two endpoints, and the reference page is the same module either way. Either
way the build can also write the served document to a file, which is what a client is generated
from; see [the export](#the-export) below.

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

The marker gates the emit, not just the route: without it no document is generated and none is
carried in the assembly. A build that runs a contract lint over its own API has to enable publishing
to have anything to lint.

To serve it somewhere else, declare your own marker carrying the path and enable that instead:

```csharp
[OpenApiDocumentPath("/spec.json")]
public sealed class SpecEndpoint { }

[Enable<SpecEndpoint>]
public partial class Application { }
```

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

Two attributes: the document is worth serving alone, and the page needs one to read.

| Property | Default |
|---|---|
| `Path` | `/docs` |
| `Title` | `API Reference` |
| `DocumentPath` | `/openapi.json` |
| `Environments` | every environment |
| `ScriptUrl` | a version-pinned Scalar build on jsDelivr |
| `ScriptIntegrity` | the `sha384` hash for that exact build |

The module's identity is its `Path`, so two installs at different paths both load and two at the
same path collapse into one:

```csharp
[HardenedOpenApiUi(Path = "/docs",          DocumentPath = "/openapi.json")]
[HardenedOpenApiUi(Path = "/docs/internal", DocumentPath = "/internal.json", Title = "Internal")]
public partial class Application { }
```

### Serving the script yourself

The UI loads from a CDN under subresource integrity. An application behind a VPC, or with a
`script-src` policy that will not name a CDN, points `ScriptUrl` at a copy it serves and states that
there is no hash:

```csharp
[HardenedOpenApiUi(ScriptUrl = "/assets/api-reference.js", ScriptIntegrity = "")]
```

`ScriptIntegrity = ""` says there is none. Leaving it null leaves the default hash in place, which
would fail against your own file.

### The page is not anonymous by default

There is no `[AllowAnonymous]` on the page. It inherits default-deny where
[`[RequireAuthorization]`](/guide/authorization) is on, stays public where no authorization is
configured, and is gate-able by convention everywhere else.

## Serving from a contract

IDL-first — [OpenAPI](/guide/openapi) or [Smithy](/guide/smithy) — the document is a build input, so
say where it publishes on the item that declares it:

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
converted to JSON. The reference page at `UiUrl` reads the document that was published.

`UiEnvironments` limits which [environments](/guide/environments) serve the page. Empty means all of
them:

```xml
<UiEnvironments>Development;Staging</UiEnvironments>
```

The same metadata works on `HardenedSmithyModel` and `HardenedSmithyAst`, where `PublishUrl` serves
the OpenAPI document generated from the model. See [Generating from Smithy](/guide/smithy).

## The export

`<HardenedOpenApiOutput>` writes the document the application serves to a file after every compile,
whichever front end wrote it:

```xml
<PropertyGroup>
  <HardenedOpenApiOutput>openapi/Todos.json</HardenedOpenApiOutput>
</PropertyGroup>
```

It exports what the server serves — the normalised document generated from the model, never the
source contract; `SourceUrl` exists for that — read out of the compiled assembly rather than a
running application, so it works for a Lambda function or a library with no entry point. The
format follows the extension: `.json` is indented JSON, `.yaml` or `.yml` is YAML, and anything
else is a build error naming the three. The served document does not change whatever the file
says.

| Property | Values | What it does |
|---|---|---|
| `HardenedOpenApiOutput` | a path relative to the project | writes the served document there after every compile. Absent means no file |
| `HardenedOpenApiOutputVersion` | `3.0.0`, `3.1.0` | lowers the written file for a reader that refuses the 3.2 banner: the banner changes, `itemSchema` is dropped and each streaming operation that lost one is named (`030`), and at `3.0.0` the exclusive-bound and nullable spellings become the 3.0 forms. The served document is untouched |

Code-first, the property with no `[Enable<OpenApiDocumentPublishing>]` is `HRDOA018`, naming the
attribute. Contract-first the same condition reports under `HOAT` or `HSMT`, and means the
generator did not run. The file is what [a client](/guide/clients) is generated from.

## What the response model changes

The same handler and the same 404, with a different contract:

**Standard** — the signature names one success type and reaches its other statuses by throwing, so
the generator has one status to write:

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

One entry per status, written in status order. Two cases at the same status become a `oneOf` —
`Response<Todo, Archived>` where both are 200 means two shapes under one status.

::: tip Code-first success defaults to 200
A code-first operation publishes 200 for its success unless the route attribute names another.
`[Post("/todos", SuccessStatus = 201)]` publishes 201 and answers it. A response set case can also
carry its own `[HttpStatus]`, which is how `Created<T>` publishes 201. IDL-first takes the success
status from the contract. The attribute and the contract fill the same field, so the document and
the wire cannot disagree. See [Routing](/guide/routing#return-values-and-status-codes).
:::

## What else reaches the document

| Attribute | Effect |
|---|---|
| [`[Tag(name)]`](/reference/attributes) | the tag an operation groups under. Defaults to the class name minus `Controller` |
| [`[Server(url, description?)]`](/reference/attributes) | a base URL listed under `servers`. Repeatable, and valid on the assembly |

A handler's XML documentation comment carries into the operation: `<summary>` becomes `summary` and
`<remarks>` becomes `description`.

A property annotated `[Range]`, `[StringLength]`, `[Pattern]`, `[ItemCount]`, `[MultipleOf]` or
`[AllowedValues]` publishes `minimum`/`maximum`, `minLength`/`maxLength`, `pattern`,
`minItems`/`maxItems`, `multipleOf` or `enum` alongside its type, so the constraint a client is
validated against is the one the document advertises. The attributes live in the
`ValidationModules.Constraints` namespace:

```csharp
using ValidationModules.Constraints;

public record CreateTodo(
    [property: StringLength(Min = 1, Max = 200)] string Title,
    [property: Range(Min = 1, Max = 5)] int Priority);
```

## Next

- [Clients](/guide/clients) — generating a client from the exported document
- [Declared responses](/guide/responses) — the three response models in full
- [Generating from OpenAPI](/guide/openapi) — going the other direction, contract to code
- [Generating from Smithy](/guide/smithy) — the same, from a Smithy model
