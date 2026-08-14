# Generating from OpenAPI

Point the build at an OpenAPI document and the generator produces the models, the service interface,
the routes and the request validation. You write the implementation of an interface it wrote for
you, which means the specification and the code cannot disagree — a spec change that removes an
operation breaks the build rather than leaving a handler nobody calls.

## Wiring up a spec

Add the specification as an `AdditionalFiles` item and reference the OpenAPI generator:

```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>

<ItemGroup>
    <AdditionalFiles Include="Specs\petstore.yaml" />
</ItemGroup>
```

Any `.yaml`, `.yml` or `.json` file in `AdditionalFiles` is treated as a candidate specification. The
file's name — `petstore` — becomes the prefix on the generated file names, so a project can carry
several specs without collision.

The application module needs the web module, and nothing else:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

[HardenedModule]
[HardenedWebModule]
public partial class OpenApiTestApp { }
```

## What comes out

For a spec with a `Pet` schema and a `Pet`-tagged set of operations:

**Models**, in `<RootNamespace>.Models`, as positional records with nullable optionals:

```csharp
public partial record Pet(
    string Id,
    string Name,
    string? Tag = default,
    string? Status = default);
```

**A service interface per tag**, in `<RootNamespace>.Services`, with the verb, path and success
status in the XML comment:

```csharp
public partial interface IPetService {
    /// <summary>GET /pets → 200</summary>
    Task<List<Pet>> ListPets(int? limit);

    /// <summary>POST /pets → 201</summary>
    Task<Pet> CreatePet(CreatePetRequest body);

    /// <summary>GET /pets/{petId} → 200</summary>
    Task<Pet> GetPet(string petId);

    /// <summary>DELETE /pets/{petId} → 204</summary>
    Task DeletePet(string petId);
}
```

**A handler per operation**, plus the routing table and a validation filter per operation derived
from the schema's constraints — `minimum`, `maximum`, `required` and the rest are checked before your
code runs.

Both the interface and the records are `partial`, so a project can add members to either without
editing generated files.

## Implementing it

Implement the interface and mark the class `[Handler]`:

```csharp
using Hardened.Requests.Abstract.Attributes;

[Handler]
public class PetServiceImpl : IPetService {
    public Task<List<Pet>> ListPets(int? limit) { /* … */ }

    public Task<Pet> CreatePet(CreatePetRequest body) { /* … */ }

    public Task<Pet> GetPet(string petId) { /* … */ }

    public Task DeletePet(string petId) => Task.CompletedTask;
}
```

That is the whole wiring. No route attributes — the verbs and paths came from the spec — and no
registration. The generated routing table points at your implementation.

Spec-generated routes carry their verb from the document, so an operation's method never has to be
restated in C#.

## Choosing the namespace

Generated types default to the project's `RootNamespace`, suffixed with `.Models`, `.Services` and
`.Generated`. Override the root with an MSBuild property:

```xml
<PropertyGroup>
    <HardenedOpenApiNamespace>Contoso.Petstore.Api</HardenedOpenApiNamespace>
    <CompilerVisibleProperty Include="HardenedOpenApiNamespace" />
</PropertyGroup>
```

Generated models and handlers carry `[ExcludeFromCodeCoverage]` by default, so a coverage report
measures your code rather than the generator's. Turn that off with:

```xml
<PropertyGroup>
    <ExcludeGeneratedCodeFromCoverage>false</ExcludeGeneratedCodeFromCoverage>
</PropertyGroup>
```

## When nothing is generated

The generator always emits `_OpenApiDiagnostic.g.cs`, which lists every `AdditionalTexts` path it was
handed, how many parsed as OpenAPI, and any parse errors. Read that file first:

```csharp
// OpenAPI Generator Diagnostic
// Total AdditionalTexts: 1
// OpenAPI files parsed: 1
// AdditionalText paths:
//   /src/Api/Specs/petstore.yaml
```

`Total AdditionalTexts: 0` means the `AdditionalFiles` item never reached the compiler — usually a
path that does not match, or an `Include` outside any `ItemGroup` the project evaluates. Parse
failures are also raised as `HOAG002` warnings, so they appear in the IDE error list rather than only
in a generated file.

## Testing

The generated routes are ordinary Hardened routes, so
[the web test client](/guide/testing-web) drives them without knowing they came from a spec:

```csharp
public class PetControllerTests {
    [HardenedTest]
    public async Task ListPetsReturnsPets(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets?limit=1");

        response.Assert.Ok();

        var pets = response.Deserialize<List<Pet>>();

        Assert.NotEmpty(pets);
    }
}
```
