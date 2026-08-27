using System.Linq;
using System.Threading;
using Hardened.Generation.Models;
using Hardened.OpenApi.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// A model nothing derives from is sealed.
/// </summary>
/// <remarks>
/// <para>
/// For the validator, not for the reader. A validator for a sealed type needs no rules for a type
/// that might inherit it - it knows the members it can be handed are the members it can see - and a
/// request or response model is exactly the case where nothing does inherit it.
/// </para>
/// <para>
/// Sealed and partial answer different questions and both are set. Sealed forbids deriving; partial
/// permits extending in place, which is how an application adds an interface or a computed member to
/// a type it did not write. Sealing to make the validator's job smaller never required refusing
/// that, and the response case types have been emitted this way since headers landed.
/// </para>
/// </remarks>
public class SealedLeafModelTests {

    /// <summary>
    /// Emits every schema the document declares, each against the full list.
    /// </summary>
    /// <remarks>
    /// The two-argument harness overload, because that is what the real emitter is handed -
    /// <c>SpecFileEmitter</c> passes <c>model.Schemas</c>. Whether a schema is a leaf cannot be
    /// answered from the schema alone, so the one-argument overload could only ever say "unknown".
    /// </remarks>
    private static string Emit(string schemas) {
        var document = $$"""
            openapi: 3.0.0
            info: { title: Depot, version: '1.0' }
            paths:
              /parts:
                get:
                  operationId: listParts
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: string
            components:
              schemas:
            {{schemas}}
            """;

        var model = OpenApiSpecParser.Parse(document, "depot", CancellationToken.None);

        Assert.NotNull(model);

        var all = model!.Schemas.ToList();

        return string.Join("\n", all.Select(schema => EmitterHarness.Schema(schema, all)));
    }

    /// <summary>
    /// The ordinary case, and the one the whole change is for.
    /// </summary>
    [Fact]
    public void AModelNothingDerivesFromIsSealed() {
        var emitted = Emit("""
                Part:
                  type: object
                  properties:
                    sku: { type: string }
            """);

        Assert.Contains("public sealed partial record Part(", emitted);
    }

    /// <summary>
    /// A schema something genuinely derives from is a base, and sealing it would make the document
    /// unrepresentable.
    /// </summary>
    /// <remarks>
    /// Discriminated, because that is the only arrangement that produces inheritance here.
    /// <c>OpenApiSpecParser</c> sets <c>BaseRef</c> only when an <c>allOf</c> branch references a
    /// schema carrying a discriminator; a plain <c>allOf</c> is flattened into the deriving schema
    /// instead. So a document without discriminators is all leaves, and all of it seals.
    /// </remarks>
    [Fact]
    public void AModelSomethingDerivesFromIsNotSealed() {
        var emitted = Emit("""
                Part:
                  type: object
                  required: [kind]
                  properties:
                    kind: { type: string }
                    sku: { type: string }
                  discriminator:
                    propertyName: kind
                    mapping:
                      motor: '#/components/schemas/MotorPart'
                MotorPart:
                  allOf:
                    - $ref: '#/components/schemas/Part'
                    - type: object
                      properties:
                        voltage: { type: integer }
            """);

        Assert.Contains("public partial record Part(", emitted);
        Assert.DoesNotContain("public sealed partial record Part(", emitted);
    }

    /// <summary>
    /// And the derived end of that pair is itself a leaf, so it seals.
    /// </summary>
    [Fact]
    public void TheDerivedEndOfAHierarchyIsStillSealed() {
        var emitted = Emit("""
                Part:
                  type: object
                  required: [kind]
                  properties:
                    kind: { type: string }
                    sku: { type: string }
                  discriminator:
                    propertyName: kind
                    mapping:
                      motor: '#/components/schemas/MotorPart'
                MotorPart:
                  allOf:
                    - $ref: '#/components/schemas/Part'
                    - type: object
                      properties:
                        voltage: { type: integer }
            """);

        Assert.Contains("public sealed partial record MotorPart(", emitted);
    }

    /// <summary>
    /// A schema declaring a discriminator is a polymorphic base whether or not this slice of the
    /// document happens to contain anything mapped to it.
    /// </summary>
    [Fact]
    public void APolymorphicBaseIsNotSealed() {
        var emitted = Emit("""
                Part:
                  type: object
                  required: [kind]
                  properties:
                    kind: { type: string }
                  discriminator:
                    propertyName: kind
            """);

        Assert.DoesNotContain("public sealed partial record Part", emitted);
    }

    /// <summary>
    /// Every model is still partial, sealed or not. That is what lets an application extend a type
    /// it did not write, and it is a separate question from whether anything may derive from it.
    /// </summary>
    [Fact]
    public void EveryModelIsStillPartial() {
        var emitted = Emit("""
                Part:
                  type: object
                  required: [kind]
                  properties:
                    kind: { type: string }
                    sku: { type: string }
                  discriminator:
                    propertyName: kind
                    mapping:
                      motor: '#/components/schemas/MotorPart'
                MotorPart:
                  allOf:
                    - $ref: '#/components/schemas/Part'
                    - type: object
                      properties:
                        voltage: { type: integer }
            """);

        var declarations = emitted
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains(" record ") && line.StartsWith("public", System.StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(declarations);
        Assert.All(declarations, line => Assert.Contains("partial record", line));
    }

}
