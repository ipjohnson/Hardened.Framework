# Generating from OpenAPI

Point the build at an OpenAPI document and it writes the models, a service interface, the routes
and the request validation. You implement the interface. A document that gains an operation fails
the build until the implementation has the method.

```csharp
public partial interface IPetService {
    /// <summary>GET /pets → 200</summary>
    Task<List<Pet>> ListPets(int? limit);

    /// <summary>POST /pets → 201</summary>
    Task<CreatePetResponse> CreatePet(CreatePetRequest body);

    /// <summary>GET /pets/{petId} → 200</summary>
    Task<Pet?> GetPet(string petId);
}
```

```csharp
using Hardened.Requests.Abstract.Attributes;

[Handler]
public class PetService(IPetStore store) : IPetService {

    public Task<List<Pet>> ListPets(int? limit) => store.List(limit ?? 20);

    public async Task<CreatePetResponse> CreatePet(CreatePetRequest body) {
        var pet = await store.Add(body.Name, body.Tag);

        return new CreatePetCreated(pet, "/pets/" + pet.Id);
    }

    public Task<Pet?> GetPet(string petId) => store.Find(petId);
}
```

That is the whole wiring. There are no route attributes, because the verbs and paths came from
the document, and there is nothing to register. The interface, the records, a handler per
operation, the routing table and a validation filter for the schema's constraints are all written
by the build.

## The document

Declare it as a `HardenedOpenApiSpec` item:

```xml
<ItemGroup>
    <HardenedOpenApiSpec Include="Specs\petstore.yaml" />
</ItemGroup>
```

A build task parses every declared document before the compiler runs, and the generators write
C# from what it parsed. The file's name becomes the prefix on the generated file names, so one
project can carry several. Declaring a document as `AdditionalFiles` is the old form and stops
the build with `HOAT003`.

The application module needs the web module and nothing else:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

[HardenedModule]
[HardenedWebModule]
public partial class Application { }
```

The interface above came from this document:

::: details Specs/petstore.yaml
```yaml
openapi: "3.0.0"
info:
  title: Petstore
  version: "1.0.0"
paths:
  /pets:
    get:
      tags: [Pet]
      operationId: listPets
      parameters:
        - name: limit
          in: query
          schema: { type: integer, format: int32, minimum: 1, maximum: 100 }
      responses:
        '200':
          description: A list of pets
          content:
            application/json:
              schema:
                type: array
                items: { $ref: '#/components/schemas/Pet' }
    post:
      tags: [Pet]
      operationId: createPet
      summary: Add a pet to the store
      requestBody:
        content:
          application/json:
            schema: { $ref: '#/components/schemas/CreatePetRequest' }
      responses:
        '201':
          description: Pet created
          headers:
            Location:
              schema: { type: string }
          content:
            application/json:
              schema: { $ref: '#/components/schemas/Pet' }
  /pets/{petId}:
    get:
      tags: [Pet]
      operationId: getPet
      parameters:
        - name: petId
          in: path
          required: true
          description: The pet's identifier, as assigned by the server.
          schema: { type: string }
      responses:
        '200':
          description: A single pet
          content:
            application/json:
              schema: { $ref: '#/components/schemas/Pet' }
        '404':
          description: No pet with that id
          content:
            application/json:
              schema: { $ref: '#/components/schemas/Problem' }
components:
  schemas:
    Pet:
      type: object
      required: [id, name]
      properties:
        id:   { type: string }
        name: { type: string }
        tag:  { type: string }
    CreatePetRequest:
      type: object
      required: [name]
      properties:
        name: { type: string, minLength: 1, maxLength: 100 }
        tag:  { type: string, maxLength: 50 }
    Problem:
      type: object
      properties:
        type:   { type: string }
        title:  { type: string }
        status: { type: integer, format: int32 }
        detail: { type: string }
```
:::

## What the document decides

Three things in the interface came from the document rather than from a choice:

- `GetPet` returns `Pet?` because the operation declares a 404. Returning `null` answers it with
  the `Problem` body the document describes.
- `CreatePet` returns `CreatePetResponse`, a struct with one case,
  `CreatePetCreated(Pet Body, string Location)`, because the 201 declares a `Location` header and
  `Pet` has nowhere to put one.
- `limit` is `int?` because the parameter is not required.

The interface is one per tag, in `<RootNamespace>.Services`, carrying the verb, path and success
status in its XML comment, and the operation's `summary` and `description` beneath. The models are
positional records in `<RootNamespace>.Models`, carrying each schema's constraints as validation
attributes:

```csharp
public partial record Pet(
    string Id,
    string Name,
    string? Tag = default);

public partial record CreatePetRequest(
    [property: Required] [property: StringLength(Min = 1, Max = 100)] string Name,
    [property: StringLength(Max = 50)] string? Tag = default);
```

Both the interface and the records are `partial`, so a project adds members to either without
editing generated files. What the validation filter enforces and how a failure answers is
[Validation](/guide/validation).

## Refusing with a body

To explain a refusal rather than answer with the document's default body, throw the response the
declared status binds to:

```csharp
public async Task<Pet?> GetPet(string petId) {
    if (await store.IsArchived(petId)) {
        throw new NotFound<Problem>(
            new Problem(Title: "Archived", Detail: $"Pet {petId} was archived.")).AsException();
    }

    return await store.Find(petId);
}
```

An anonymous error, which is what a `$ref` to a shared `Problem` schema is, binds to the record
the framework ships for that status. A code-first handler returns the same type. An error under a
`components/responses` key keeps that name instead; see
[When the build still generates a type](/guide/responses#when-the-build-still-generates-a-type).

## Declaring the whole response set

Set `HardenedResponseModel` and every declared status becomes a case on a generated response
container, which the compiler checks you handled:

```xml
<PropertyGroup>
    <HardenedResponseModel>Response</HardenedResponseModel>
