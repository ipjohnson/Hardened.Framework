using Hardened.Idl.Models;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

public class OpenApiSourceGeneratorTests {
    [Fact]
    public void Parse_PetstoreYaml_ProducesCorrectModel() {
        var yaml = File.ReadAllText(Path.Combine("Fixtures", "petstore.yaml"));

        var model = OpenApiSpecParser.Parse(yaml, "petstore", CancellationToken.None);

        Assert.NotNull(model);
        Assert.Equal("petstore", model!.FileName);

        // Schemas
        Assert.Contains(model.Schemas, s => s.Name == "Pet" && s.Kind == SchemaKind.Object);
        Assert.Contains(model.Schemas, s => s.Name == "CreatePetRequest" && s.Kind == SchemaKind.Object);
        Assert.Contains(model.Schemas, s => s.Name == "Store" && s.Kind == SchemaKind.Object);
        Assert.Contains(model.Schemas, s => s.Name == "PetStatus" && s.Kind == SchemaKind.Enum);

        // Services grouped by tag
        Assert.Contains(model.Services, s => s.Tag == "Pet");
        Assert.Contains(model.Services, s => s.Tag == "Store");

        var petService = model.Services.First(s => s.Tag == "Pet");
        Assert.Equal(4, petService.Operations.Count);

        Assert.Contains(petService.Operations, o => o.OperationId == "listPets" && o.HttpMethod == "GET");
        Assert.Contains(petService.Operations, o => o.OperationId == "createPet" && o.HttpMethod == "POST");
        Assert.Contains(petService.Operations, o => o.OperationId == "getPet" && o.HttpMethod == "GET");
        Assert.Contains(petService.Operations, o => o.OperationId == "deletePet" && o.HttpMethod == "DELETE");
    }

    [Fact]
    public void Parse_PetstoreYaml_PetSchema_HasCorrectProperties() {
        var yaml = File.ReadAllText(Path.Combine("Fixtures", "petstore.yaml"));

        var model = OpenApiSpecParser.Parse(yaml, "petstore", CancellationToken.None);
        var petSchema = model!.Schemas.First(s => s.Name == "Pet");

        Assert.Equal(4, petSchema.Properties.Count);
        Assert.Contains(petSchema.Properties, p => p.Name == "id" && p.IsRequired);
        Assert.Contains(petSchema.Properties, p => p.Name == "name" && p.IsRequired);
        Assert.Contains(petSchema.Properties, p => p.Name == "tag" && !p.IsRequired);
        Assert.Contains(petSchema.Properties, p => p.Name == "status" && !p.IsRequired);
    }

    [Fact]
    public void Parse_PetstoreYaml_Operations_HaveCorrectParameters() {
        var yaml = File.ReadAllText(Path.Combine("Fixtures", "petstore.yaml"));

        var model = OpenApiSpecParser.Parse(yaml, "petstore", CancellationToken.None);
        var petService = model!.Services.First(s => s.Tag == "Pet");

        var getPet = petService.Operations.First(o => o.OperationId == "getPet");
        Assert.Single(getPet.Parameters);
        Assert.Equal("petId", getPet.Parameters[0].Name);
        Assert.Equal("path", getPet.Parameters[0].In);
        Assert.True(getPet.Parameters[0].IsRequired);

        var listPets = petService.Operations.First(o => o.OperationId == "listPets");
        Assert.Single(listPets.Parameters);
        Assert.Equal("limit", listPets.Parameters[0].Name);
        Assert.Equal("query", listPets.Parameters[0].In);
        Assert.False(listPets.Parameters[0].IsRequired);
    }

