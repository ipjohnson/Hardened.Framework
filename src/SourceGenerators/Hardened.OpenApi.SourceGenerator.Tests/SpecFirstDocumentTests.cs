using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// The document a specification-first application publishes, read back out of the generated
/// constant and parsed strictly.
/// </summary>
/// <remarks>
/// <para>
/// Nothing covered this before: the emitter tests assert generated C# and the writer tests drive
/// <c>OpenApiDocumentGenerator</c> over hand-built handler models, so a bridge that dropped a fact
/// between the parsed specification and the writer failed neither. That is exactly what happened -
/// every parameter published as <c>{"type":"string"}</c>, with no enum, no bounds and no default,
/// while the binder and the validators enforced all of them.
/// </para>
/// <para>
/// Parsed with <see cref="JsonDocument"/>, which refuses raw control characters, so these also
/// hold the multi-line-description guarantee end to end.
/// </para>
/// </remarks>
public class SpecFirstDocumentTests {

    /// <summary>
    /// The shapes the trial found missing: a bounded integer with a default, an enum vocabulary,
    /// and a pattern - declared on parameters, where only body schemas kept their facts.
    /// </summary>
    private const string ConstrainedParameters =
        """
        openapi: "3.0.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /pets/{petId}:
            get:
              tags: [Pet]
              operationId: getPet
              description: |
                Fetch one pet.
                Second line, because multi-line prose has to survive too.
              parameters:
                - name: petId
                  in: path
                  required: true
                  schema: { type: string, pattern: '^[a-z0-9-]+$' }
                - name: limit
                  in: query
                  schema: { type: integer, format: int32, minimum: 1, maximum: 100, default: 20 }
                - name: status
                  in: query
                  schema: { type: string, enum: [available, pending, sold-out] }
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

    private static JsonElement PublishedDocumentFor(string spec) => PublishedDocument(spec);

    private static JsonElement PublishedDocument(string spec) {
        var result = OpenApiGenerator.Run(spec);

        Assert.Empty(result.Errors);

        var source = result.GeneratedSources
            .First(pair => pair.Key.Contains("OpenApiDocument")).Value;

        var match = Regex.Match(
            source, @"new byte\[\]\s*\{(.*?)\}\s*;", RegexOptions.Singleline);

        Assert.True(match.Success, "No document byte array in the generated source.");

        var bytes = match.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(byte.Parse)
            .ToArray();

        using var compressed = new MemoryStream(bytes, writable: false);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        gzip.CopyTo(inflated);

        return JsonDocument.Parse(Encoding.UTF8.GetString(inflated.ToArray())).RootElement;
    }

    private static JsonElement Parameter(JsonElement document, string name) {
        foreach (var parameter in document
                     .GetProperty("paths").GetProperty("/pets/{petId}").GetProperty("get")
                     .GetProperty("parameters").EnumerateArray()) {
            if (parameter.GetProperty("name").GetString() == name) {
                return parameter;
            }
        }

        throw new Xunit.Sdk.XunitException($"No parameter named '{name}' in the document.");
    }

    [Fact]
    public void ABoundedIntegerParameterKeepsItsTypeBoundsAndDefault() {
        var schema = Parameter(PublishedDocument(ConstrainedParameters), "limit")
            .GetProperty("schema");

        Assert.Equal("integer", schema.GetProperty("type").GetString());
        Assert.Equal("int32", schema.GetProperty("format").GetString());
        Assert.Equal(1, schema.GetProperty("minimum").GetDecimal());
        Assert.Equal(100, schema.GetProperty("maximum").GetDecimal());
        Assert.Equal(20, schema.GetProperty("default").GetDecimal());
    }

    [Fact]
    public void AnEnumParameterPublishesItsVocabulary() {
        var schema = Parameter(PublishedDocument(ConstrainedParameters), "status")
            .GetProperty("schema");

        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal(
            new[] { "available", "pending", "sold-out" },
            schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public void APatternedPathParameterPublishesItsPattern() {
        var schema = Parameter(PublishedDocument(ConstrainedParameters), "petId")
            .GetProperty("schema");

        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal("^[a-z0-9-]+$", schema.GetProperty("pattern").GetString());
    }

    /// <summary>
    /// The contract's own identity and its security reach the served document. It restated the
    /// module class name, "1.0.0", and no security at all - so a client generated from it renamed
    /// the API and sent unauthenticated requests to operations the service refuses them on.
    /// </summary>
    private const string SecuredContract =
        """
        openapi: "3.0.0"
        info: { title: Pet Store, version: "2.4.0", description: The pets. }
        security:
          - BearerAuth: []
        paths:
          /pets:
            get:
              tags: [Pet]
              operationId: listPets
              security: []
              responses:
                '200':
                  description: Pets
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Pet'
            post:
              tags: [Pet]
              operationId: createPet
              responses:
                '200':
                  description: A pet
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Pet'
        components:
          securitySchemes:
            BearerAuth:
              type: http
              scheme: bearer
          schemas:
            Pet:
              type: object
              required: [id]
              properties:
                id: { type: string }
        """;

    [Fact]
    public void TheContractsInfoBlockIsTheDocuments() {
        var info = PublishedDocumentFor(SecuredContract).GetProperty("info");

        Assert.Equal("Pet Store", info.GetProperty("title").GetString());
        Assert.Equal("2.4.0", info.GetProperty("version").GetString());
        Assert.Equal("The pets.", info.GetProperty("description").GetString());
    }

    [Fact]
    public void TheDeclaredSchemeIsPublished() {
        var scheme = PublishedDocumentFor(SecuredContract)
            .GetProperty("components").GetProperty("securitySchemes").GetProperty("BearerAuth");

        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
    }

    /// <summary>
    /// The secured operation names its requirement; the one that opted out with an empty
    /// security list declares none.
    /// </summary>
    [Fact]
    public void OperationsCarryTheirDeclaredSecurity() {
        var paths = PublishedDocumentFor(SecuredContract).GetProperty("paths").GetProperty("/pets");

        var post = paths.GetProperty("post");
        var requirement = Assert.Single(post.GetProperty("security").EnumerateArray());

        Assert.Equal(
            0, requirement.GetProperty("BearerAuth").GetArrayLength());

        Assert.False(paths.GetProperty("get").TryGetProperty("security", out _));
    }

    /// <summary>
    /// The block scalar's second line reaches <c>description</c> escaped rather than raw - the
    /// published document was not valid JSON before that, and this test cannot parse an invalid
    /// one.
    /// </summary>
    [Fact]
    public void AMultiLineDescriptionSurvivesToTheDocument() {
        var operation = PublishedDocument(ConstrainedParameters)
            .GetProperty("paths").GetProperty("/pets/{petId}").GetProperty("get");

        Assert.Contains(
            "Second line", operation.GetProperty("description").GetString());
    }
}
