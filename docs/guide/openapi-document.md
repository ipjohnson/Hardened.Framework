# The OpenAPI document

Every Hardened web application can serve an OpenAPI document and a reference page over it.
Code-first, two attributes on the module do it:

```csharp
[HardenedModule]
[HardenedWebModule]
[Enable<OpenApiDocumentPublishing>]            // serves /openapi.json
[HardenedOpenApiUi(Title = "Contoso Orders")]  // renders /docs from it
[KestrelRuntime]
public partial class Application { }
```

```console
$ curl localhost:5080/openapi.json
{"openapi":"3.2.0","info":{...},"paths":{"/orders":{"get":{...}},"/orders/{id}":{...}}, ...}
```

The document is generated from the routing table during the build: the paths, the verbs, the
bound parameters and the response schemas. Contract-first, the document is the contract you
wrote, embedded verbatim:

| | The document is | Served by |
|---|---|---|
| **Code-first** | generated from your handlers during the build | `[Enable<OpenApiDocumentPublishing>]` |
| **Contract-first** | the contract you wrote, embedded verbatim | `PublishUrl` on the spec item |

Both end at the same two endpoints, and the reference page is the same module either way. The
build can also write the served document to a file, which is what a
[client](/guide/clients) is generated from; see [the export](#the-export).

## Serving a generated document

`[Enable<OpenApiDocumentPublishing>]` serves the document at `/openapi.json`. The marker gates the
emit, not just the route: without it no document is generated and none is carried in the
assembly.

It goes on the module that declares the routes. A host module that declares none and carries the
attribute serves an empty document, and `HRDOA003` says so.

To serve it somewhere else, declare your own marker carrying the path and enable that instead:

```csharp
[OpenApiDocumentPath("/spec.json")]
public sealed class SpecEndpoint { }

[Enable<SpecEndpoint>]
public partial class Application { }
```

## Serving a reference page

`[HardenedOpenApiUi]` renders a page against a published document:

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

The template serves the page in the `development` environment only; see
[The reference page](/guide/project-templates#the-reference-page).

### Serving the script yourself

The UI loads from a CDN under subresource integrity. An application behind a VPC, or with a
`script-src` policy that will not name a CDN, points `ScriptUrl` at a copy it serves and states
that there is no hash:

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

Contract-first, the document is a build input, so say where it publishes on the item that
declares it:

```xml
<ItemGroup>
    <HardenedOpenApiSpec Include="Specs\petstore.yaml">
        <PublishUrl>/openapi.yaml</PublishUrl>
        <UiUrl>/docs</UiUrl>
    </HardenedOpenApiSpec>
</ItemGroup>
```

Nothing is registered in code. The document is embedded verbatim and served at `PublishUrl` with
the content type its file extension implies, so a `.yaml` spec is served as `application/yaml`.
The reference page at `UiUrl` reads it.

`UiEnvironments` limits which [environments](/guide/environments) serve the page. Empty means all
of them:

```xml
<UiEnvironments>Development;Staging</UiEnvironments>
```

The same metadata works on `HardenedSmithyModel` and `HardenedSmithyAst`, where `PublishUrl`
serves the OpenAPI document generated from the model. See
[Generating from Smithy](/guide/smithy).

## The export

`<HardenedOpenApiOutput>` writes the document the application serves to a file after every
compile, whichever front end wrote it:

```xml
<PropertyGroup>
  <HardenedOpenApiOutput>openapi/Todos.json</HardenedOpenApiOutput>
</PropertyGroup>
```

It exports what the server serves, read out of the compiled assembly rather than a running
application, so it works for a Lambda function or a library with no entry point. The format
follows the extension: `.json` is indented JSON, `.yaml` or `.yml` is YAML, and anything else is
a build error naming the three. The served document does not change whatever the file says.

| Property | Values | What it does |
|---|---|---|
| `HardenedOpenApiOutput` | a path relative to the project | writes the served document there after every compile. Absent means no file |
| `HardenedOpenApiOutputVersion` | `3.0.0`, `3.1.0` | lowers the written file for a reader that refuses the 3.2 banner. The banner changes; `itemSchema` is dropped while the array under `schema` stays, so the item type survives, and each streaming operation is named (`030`) because a client generated from the lowered file reads a list rather than a stream; and at `3.0.0` the exclusive-bound and nullable spellings become the 3.0 forms. The served document is untouched |

Code-first, the property with no `[Enable<OpenApiDocumentPublishing>]` is `HRDOA018`, naming the
attribute. Contract-first the same condition reports under `HOAT` or `HSMT`, and means the
generator did not run.

## What the response model changes

The same handler and the same 404, with a different contract. In `Throws` mode the signature names
one success type, so the generator has one status to write:

```csharp
[Get("/todos/{id}")]
public Todo ById(ITodoStore store, int id) { /* throws NotFound */ }
```

```json
"responses": {
  "200": { "description": "OK", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Todo" } } } }
}
```

The 404 a client receives on every miss is not in the document, so a generated client has no
branch for it. `[Throws<NotFound>]` on the handler puts it there. In `Response` or `Union` mode
the set is in the return type, so it is in the contract:

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

One entry per status, written in status order. Two cases at the same status become a `oneOf`.

::: tip Code-first success defaults to 200
A code-first operation publishes 200 for its success unless the route attribute names another.
`[Post("/todos", SuccessStatus = 201)]` publishes 201 and answers it. A response set case can
also carry its own `[HttpStatus]`, which is how `Created<T>` publishes 201. Contract-first takes
the success status from the contract. The attribute and the contract fill the same field, so the
document and the wire cannot disagree.
:::

## What else reaches the document

| Attribute | Effect |
|---|---|
| [`[Tag(name)]`](/reference/attributes) | the tag an operation groups under. Defaults to the class name minus `Controller` |
| [`[Server(url, description?)]`](/reference/attributes) | a base URL listed under `servers`. Repeatable, and valid on the assembly |

A handler's XML documentation comment carries into the operation: `<summary>` becomes `summary`
and `<remarks>` becomes `description`.

A property annotated `[Range]`, `[StringLength]`, `[Pattern]`, `[ItemCount]`, `[MultipleOf]` or
`[AllowedValues]` publishes `minimum`/`maximum`, `minLength`/`maxLength`, `pattern`,
`minItems`/`maxItems`, `multipleOf` or `enum` alongside its type, so the constraint a client is
validated against is the one the document advertises. See [Validation](/guide/validation).

## What a guard on the operation publishes

A filter that can refuse a request answers a status the handler's return type says nothing about,
so an operation guarded by one publishes that status too:

| Declaration | Publishes |
|---|---|
| [`[AuthorizeGrants]`](/guide/authorization), and any other authorization attribute | `403` |
| [`[RateLimit]`](/guide/rate-limiting) | `429` |
| [`[Timeout]`](/guide/request-timeouts) | `504`, or the `Status` it declares, and `x-hardened-timeout` with the budget |

All three answer the framework's error envelope, so a [generated client](/guide/clients) gets a
typed case for the refusal it will be sent. Declarations on the method, its class and the
assembly are all read, nearest first. A `401` is published separately for any operation carrying
a security requirement, with the `WWW-Authenticate` challenge beside it.

An authorization attribute of your own publishes the `403` without doing anything, because the
declaration lives on `IAuthorizeAttribute`. A filter vocabulary of your own publishes its status
by carrying `[AnswersStatus(status, typeof(body))]`.

## Next

- [Generated clients](/guide/clients): generating a client from the exported document
- [Declared responses](/guide/responses): the three response models in full
- [Generating from OpenAPI](/guide/openapi): the other direction, contract to code
