# Diagnostics

Every code the Hardened generators and build tasks raise.

**Warnings become errors under `ContinuousIntegrationBuild`**, so anything left unaddressed locally
fails CI. Where a warning describes something you meant, silence it by id:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);HOAG030</NoWarn>
</PropertyGroup>
```

The framework's own [`docs/generator-diagnostics.md`](https://github.com/ipjohnson/Hardened.Framework/blob/main/docs/generator-diagnostics.md)
carries the long-form entry for each, with the message text and the fix.

## Prefixes

| Prefix | Raised by |
|---|---|
| `HOAG` | Handler binding: matching `[Handler]` classes to described services |
| `HAUTH` | Authorization |
| `HRDR` | Routing |
| `HRDV` | Validation |
| `HRDW` | Web handlers |
| `HRDRM` | Response models |
| `HRDT` | `[Throws<T>]` |
| `HRDSC` | Authentication schemes |
| `HRDOA` | The code-first OpenAPI document |
| `HOAT` | The OpenAPI description build task |
| `HSMT` | The Smithy description build task |
| `HTPL` | The `hardened-web` template's own project checks |
| `HRDAWS` | Hardened.Amz |

`HOAT` and `HSMT` share one numbering: a number means the same thing under each, and a finding is
reported under the prefix of the front end that read the description. A number that exists for one
front end only leaves a gap in the other.

## Handler binding

| Id | | Meaning |
|---|---|---|
| `HOAG001` | error | The routing table generator failed |
| `HOAG002` | error | The description could not be parsed; the build task's message is passed through |
| `HOAG010` | warning | A handler was skipped because a parameter type did not resolve. Other handlers are unaffected |
| `HOAG020` | error | An operation declares a markup content type and names no view to render it |
| `HOAG030` | warning | A described service has no `[Handler]`. Its routes exist and fail at request time. `NoWarn` it in a project that ships contracts without implementations |
| `HOAG031` | warning | A `[Handler]` class names no described service in its base list. Usually a spelling mismatch. A base class beside the interface is fine |

## Routing

| Id | | Meaning |
|---|---|---|
| `HRDR001` | error | Two routes are ambiguous. Give them different paths |
| `HRDR002` | error | Unsupported route token syntax |
| `HRDR003` | error | A `[RouteConstraint]` method has the wrong signature. It must be `static bool(ReadOnlySpan<char>)` |
| `HRDR004` | error | More than one Hardened entry point in one assembly |
| `HRDR005` | warning | A route token binds no parameter |
| `HRDR006` | warning | No routing generator is compiling this assembly's routes |
| `HRDR007` | error | A service parameter binds from the request body |
| `HRDR008` | error | More than one routing generator is compiling this assembly |

## Validation

| Id | | Meaning |
|---|---|---|
| `HRDV001` | retired | Retired. It warned that a constraint on a handler parameter was not compiled; [they are compiled now](/guide/validation#declaring-constraints-in-code) |
| `HRDV002` | warning | Two validators claimed the same generated file |
| `HRDV003` | warning | A required member of a value type cannot be found missing, so `[Required]` there does nothing |
| `HRDV004` | warning | Nested constraints are never reached |
| `HRDV005` | error | A `When` or `Unless` on a parameter's constraint names a member of a model the parameter does not sit on |

## Web handlers

| Id | | Meaning |
|---|---|---|
| `HRDW002` | error | A handler binds both a form and a body |
| `HRDW003` | error | A handler declares [`[Compress]`](/guide/compression) more than once, on the method and on its class |
| `HRDW004` | error | [`[ServerSentEvents]`](/guide/streaming) on a handler that does not return `IAsyncEnumerable<T>` |

## Responses

| Id | | Meaning |
|---|---|---|
| `HRDRM003` | error | A [response case](/guide/responses) is `object` or `dynamic`, so the dispatch would answer that case's status for every response |
| `HRDRM004` | error | Two cases at different statuses where one is assignable to the other |
| `HRDT001` | error | A [`[Throws<T>]`](/guide/responses#declaring-what-a-handler-throws) names a type with no `[HttpStatus]` and states no status of its own |
| `HRDSC001` | warning | An [authentication scheme](/guide/authentication#declaring-the-scheme) attribute is not read where it was written |

## Authorization

| Id | | Meaning |
|---|---|---|
| `HAUTH001` | warning | The module carries [`[RequireAuthorization]`](/guide/authorization#requiring-authorization-everywhere) and this handler declares nothing. `<NoWarn>` is the only lever: neither `#pragma` nor an `.editorconfig` severity affects a generator-reported diagnostic. Anything implementing `IAuthorizeAttribute` satisfies it, including attributes of your own |

## The code-first document

