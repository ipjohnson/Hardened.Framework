using System.Linq;
using System.Threading;
using Hardened.Idl;
using Hardened.Generation;
using Hardened.Generation.Models;
using Hardened.OpenApi.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// A reference the description makes to something it never declares.
/// </summary>
/// <remarks>
/// <para>
/// OR-07. <c>$ref: '#/components/schemas/DoesNotExist'</c> in a response produced no diagnostic at
/// all: the success case became a bodyless record, so a handler written against the generated
/// interface compiled and answered 200 with an empty body. The only errors were CS0246s a hop away
/// in application code that happened to name the missing model, or - where nothing named it -
/// nothing.
/// </para>
/// <para>
/// The check has to run in the parser, for the reason the unmapped-keyword check does: every
/// reference pass after it clears what it cannot resolve, and afterwards a reference naming
/// nothing is indistinguishable from one naming something the parser deliberately resolved to a
/// different shape.
/// </para>
/// </remarks>
public class DanglingReferenceTests {

    private static string Document(string responseSchema, string components) => $$"""
        openapi: 3.0.0
        info: { title: Depot, version: '1.0' }
        paths:
          /products:
            get:
              operationId: listProducts
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema: {{responseSchema}}
        components:
          schemas:
        {{components}}
        """;

    private const string Product = """
            Product:
              type: object
              properties:
                sku: { type: string }
        """;

    private static ServiceSpecModel Parse(string yaml) {
        var model = OpenApiSpecParser.Parse(yaml, "depot", CancellationToken.None);

        Assert.NotNull(model);

        return model!;
    }

    private static SpecDiagnostics.Problem[] Problems(ServiceSpecModel model) =>
        SpecDiagnostics.Find(model, "HOAT").ToArray();

    /// <summary>The exact repro.</summary>
    [Fact]
    public void AReferenceToAnUndeclaredSchemaIsRecorded() {
        var model = Parse(Document("{ $ref: '#/components/schemas/DoesNotExist' }", Product));

        var dangling = Assert.Single(model.DanglingReferences);

        Assert.Equal("#/components/schemas/DoesNotExist", dangling.Reference);
    }

    [Fact]
    public void ItStopsTheBuild() {
        var model = Parse(Document("{ $ref: '#/components/schemas/DoesNotExist' }", Product));

        var problem = Assert.Single(Problems(model), p => p.Code == "HOAT027");

        Assert.True(problem.Fatal);
    }

    /// <summary>
    /// The message names the reference and where it was made, because a document referencing a
    /// dropped schema usually does so from several places and each is a separate edit.
    /// </summary>
    [Fact]
    public void TheMessageNamesTheReferenceAndWhereItWasMade() {
        var model = Parse(Document("{ $ref: '#/components/schemas/DoesNotExist' }", Product));

        var message = Assert.Single(Problems(model), p => p.Code == "HOAT027").Message;

        Assert.Contains("#/components/schemas/DoesNotExist", message);
        Assert.Contains("/products", message);
        Assert.Contains("(200)", message);
    }

    /// <summary>A reference from a property carries the member it was made from.</summary>
    /// <summary>
    /// A request body reference names the operation it was made from.
    /// </summary>
    [Fact]
    public void ARequestBodyReferenceNamesTheOperation() {
        var model = Parse("""
            openapi: 3.0.0
            info: { title: Depot, version: '1.0' }
            paths:
              /products:
                post:
                  operationId: createProduct
                  requestBody:
                    content:
                      application/json:
                        schema: { $ref: '#/components/schemas/Missing' }
                  responses:
                    '201': { description: created }
            components:
              schemas:
                Product:
                  type: object
                  properties:
                    sku: { type: string }
            """);

        var dangling = Assert.Single(model.DanglingReferences);

        Assert.Equal("#/components/schemas/Missing", dangling.Reference);
        Assert.Contains("request body", dangling.Location);
    }

    /// <summary>
    /// What this does not catch, pinned so nobody reads the check as complete.
    /// </summary>
    /// <remarks>
    /// A <c>$ref</c> on a schema property is resolved by the reader before this parser sees it, and
    /// an unresolvable one is discarded there: the property arrives carrying no reference, no type
    /// and no shape, and becomes <c>JsonElement</c>. The reader reports nothing about it either -
    /// its own diagnostics are empty. There is no record left in the model to report from, so
    /// catching this needs the raw document rather than the object model, which is a separate
    /// piece of work.
    /// </remarks>
    [Fact]
    public void APropertyReferenceTheReaderDiscardsIsNotCaught() {
        var model = Parse(Document("{ $ref: '#/components/schemas/Product' }", """
            Product:
              type: object
              properties:
                supplier: { $ref: '#/components/schemas/Missing' }
        """));

        var property = Assert.Single(Assert.Single(model.Schemas).Properties);

        Assert.Null(property.Ref);
        Assert.Null(property.Type);
        Assert.Empty(model.DanglingReferences);
    }

    /// <summary>
    /// A schema that is declared and produces no type is not this. A top-level array alias is read,
    /// used, and resolved into the property that names it - which is the parser working, not
    /// failing.
    /// </summary>
    [Fact]
    public void AnArrayAliasIsNotDangling() {
        var model = Parse(Document("{ $ref: '#/components/schemas/ProductList' }", """
            ProductList:
              type: array
              items: { $ref: '#/components/schemas/Product' }
            Product:
              type: object
              properties:
                sku: { type: string }
        """));

        Assert.Empty(model.DanglingReferences);
        Assert.DoesNotContain(Problems(model), p => p.Code == "HOAT027");
    }

    /// <summary>A document that declares what it references says nothing at all.</summary>
    [Fact]
    public void ADocumentThatResolvesIsSilent() {
        var model = Parse(Document("{ $ref: '#/components/schemas/Product' }", Product));

        Assert.Empty(model.DanglingReferences);
        Assert.Empty(Problems(model));
    }

    /// <summary>
    /// Every place the reference was made, not the first: two operations naming one missing schema
    /// are two edits.
    /// </summary>
    [Fact]
    public void EveryReferenceToTheMissingSchemaIsReported() {
        var model = Parse("""
            openapi: 3.0.0
            info: { title: Depot, version: '1.0' }
            paths:
              /products:
                get:
                  operationId: listProducts
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema: { $ref: '#/components/schemas/Missing' }
              /suppliers:
                get:
                  operationId: listSuppliers
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema: { $ref: '#/components/schemas/Missing' }
            components:
              schemas:
                Product:
                  type: object
                  properties:
                    sku: { type: string }
            """);

        Assert.Equal(2, model.DanglingReferences.Count);
        Assert.Equal(2, Problems(model).Count(p => p.Code == "HOAT027"));
    }

    /// <summary>
    /// Not serialized, and absent from the model's equality: the build stops on one of these, so
    /// no model carrying any is ever read back, and a diagnostic in a cache key is a cache miss
    /// for a build that generates identical code.
    /// </summary>
    [Fact]
    public void ItDoesNotTravelInTheModelFile() {
        var model = Parse(Document("{ $ref: '#/components/schemas/DoesNotExist' }", Product));

        var restored = SpecModelSerializer.Read(SpecModelSerializer.Write(model));

        Assert.NotNull(restored);
        Assert.Empty(restored!.DanglingReferences);
        Assert.Equal(model, restored);
    }
}
