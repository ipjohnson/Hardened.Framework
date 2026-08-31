# The published OpenAPI document

What `/openapi.json` says, where each fact comes from, and the vocabulary that controls it. This
is the reference for the publishing pipeline; the front-end trial found most of this vocabulary
undocumented and most of the document's facts dropped, which is what the remediation fixed.

## The design

One writer serves every front end. The OpenAPI and Smithy readers parse into `ServiceSpecModel`;
code-first builds the same model from attributes and symbols. `SpecHandlerModelBuilder` turns
either into the handler models the emitters consume, and `OpenApiDocumentGenerator` writes the
document from those models - preferring the contract's own declaration wherever one exists, and
reading the C# type only where none does. The served document is generated from the normalised
model, deliberately: a served source file is only an OpenAPI document when the source happens to
be one, and it keeps advertising whatever the front end failed to read. `SourceUrl` exists for
serving the source verbatim where that is wanted.

## What the document carries

- **`info`** - the contract's own `info` block (OpenAPI) or `@title` and the service version
  (Smithy). Code-first declares it with `[OpenApiInfo("Consignments API", "1.2.0")]` on the entry
  point; an application saying nothing gets the entry point's class name and `"1.0.0"`.
- **`servers`** - `[Server(url, description)]` on the entry point, one entry per attribute.
- **Security** - `components.securitySchemes` carries every scheme the contract declares, and
  each operation carries its declared requirements, scopes included. OpenAPI: from
  `components.securitySchemes` and the effective `security` per operation. Smithy: from the
  service's auth trait (`@httpBearerAuth`, `@httpBasicAuth`, `@httpDigestAuth`,
  `@httpApiKeyAuth`), minus operations opting out with `@auth([])`; sigv4 has no OpenAPI spelling
  and stays unpublished. Code-first declares a scheme as a type - a class implementing
  `IAuthenticationScheme`, shaped by `[HttpAuthenticationScheme]`, `[ApiKeyAuthenticationScheme]`
  or `[OAuth2AuthenticationScheme]` - and using it anywhere is declaring it:
  `[Authorize<BearerAuth>]` requires an authenticated caller and puts the scheme in the document,
  `[Authorize<BearerAuth, CanManagePets>]` conjoins a policy, and `[AuthorizeGrants]` beside
  either becomes the requirement's scope list where the scheme kind carries scopes (OAuth2), and
  "authenticated via this scheme" where it cannot - the same rule the OpenAPI reader applies in
  the other direction. A derived grants attribute or an `IGrantProvider` computes its grants at
  run time, so those operations publish the scheme requirement without scopes.
- **Parameters** - the declared wire type, format, bounds, pattern, item counts, enum vocabulary
  and default, from the contract. A parameter carrying a default publishes `required: false`,
  because the binder answers an absent value with the default. A code-first enum parameter
  carries the same vocabulary the wire converters are generated from ([JsonEnumNaming] governs
  both); one that appears only as a parameter and never in a body is not yet collected.
- **Bodies** - `$ref` schemas for named types, with constraints from the
  `ValidationModules.Constraints` vocabulary as schema facets. Nullable members carry `"null"`
  in their type; non-nullable value members are in `required`. Exclusive bounds use the JSON
  Schema 2020-12 spelling (the bound is the number), which is what 3.1 and 3.2 documents require.
- **Responses** - every declared status, each with its description, its `headers` block where the
  contract declares headers, and its `content` under the declared media type. Error statuses stay
  `application/json`, because the exception path serializes JSON whatever the success was. An
  operation with a generated validator declares the `400` that validator answers, with the
  `RequestValidationError` schema.
- **Statuses in Standard mode** - `[Throws<T>(status)]` puts a thrown status into the document
  with its schema; `SuccessStatus` on the verb attribute names the success. Neither is checked
  against what the handler actually throws - the declared response models are where the compiler
  holds the set.

## The vocabulary

| Declaration | Where | What it does |
|---|---|---|
| `[Enable<OpenApiDocumentPublishing>]` | entry point | emits and serves the document (code-first) |
| `<HardenedOpenApiSpec>` + `PublishUrl`/`SourceUrl`/`UiUrl` | csproj item | the contract, where the generated document, the source text and the reference page are served |
| `<HardenedSmithyModel>` / `<HardenedSmithyAst>` | csproj item | the Smithy contract, same metadata |
| `$(HardenedResponseModel)` | csproj property | `Standard` / `Response` / `Union` for a described project |
| `[ResponseModel(...)]` | entry point | the same choice, code-first |
| `[OpenApiInfo(title, version, description)]` | entry point | the document's `info`, code-first |
| `[Server(url, description)]` | entry point | a `servers` entry |
| `[Throws<T>(status)]` | handler | a thrown status, into the document |
| `SuccessStatus = 201` | verb attribute | the success status, code-first Standard mode |
| `[JsonEnumNaming(...)]` | assembly or enum | the wire vocabulary: converters, binder, document |
| `IAuthenticationScheme` + shape attribute | scheme class | a security scheme, declared by being used |
| `[Authorize<TAuth>]` / `[Authorize<TAuth, TPolicy>]` | handler or controller | the requirement, enforced and published |
| `ValidationModules.Constraints.*` | model properties | validation, and the schema facets |

## What holds it true

The integration suites fetch the served document and assert it against the contract:
`GeneratedDocumentTests` (OpenAPI), `SmithyServedDocumentTests` (Smithy),
`OpenApiDocumentTests` and `OpenApiDocumentEmissionTests` (code-first), and
`SpecFirstDocumentTests` drives YAML through the whole pipeline and strict-parses the result.
Strict parsing is part of the assertion: `System.Text.Json` refuses raw control characters, so a
multi-line description that stopped being escaped fails these suites rather than the reference
page.

Known gaps, stated rather than implied: code-first cannot express a
constraint on a query, header or path parameter (`HRDV001` names the ways out); a `Nullable`
scalar parameter whose type argument did not survive the syntax transform still publishes as a
string; and `@timestampFormat` is read for nullability and otherwise inert.
