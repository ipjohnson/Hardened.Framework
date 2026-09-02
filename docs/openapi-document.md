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
- **Statuses in throws mode** - `[Throws<T>(status)]` puts a thrown status into the document
  with its schema; `SuccessStatus` on the verb attribute names the success. Neither is checked
  against what the handler actually throws - the declared response models are where the compiler
  holds the set.

## Numbers, and money

`format` is an open-valued property. OpenAPI names four numeric formats - `int32`, `int64`,
`float`, `double` - and says a document may use others, so anything else is a convention rather
than a standard. What the build reads:

| declared | C# |
|---|---|
| `number` / `float` | `float` |
| `number` / `double` | `double` |
| `number` / `decimal` | `decimal` |
| `string` / `number` | `decimal` |
| `number` / anything else, or no format | `double` |
| `integer` / `int64` | `long` |
| `integer` / `uint32` | `uint` |
| `integer` / anything else | `int`, widened to `long` where a declared bound exceeds it |

Two spellings for a decimal because the ecosystem has two and neither is in the specification.
`number` + `decimal` is NSwag's, measured against 14.1.0: it answers `decimal` for that pair and
`double` for a formatless `number`. `string` + `number` is openapi-generator's, whose
`ModelUtils.isDecimalSchema` tests exactly that pair and whose language generators map it to
`BigDecimal`, `Decimal`, `decimal.Decimal` and so on.

What neither spelling can be is the *absence* of a format. openapi-generator's C# target maps
every formatless `number` to `decimal`, so a document written against it means a decimal by
saying nothing - and saying nothing is how every other document means `double`. That one is not
readable and stays `double`.

A bound on a decimal member is emitted as `Range(Min = "0.01")` rather than as a number.
`decimal` is not a legal attribute argument type in C#, and ValidationModules parses a string
bound invariantly against the property's own type at generation time - so the bound stays exact
instead of going through the `double` the member exists to avoid, and a malformed one is still a
build error.

Smithy's `BigDecimal` and `BigInteger` still degrade to `double` and `long` under `HSMT006`.
Arbitrary precision has no C# type; `decimal` would be closer than `double` and is not the same
thing, which is its own change.

Code-first, a `decimal` property publishes `number` / `decimal`, so it reads back as one.

## The vocabulary

| Declaration | Where | What it does |
|---|---|---|
| `[Enable<OpenApiDocumentPublishing>]` | entry point | emits and serves the document (code-first) |
| `<HardenedOpenApiSpec>` + `PublishUrl`/`SourceUrl`/`UiUrl` | csproj item | the contract, where the generated document, the source text and the reference page are served |
| `<HardenedSmithyModel>` / `<HardenedSmithyAst>` | csproj item | the Smithy contract, same metadata |
| `$(HardenedResponseModel)` | csproj property | `Response` / `Throws` / `Union` for a described project; absent means `Throws`, and the pre-0.19.0 value `Standard` still reads as `Throws` under a 026 warning |
| `[ResponseModel(...)]` | entry point | the same choice, code-first |
| `[OpenApiInfo(title, version, description)]` | entry point | the document's `info`, code-first |
| `[Server(url, description)]` | entry point | a `servers` entry |
| `[Throws<T>(status)]` | handler | a thrown status, into the document |
| `SuccessStatus = 201` | verb attribute | the success status, code-first throws mode |
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
constraint on a query, header or path parameter (`HRDV001` names the ways out), and
`@timestampFormat` is read for nullability and otherwise inert. Nullable scalar parameters used
to be listed here too, blamed on a type argument lost in the syntax transform. The diagnosis was
wrong: the argument survived, the definition arrived named with the C# keyword, and the schema
switch matched only CLR names. `ScalarSchema` accepts both spellings now.
