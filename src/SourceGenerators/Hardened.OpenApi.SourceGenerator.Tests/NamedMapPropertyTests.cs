using System.Text.Json;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// A property that refers to a named map, from the contract to the generated model and the
/// published document.
/// </summary>
/// <remarks>
/// It generated a <c>JsonElement?</c> and published <c>{"type":"string"}</c>, so a client generated
/// from the document sent a string where the contract declared a map. The same map written inline
/// was a <c>Dictionary</c> and published as one.
/// </remarks>
public class NamedMapPropertyTests
{
    private const string Contract = """
        openapi: "3.0.0"
        info: { title: Catalog, version: "1.0" }
        paths:
          /products/{id}:
            get:
              tags: [Catalog]
              operationId: getProduct
              parameters:
                - name: id
                  in: path
                  required: true
                  schema: { type: string }
              responses:
                '200':
                  description: The product
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Product'
        components:
          schemas:
            Product:
              type: object
              required: [sku]
              properties:
                sku: { type: string }
                labels:
                  $ref: '#/components/schemas/Labels'
            Labels:
              type: object
              description: Labels by key.
              additionalProperties:
                type: string
        """;

    [Fact]
    public void TheModelTypesThePropertyAsTheMap()
    {
        var result = OpenApiGenerator.Run(Contract);

        result.AssertNoErrors();

        Assert.Contains(
            "global::System.Collections.Generic.Dictionary<string,string>? Labels",
            result.SourceContaining("petstore.g.cs")
        );
    }

    [Fact]
    public void TheDocumentPublishesThePropertyAsTheMap()
    {
        var result = OpenApiGenerator.Run(Contract);

        result.AssertNoErrors();

        var labels = SpecFirstDocumentTests
            .PublishedDocumentFrom(result)
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("Product")
            .GetProperty("properties")
            .GetProperty("labels");

        Assert.Equal("object", labels.GetProperty("type").GetString());
        Assert.Equal(
            "string",
            labels.GetProperty("additionalProperties").GetProperty("type").GetString()
        );
        Assert.Equal("Labels by key.", labels.GetProperty("description").GetString());
    }
}