| Id | | Meaning |
|---|---|---|
| `HRDOA001` | error | `<HardenedOpenApiVersion>` is not `3.0.0`, `3.1.0` or `3.2.0` |
| `HRDOA002` | warning | A [streamed response](/guide/streaming#what-the-document-says) under a document version with no `itemSchema`; the operation is described without a schema |
| `HRDOA003` | warning | `[Enable<OpenApiDocumentPublishing>]` sits on a module declaring no routes, so the document is empty |

## The description build tasks

Shared between `HOAT` and `HSMT`.

| Number | | Meaning |
|---|---|---|
| `001` | error | The description file does not exist |
| `002` | error | The description could not be parsed |
| `003` | error | The description was declared as the wrong item kind |
| `004` | error | A model or generated source the extract step should have written is missing. Delete the model directory and rebuild |
| `005` | error | The targets file was imported before the specs were declared. Move the `<Import>` below the item group |
| `006` | warning | The reader had something to say, including what a degraded trait promises that the code does not enforce |
| `007` | error | A slice selected no operations |
| `008` | warning | A slice removed a schema that is still referenced; the reference degrades to `JsonElement` |
| `009` | warning | The spec is sliced but its document is embedded whole, so the served description claims operations the application does not implement |
| `015` | error | `HSMT` only. More than one `PublishUrl` or `UiUrl` |
| `016` | error | `UiUrl` without `PublishUrl` |
| `017` | error | `SourceUrl` without `EmbedDocument` |
| `026` | warning | `$(HardenedResponseModel)` is `Standard`, [the throws mode's name before 0.19.0](/guide/responses#choosing-a-mode). The mode is unchanged; write `Throws` |

### The model-diagnostics pass

Problems any description can state that would generate C# which does not compile, reported against
the document rather than as compiler errors in a generated file.

| Number | | Meaning |
|---|---|---|
| `020` | warning | A schema declares a property named like the schema itself, which C# forbids. The member is renamed; the wire name is unchanged |
| `021` | warning | Two schemas generate one C# type name. Resolved automatically; rename one to choose the names yourself |
| `022` | warning | A `oneOf` with no discriminator whose branches cannot all be told apart by shape |
| `023` | error | An `enum` declaring both string and numeric values |
| `024` | warning | A declared keyword or trait the generator does not enforce |
| `027` | error | A reference to something the description does not declare. Fatal, because a dangling `$ref` in a response silently degrades the success case to a bodyless one |

`025` is retired. It rejected two error responses at one status on one operation, which a valid
Smithy model says routinely. A declared error is now named for the error or
[binds to a shipped wrapper](/guide/responses#when-the-build-still-generates-a-type), so two shapes
at one status are two types either way. A model that used to be rejected now builds.

### The document export

`018`, `019` and `028`–`030`, reported under the prefix of the front end that wrote the document.

| Number | | Meaning |
|---|---|---|
| `018` | error | `<HardenedOpenApiOutput>` is set and the assembly carries no served document. Code-first, add `[Enable<OpenApiDocumentPublishing>]` to the module declaring the routes |
| `019` | error | The project declares more than one served document, and one output path cannot express both |
| `028` | error | The output path's extension names no format. Use `.json`, `.yaml` or `.yml` |
| `029` | error | `<HardenedOpenApiOutputVersion>` is not `3.0.0` or `3.1.0` |
| `030` | warning | The file was lowered to a version with no `itemSchema` and the named operation streams. Once per operation |

### The Smithy CLI

| Id | | Meaning |
|---|---|---|
| `HSMT010` | error | The Smithy CLI was not found. Install it, set `$(HardenedSmithyCliPath)`, or commit an AST |
| `HSMT011` | warning | The CLI is not the pinned version. An error under the pin, because a different CLI can produce a different AST from identical sources |
| `HSMT012` | error | The CLI refused the model. One error per finding |
| `HSMT013` | warning | What the CLI said without failing |
| `HSMT014` | error | The CLI exited cleanly and wrote no AST |

### The template

| Id | | Meaning |
|---|---|---|
| `HTPL001` | error | `--host aws-lambda` with `--response-model union`. The union model needs net11.0 and the Lambda managed runtime is net8.0 |
| `HTPL002` | error | The Kiota tool could not be restored, so the [client](/guide/clients) cannot be generated |
| `HTPL003` | error | The Kiota tool and `Microsoft.Kiota.Bundle` disagree. Both versions move together |

## AWS

| Id | | Meaning |
|---|---|---|
| `HRDAWS001` | error | `[LambdaWebApplication(Version = ProxyIntegrationType.ApiGateway)]` selects REST API payload format 1.0, which is not implemented |

## Renumbered in 0.18

Codes moved so every number has one meaning per prefix, and so findings from a Smithy model stopped
being reported as `HOAT`. If a `NoWarn` names an old code, update it:

`HOAT003`→`HOAT020`, `HOAT005`→`HOAT021`, `HOAT010`→`HOAT022`, `HOAT011`→`HOAT023`,
`HOAT013`→`HOAT024`, `HSPEC010`→`025` under the front end's prefix, `010`→`016` and `011`→`017`
under both prefixes, the silent-success `HSMT012`→`HSMT014`, and the multiple-`PublishUrl`
`HSMT012`→`HSMT015`.
