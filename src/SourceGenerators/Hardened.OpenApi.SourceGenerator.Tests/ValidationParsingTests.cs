using Hardened.Idl.Models;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

public class ValidationParsingTests {
    private const string SpecWithValidation = @"
openapi: '3.0.0'
info:
  title: Test API
  version: '1.0.0'
paths:
  /items:
    post:
      tags:
        - Item
      operationId: createItem
      parameters:
        - name: limit
          in: query
          required: false
          schema:
            type: integer
            minimum: 1
            maximum: 100
        - name: search
          in: query
          required: false
          schema:
            type: string
            minLength: 1
            maxLength: 50
            pattern: '^[a-zA-Z0-9]+$'
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateItemRequest'
      responses:
        '201':
          description: Item created
components:
  schemas:
    CreateItemRequest:
      type: object
      required:
        - name
      properties:
        name:
          type: string
          minLength: 1
          maxLength: 100
        price:
          type: number
          minimum: 0
          exclusiveMinimum: true
          maximum: 10000
        tags:
          type: array
          items:
            type: string
          minItems: 0
          maxItems: 10
        status:
          type: string
          enum:
            - active
            - inactive
";

    [Fact]
    public void ParsesParameterValidationConstraints() {
        var model = OpenApiSpecParser.Parse(SpecWithValidation, "test", CancellationToken.None);

        Assert.NotNull(model);
        var service = model!.Services[0];
        var operation = service.Operations[0];

        // Check limit parameter
        var limitParam = operation.Parameters.First(p => p.Name == "limit");
        Assert.Equal(1m, limitParam.Minimum);
        Assert.Equal(100m, limitParam.Maximum);

        // Check search parameter
        var searchParam = operation.Parameters.First(p => p.Name == "search");
        Assert.Equal(1, searchParam.MinLength);
        Assert.Equal(50, searchParam.MaxLength);
        Assert.Equal("^[a-zA-Z0-9]+$", searchParam.Pattern);
    }

    [Fact]
    public void ParsesBodyPropertyValidationConstraints() {
        var model = OpenApiSpecParser.Parse(SpecWithValidation, "test", CancellationToken.None);

        Assert.NotNull(model);
        var operation = model!.Services[0].Operations[0];

        // Check body properties are extracted
        Assert.NotEmpty(operation.RequestBodyProperties);

        var nameProp = operation.RequestBodyProperties.First(p => p.Name == "name");
        Assert.True(nameProp.IsRequired);
        Assert.Equal(1, nameProp.MinLength);
        Assert.Equal(100, nameProp.MaxLength);

        var priceProp = operation.RequestBodyProperties.First(p => p.Name == "price");
        Assert.Equal(0m, priceProp.Minimum);
        Assert.True(priceProp.ExclusiveMinimum);
        Assert.Equal(10000m, priceProp.Maximum);

        var tagsProp = operation.RequestBodyProperties.First(p => p.Name == "tags");
        Assert.Equal(0, tagsProp.MinItems);
        Assert.Equal(10, tagsProp.MaxItems);
    }

    [Fact]
    public void ParsesEnumValuesOnProperties() {
        var model = OpenApiSpecParser.Parse(SpecWithValidation, "test", CancellationToken.None);

        Assert.NotNull(model);
        // Enum values on schema properties are extracted by the schema parser
        var schema = model!.Schemas.First(s => s.Name == "CreateItemRequest");
        var statusProp = schema.Properties.First(p => p.Name == "status");
        Assert.NotNull(statusProp.EnumValues);
        Assert.Contains("active", statusProp.EnumValues!);
        Assert.Contains("inactive", statusProp.EnumValues!);
    }

    [Fact]
    public void HasValidationConstraints_TrueForOperationsWithConstraints() {
        var model = OpenApiSpecParser.Parse(SpecWithValidation, "test", CancellationToken.None);

        Assert.NotNull(model);
        var operation = model!.Services[0].Operations[0];
        Assert.True(operation.HasValidationConstraints);
    }

    [Fact]
    public void NoConstraints_HasValidationConstraints_False() {
        const string simpleSpec = @"
openapi: '3.0.0'
info:
  title: Simple API
  version: '1.0.0'
paths:
  /items:
    get:
      tags:
        - Item
      operationId: listItems
      responses:
        '200':
          description: A list of items
components:
  schemas: {}
";

        var model = OpenApiSpecParser.Parse(simpleSpec, "test", CancellationToken.None);

        Assert.NotNull(model);
        var operation = model!.Services[0].Operations[0];
        Assert.False(operation.HasValidationConstraints);
    }
}