</PropertyGroup>
```

```csharp
public async Task<GetPetResponse> GetPet(string petId) {
    var pet = await store.Find(petId);

    if (pet is null) {
        return new NotFound("pet", $"No pet has id {petId}.");
    }

    return pet;
}
```

The 404 is the framework's own `NotFound`. The container converts it into the `NotFound<Problem>`
the document declares, filling the `Problem` from the record and its detail from the handler.
[Declared responses](/guide/responses#specification-first) has the rule.

`Throws`, `Response` or `Union`; absent means `Throws`. Two rules hold in every mode: a non-200
success is honoured, so a `201` in the document is a 201 on the wire, and an operation declaring
two 2xx statuses always gets a response container.

## Attributes on the implementation

A `[Handler]` class implements a generated interface, and attributes go on its methods as they
would anywhere. Which of them mean anything depends on when they are read:

| | Read at | On a `[Handler]` method |
|---|---|---|
| `[Retry]`, `[RateLimit]`, `[CacheResponse<T>]`, `[Compress]`, `[ConditionalGet]`, `[Timeout]`, your own `IRequestFilterProvider` | run time, off the handler's metadata | Honoured. This is where a per-operation filter goes in a spec-first project |
| `[AuthorizeGrants]`, `[Authorize<TAuth>]`, `[AllowAnonymous]`, an `IAuthorizationConvention` | run time, into the handler's `Requirement` | Honoured, and can only narrow what the contract admits |
| `[Throws<T>]`, `[Tag]`, `[Server]` | build time, into the document | Inert. The build task writes the document from the contract before the compiler runs, so it never sees them |

Anything shaping the document has to be in the description. Anything shaping the pipeline can be
on the implementation:

```csharp
[Handler]
public class PetService : IPetService {

    [CacheResponse<VaryByRoute>(Duration = 60, Scope = CacheScope.AllCallers)]
    [ConditionalGet]
    public Task<Pet?> GetPet(string petId) => store.Find(petId);
}
```

### Filters from the description

A filter can also come from the description itself, with `x-filters` on the operation:

```yaml
paths:
  /pets/{petId}:
    get:
      operationId: getPet
      x-filters:
        Audit:
          Category: catalog
```

Each key names a filter attribute type and its object supplies property values. `x-filter-types`
at the document root declares the types, each with a `namespace` and its `properties`, and
`generate: false` for one that already exists in a referenced library.

Reach for `x-filters` when the description is the artefact several implementations share. Reach
for an attribute on the method when the filter belongs to this implementation.

### A deadline from the description

A deadline has a field of its own:

```yaml
paths:
  /rates:
    get:
      operationId: readRates
      x-hardened-timeout: 2000
```

The scalar is the budget in milliseconds. An object carries `status` and `retryAfterSeconds`
beside it for an operation shedding load. It reaches the handler as the same
[`[Timeout]`](/guide/request-timeouts) a code-first handler carries, and the nearest declaration
wins: a `[Timeout]` on the implementation's method overrides what the description said.

## Serving the document

Say where it publishes on the item that declares it:

```xml
<ItemGroup>
    <HardenedOpenApiSpec Include="Specs\petstore.yaml">
        <PublishUrl>/openapi.yaml</PublishUrl>
        <UiUrl>/docs</UiUrl>
    </HardenedOpenApiSpec>
</ItemGroup>
```

The document is embedded verbatim and served at `PublishUrl` with the content type its extension
implies. The reference page at `UiUrl` reads it. See
[The OpenAPI document](/guide/openapi-document).

## Build properties

Generated types default to the project's `RootNamespace`, suffixed with `.Models`, `.Services` and
`.Generated`. Override the root:

```xml
<PropertyGroup>
    <HardenedOpenApiNamespace>Contoso.Petstore.Api</HardenedOpenApiNamespace>
    <CompilerVisibleProperty Include="HardenedOpenApiNamespace" />
</PropertyGroup>
```

Generated models and handlers carry `[ExcludeFromCodeCoverage]`. Turn that off with
`<ExcludeGeneratedCodeFromCoverage>false</ExcludeGeneratedCodeFromCoverage>`.

## When nothing is generated

The generator always emits `_SpecModelDiagnostic.g.cs`, listing every parsed model the build task
handed it and any parse errors:

```csharp
// OpenAPI Generator Diagnostic
// Total AdditionalTexts: 1
// OpenAPI files parsed: 1
// AdditionalText paths:
//   /src/Api/obj/Debug/net8.0/openapi/petstore.openapi-model.txt
```

`Total AdditionalTexts: 0` means the build task's output never reached the compiler, which
usually means the document is not declared as a `HardenedOpenApiSpec` item. Parse failures are
raised as `HOAG002` warnings.

## Testing

Generated routes are ordinary Hardened routes, so `ITestWebApp` and a generated client drive them
through the same pipeline. See [Sending requests](/guide/testing-web) and
[Typed clients](/guide/testing-clients).

## Next

- [Generating from Smithy](/guide/smithy): the same generated output from a Smithy model
- [The OpenAPI document](/guide/openapi-document): serving a document and a reference page
- [Declared responses](/guide/responses): the three response models in full