    [Fact]
    public void Parse_InvalidContent_ReturnsNull() {
        var result = OpenApiSpecParser.Parse("not valid openapi", "test", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_AllOfSchemaComposition_MergesPropertiesAndRequired() {
        var yaml = @"
openapi: '3.0.0'
info:
  title: Test
  version: '1.0'
paths: {}
components:
  schemas:
    Base:
      type: object
      required: [id]
      properties:
        id:
          type: string
    Extended:
      allOf:
        - $ref: '#/components/schemas/Base'
        - type: object
          required: [name]
          properties:
            name:
              type: string
            description:
              type: string
";

        var model = OpenApiSpecParser.Parse(yaml, "test", CancellationToken.None);

        Assert.NotNull(model);
        var extended = model!.Schemas.First(s => s.Name == "Extended");
        Assert.Equal(SchemaKind.Object, extended.Kind);
        Assert.Contains(extended.Properties, p => p.Name == "id");
        Assert.Contains(extended.Properties, p => p.Name == "name");
        Assert.Contains(extended.Properties, p => p.Name == "description");
        Assert.Contains("name", extended.Required);
    }

    [Fact]
    public void Parse_DictionarySchema_ProducesDictionaryKind() {
        var yaml = @"
openapi: '3.0.0'
info:
  title: Test
  version: '1.0'
paths: {}
components:
  schemas:
    Metadata:
      type: object
      additionalProperties:
        type: string
";

        var model = OpenApiSpecParser.Parse(yaml, "test", CancellationToken.None);

        Assert.NotNull(model);
        var metadata = model!.Schemas.First(s => s.Name == "Metadata");
        Assert.Equal(SchemaKind.Dictionary, metadata.Kind);
        Assert.Equal("string", metadata.DictionaryValueType);
    }

    [Fact]
    public void Parse_ArraySchema_ProducesArrayKind() {
        var yaml = @"
openapi: '3.0.0'
info:
  title: Test
  version: '1.0'
paths: {}
components:
  schemas:
    PetList:
      type: array
      items:
        $ref: '#/components/schemas/Pet'
    Pet:
      type: object
      properties:
        name:
          type: string
";

        var model = OpenApiSpecParser.Parse(yaml, "test", CancellationToken.None);

        Assert.NotNull(model);
        var petList = model!.Schemas.First(s => s.Name == "PetList");
        Assert.Equal(SchemaKind.Array, petList.Kind);
        Assert.Contains("Pet", petList.ArrayItemsRef);
    }

    [Fact]
    public void Parse_InlineEnumProperty_ParsedCorrectly() {
        var yaml = @"
openapi: '3.0.0'
info:
  title: Test
  version: '1.0'
paths: {}
components:
  schemas:
    Pet:
      type: object
      properties:
        status:
          type: string
          enum:
            - available
            - pending
            - sold
";

        var model = OpenApiSpecParser.Parse(yaml, "test", CancellationToken.None);

        Assert.NotNull(model);
        var pet = model!.Schemas.First(s => s.Name == "Pet");
        var status = pet.Properties.First(p => p.Name == "status");
        Assert.NotNull(status.EnumValues);
        Assert.Equal(3, status.EnumValues!.Count);
        Assert.Contains("available", status.EnumValues);
    }

    [Fact]
    public void Parse_MissingOperationId_AutoGeneratesFromMethodAndPath() {
        var yaml = @"
openapi: '3.0.0'
info:
  title: Test
  version: '1.0'
paths:
  /pets:
    get:
      tags:
        - Pet
      responses:
        '200':
          description: List pets
";

        var model = OpenApiSpecParser.Parse(yaml, "test", CancellationToken.None);

        Assert.NotNull(model);
        var petService = model!.Services.First(s => s.Tag == "Pet");
        var op = petService.Operations.First();
        Assert.Equal("getPets", op.OperationId);
    }

    [Fact]
    public void Parse_MissingTags_DefaultsToDefaultTag() {
        var yaml = @"
openapi: '3.0.0'
info:
  title: Test
  version: '1.0'
paths:
  /health:
    get:
      operationId: healthCheck
      responses:
        '200':
          description: OK
";

        var model = OpenApiSpecParser.Parse(yaml, "test", CancellationToken.None);

        Assert.NotNull(model);
        Assert.Contains(model!.Services, s => s.Tag == "Default");
        var defaultService = model.Services.First(s => s.Tag == "Default");
        Assert.Contains(defaultService.Operations, o => o.OperationId == "healthCheck");
    }

    [Fact]
    public void Parse_HeaderParameter_ParsedWithHeaderIn() {
        var yaml = @"
openapi: '3.0.0'
info:
  title: Test
  version: '1.0'
paths:
  /secure:
    get:
      tags:
        - Auth
      operationId: secureEndpoint
      parameters:
        - name: X-Api-Key
          in: header
          required: true
          schema:
            type: string
      responses:
        '200':
          description: OK
";

        var model = OpenApiSpecParser.Parse(yaml, "test", CancellationToken.None);

        Assert.NotNull(model);
        var authService = model!.Services.First(s => s.Tag == "Auth");
        var op = authService.Operations.First();
        Assert.Single(op.Parameters);
        Assert.Equal("header", op.Parameters[0].In);
        Assert.Equal("X-Api-Key", op.Parameters[0].Name);
    }

    [Fact]
    public void Parse_MultipleResponseStatusCodes_PicksFirst2xx() {
        var yaml = @"
openapi: '3.0.0'
info:
  title: Test
  version: '1.0'
paths:
  /pets:
    post:
      tags:
        - Pet
      operationId: createPet
      responses:
        '201':
          description: Created
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Pet'
        '200':
          description: OK
components:
  schemas:
    Pet:
      type: object
      properties:
        id:
          type: string
";

        var model = OpenApiSpecParser.Parse(yaml, "test", CancellationToken.None);

        Assert.NotNull(model);
        var petService = model!.Services.First(s => s.Tag == "Pet");
        var createPet = petService.Operations.First(o => o.OperationId == "createPet");
        // Picks first 2xx ordered by key
        Assert.Equal(200, createPet.SuccessStatusCode);
    }

    [Fact]
    public void Parse_PetstoreYaml_ListPetsResponse_IsArray() {
        var yaml = File.ReadAllText(Path.Combine("Fixtures", "petstore.yaml"));

        var model = OpenApiSpecParser.Parse(yaml, "petstore", CancellationToken.None);
        var petService = model!.Services.First(s => s.Tag == "Pet");
        var listPets = petService.Operations.First(o => o.OperationId == "listPets");

        Assert.True(listPets.ResponseIsArray);
        Assert.Contains("Pet", listPets.ResponseArrayItemsRef);
    }

    [Fact]
    public void Parse_PetstoreYaml_CreatePetResponse_HasRefResponse() {
        var yaml = File.ReadAllText(Path.Combine("Fixtures", "petstore.yaml"));

        var model = OpenApiSpecParser.Parse(yaml, "petstore", CancellationToken.None);
        var petService = model!.Services.First(s => s.Tag == "Pet");
        var createPet = petService.Operations.First(o => o.OperationId == "createPet");

        Assert.NotNull(createPet.ResponseRef);
        Assert.Contains("Pet", createPet.ResponseRef);
        Assert.NotNull(createPet.RequestBodyRef);
        Assert.Contains("CreatePetRequest", createPet.RequestBodyRef);
    }
}
