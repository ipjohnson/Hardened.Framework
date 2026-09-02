# Generating from OpenAPI

Point the build at an OpenAPI document and the generator writes the models, a service interface, the
routes and the request validation. You implement the interface it wrote, so a document that gains an
operation stops the build until the implementation catches up.

## The document

Declare it as a `HardenedOpenApiSpec` item:

```xml
<ItemGroup>
    <HardenedOpenApiSpec Include="Specs\petstore.yaml" />
</ItemGroup>
```

A build task parses every declared document before the compiler runs, and the generators write C#
from what it parsed. The file's name becomes the prefix on the generated file names, so one
project can carry several. Declaring a document as `AdditionalFiles` is the old form and stops the
build with `HOAT003`; the generator no longer reads yaml directly.

```yaml
# Specs/petstore.yaml
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

The application module needs the web module and nothing else:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

[HardenedModule]
[HardenedWebModule]
public partial class Application { }
```

## The interface it produces

One interface per tag, in `<RootNamespace>.Services`, carrying the verb, path and success status in
its XML comment:

```csharp
public partial interface IPetService {
    /// <summary>GET /pets → 200</summary>
    Task<List<Pet>> ListPets(int? limit);

    /// <summary>
    /// POST /pets → 201
    ///
    /// Add a pet to the store
    /// </summary>
    Task<CreatePetResponse> CreatePet(CreatePetRequest body);

    /// <summary>GET /pets/{petId} → 200</summary>
    /// <param name="petId">The pet's identifier, as assigned by the server.</param>
    Task<Pet?> GetPet(string petId);
}
```

The models are positional records in `<RootNamespace>.Models`, carrying each schema's constraints as
validation attributes:

```csharp
public partial record Pet(
    string Id,
    string Name,
    string? Tag = default);

public partial record CreatePetRequest(
    [property: Required] [property: StringLength(Min = 1, Max = 100)] string Name,
    [property: StringLength(Max = 50)] string? Tag = default);

public partial record Problem(
    string? Type = default,
    string? Title = default,
    int? Status = default,
    string? Detail = default);
```

Three things in that interface come from the document rather than from a choice:

- `GetPet` returns `Pet?` because the operation declares a 404. Returning `null` answers it with the
  `Problem` body the document describes.
- `CreatePet` returns `CreatePetResponse`, a struct with one case — `CreatePetCreated(Pet Body,
  string Location)` — because the 201 declares a `Location` header, and `Pet` has nowhere to put one.
- `limit` is `int?` because the parameter is not required.

Both the interface and the records are `partial`, so a project adds members to either without
editing generated files. Alongside them the build emits a handler per operation, the routing table,
and a validation filter that checks the schema's constraints before your code runs. What the
filter enforces and how a failure answers is [Validation](/guide/validation).

## The implementation

Implement the interface and mark the class `[Handler]`:

```csharp
using Hardened.Requests.Abstract.Attributes;

[Handler]
public class PetService : IPetService {
    private readonly IPetStore _store;

    public PetService(IPetStore store) {
        _store = store;
    }

    public Task<List<Pet>> ListPets(int? limit) =>
        _store.List(limit ?? 20);

    public async Task<CreatePetResponse> CreatePet(CreatePetRequest body) {
        var pet = await _store.Add(body.Name, body.Tag);

        return new CreatePetCreated(pet, "/pets/" + pet.Id);
    }

    public Task<Pet?> GetPet(string petId) =>
        _store.Find(petId);
}
```

That is the whole wiring. There are no route attributes — the verbs and paths came from the
document — and nothing to register.

To explain a refusal rather than answer it with the document's default body, throw the generated
`{Operation}{Status}Exception`:

```csharp
public async Task<Pet?> GetPet(string petId) {
    if (await _store.IsArchived(petId)) {
        throw new GetPetNotFoundException(
            new Problem(Title: "Archived", Detail: $"Pet {petId} was archived."));
    }

    return await _store.Find(petId);
}
```

## Declaring the whole response set

Set `HardenedResponseModel` and every declared status becomes a case on a generated response
container, which the compiler checks you handled:

```xml
<PropertyGroup>
    <HardenedResponseModel>Response</HardenedResponseModel>
</PropertyGroup>
```

```csharp
public Task<GetPetResponse> GetPet(string petId) {
    var pet = _store.Find(petId);

    if (pet is null) {
        return Task.FromResult<GetPetResponse>(
            new GetPetNotFound(new Problem(Detail: $"No pet has id {petId}.")));
    }

    return Task.FromResult<GetPetResponse>(pet);
}
```

`Standard`, `Response` or `Union`; absent means `Standard`. [Declared responses](/guide/responses)
covers the three modes. Two rules hold in every mode: a non-200 success is honoured, so a `201` in
the document is a 201 on the wire, and an operation declaring two 2xx statuses always gets a
response container.

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

## Choosing the namespace

Generated types default to the project's `RootNamespace`, suffixed with `.Models`, `.Services` and
`.Generated`. Override the root:

```xml
<PropertyGroup>
    <HardenedOpenApiNamespace>Contoso.Petstore.Api</HardenedOpenApiNamespace>
    <CompilerVisibleProperty Include="HardenedOpenApiNamespace" />
</PropertyGroup>
```

Generated models and handlers carry `[ExcludeFromCodeCoverage]`. Turn that off with:

```xml
<PropertyGroup>
    <ExcludeGeneratedCodeFromCoverage>false</ExcludeGeneratedCodeFromCoverage>
</PropertyGroup>
```

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

`Total AdditionalTexts: 0` means the build task's output never reached the compiler, which usually
means the document is not declared as a `HardenedOpenApiSpec` item. A document still declared as
`AdditionalFiles` stops the build with `HOAT003`, whose message names the item to use. Parse
failures are raised as `HOAG002` warnings.

## Testing

Generated routes are ordinary Hardened routes, so
[the web test client](/guide/testing-web) drives them:

```csharp
public class PetServiceTests {
    [HardenedTest]
    public async Task ListPetsReturnsPets(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets?limit=1");

        response.Assert.Ok();

        Assert.NotEmpty(response.Deserialize<List<Pet>>());
    }
}
```

## Next

- [Generating from Smithy](/guide/smithy) — the same generated output from a Smithy model
- [The OpenAPI document](/guide/openapi-document) — serving a document and a reference page
- [Declared responses](/guide/responses) — the three response models in full
