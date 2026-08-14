namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// Documents the task is driven with.
/// </summary>
/// <remarks>
/// A deliberately small set. The parser's own behaviour is covered by <c>SpecParsingTests</c> in
/// <c>Hardened.OpenApi.SourceGenerator.Tests</c>, which compiles the parser in and has the full
/// fixture library; what is needed here is one document that parses and several that do not.
/// </remarks>
internal static class Specs {

    /// <summary>The smallest spec that produces a schema and an operation.</summary>
    internal const string Minimal =
        """
        openapi: "3.0.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /pets:
            get:
              tags: [Pet]
              operationId: listPets
              responses:
                '200':
                  description: A pet
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Pet'
        components:
          schemas:
            Pet:
              type: object
              required: [id]
              properties:
                id: { type: string }
        """;

    /// <summary>Valid YAML that is not an OpenAPI document — a compose file, the usual mistake.</summary>
    internal const string NotOpenApiYaml =
        """
        version: "3.9"
        services:
          web:
            image: nginx
            ports:
              - "80:80"
        """;

    /// <summary>Valid JSON that is not an OpenAPI document.</summary>
    internal const string NotOpenApiJson =
        """
        { "Logging": { "LogLevel": { "Default": "Information" } } }
        """;
}
