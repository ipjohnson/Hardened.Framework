using System.Collections.Generic;
using System.Threading;
using Hardened.Idl.Models;
using Hardened.OpenApi.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// A <c>oneOf</c> becoming a type that holds exactly one of its branches.
/// </summary>
/// <remarks>
/// The property used to land on <c>JsonElement</c>: the payload arrived as unparsed JSON, the
/// branch types were reachable from nothing so they were not generated, and a caller who wanted a
/// <c>Cat</c> had neither the type nor a way to know it was one. What is asserted here is that the
/// document decides - a discriminator resolves the branch, and without one nothing is guessed.
/// </remarks>
public class OneOfTests {

    private static ServiceSpecModel Parse(string yaml) {
        var model = OpenApiSpecParser.Parse(yaml, "spec", CancellationToken.None);

        Assert.NotNull(model);

        return model!;
    }

    private static SchemaModel? Choice(ServiceSpecModel model) {
        foreach (var schema in model.Schemas) {
            if (schema.Kind == SchemaKind.OneOf) {
                return schema;
            }
        }

        return null;
    }

    [Fact]
    public void ADiscriminatedChoiceBecomesATypeNamedForWhereItIsDeclared() {
        var model = Parse(Discriminated);

        var choice = Choice(model);

        Assert.NotNull(choice);

        // Named for its owner and property, not for its branches, so adding a branch does not
        // rename it and fifteen branches do not produce a fifteen-word name.
        Assert.Equal("HolderPayload", choice!.Name);
        Assert.Equal("kind", choice.DiscriminatorPropertyName);
        Assert.Equal(2, choice.OneOf.Count);
    }

    /// <summary>The property is typed as the choice, which is the whole point.</summary>
    [Fact]
    public void ThePropertyIsTypedAsTheChoiceRatherThanLeftLoose() {
        var model = Parse(Discriminated);

        var holder = Assert.Single(model.Schemas, schema => schema.Name == "Holder");
        var payload = Assert.Single(holder.Properties, property => property.Name == "payload");

        Assert.NotNull(payload.Ref);
        Assert.Equal("HolderPayload", Hardened.Idl.TypeMapper.GetRefName(payload.Ref!));
    }

    /// <summary>
    /// A bare discriminator maps each value to the schema it names, which is what the
    /// specification says it means and what most descriptions rely on.
    /// </summary>
    [Fact]
    public void ABareDiscriminatorMapsEachValueToTheSchemaItNames() {
        var model = Parse(Discriminated);

        var choice = Choice(model)!;
        var values = new List<string>();

        foreach (var mapping in choice.DiscriminatorMapping) {
            values.Add(mapping.Value);
        }

        Assert.Contains("Cat", values);
        Assert.Contains("Dog", values);
    }

    [Fact]
    public void AnExplicitMappingIsUsedWhereTheDocumentDeclaresOne() {
        var model = Parse(ExplicitMapping);

        var choice = Choice(model)!;
        var values = new List<string>();

        foreach (var mapping in choice.DiscriminatorMapping) {
            values.Add(mapping.Value);
        }

        Assert.Contains("feline", values);
        Assert.Contains("canine", values);
    }

    /// <summary>
    /// No discriminator, but the shapes decide it - which is most of the published corpus.
    /// </summary>
    /// <remarks>
    /// <c>Cat</c> declares <c>meow</c> and <c>Dog</c> declares <c>bark</c>, so the presence of
    /// either says which branch a payload is. That is proved here rather than attempted at run
    /// time: the generated converter tests for the property instead of trying each branch and
    /// keeping whichever happens to read.
    /// </remarks>
    [Fact]
    public void AChoiceWithNoDiscriminatorIsResolvedWhenTheShapesDecideIt() {
        var model = Parse(Undiscriminated);

        var choice = Choice(model);

        Assert.NotNull(choice);
        Assert.Null(choice!.DiscriminatorPropertyName);
        Assert.Equal(2, choice.OneOf.Count);
    }

    /// <summary>
    /// Branches the schemas do not separate still get a type, decided when a payload arrives.
    /// </summary>
    /// <remarks>
    /// Overlapping on paper is not the same as ambiguous in fact: two schemas whose properties are
    /// all optional overlap until a payload turns up carrying one that only one of them declares.
    /// Refusing to generate anything would throw away every such payload to guard against the ones
    /// that genuinely collide, so the branch is generated and the count decides - which is what
    /// openapi-generator does, and unlike serde's first-one-that-reads it cannot bind silently.
    /// </remarks>
    [Fact]
    public void BranchesTheSchemasDoNotSeparateAreDecidedByCountingMatches() {
        var model = Parse(Ambiguous);

        var choice = Choice(model);

        Assert.NotNull(choice);

        var plan = Hardened.Idl.ChoiceResolution.Resolve(choice!.OneOf, model.Schemas);

        Assert.False(plan.FullyProved);
        Assert.Equal(2, plan.Overlapping.Count);

        // And the generated converter counts rather than taking the first that reads.
        var emitted = EmitterHarness.Schema(choice);

        Assert.Contains("var matches = 0;", emitted);
        Assert.Contains("if (matches == 1)", emitted);
        Assert.Contains("permitted types at once", emitted);
    }

    /// <summary>An inline branch, which most published choices are made of.</summary>
    [Fact]
    public void InlineBranchesAreModelledAsWellAsNamedOnes() {
        var model = Parse(InlinePrimitives);

        var choice = Choice(model);

        Assert.NotNull(choice);
        Assert.Equal(2, choice!.OneOf.Count);

        // Neither branch names a schema; both are types written in place.
        foreach (var branch in choice.OneOf) {
            Assert.Null(branch.Ref);
            Assert.NotNull(branch.Type);
        }

        var plan = Hardened.Idl.ChoiceResolution.Resolve(choice.OneOf, model.Schemas);

        Assert.True(plan.FullyProved);
    }

    /// <summary>Branches of different JSON kinds, which is 56% of the corpus's choices.</summary>
    [Fact]
    public void BranchesOfDifferentValueKindsAreDecidedByKind() {
        var model = Parse(DifferentKinds);

        var choice = Choice(model);

        Assert.NotNull(choice);

        var plan = Hardened.Idl.ChoiceResolution.Resolve(choice!.OneOf, model.Schemas);

        Assert.True(plan.FullyProved);

        foreach (var branch in plan.Branches) {
            Assert.NotNull(branch.ValueKind);
        }
    }

    /// <summary>The one that is easy to leave silent.</summary>
    [Fact]
    public void AnUnresolvableChoiceIsReported() {
        var model = Parse(Ambiguous);

        var problems = Hardened.Idl.SpecDiagnostics.Find(model);
        var codes = new List<string>();

        foreach (var problem in problems) {
            codes.Add(problem.Code);
        }

        Assert.Contains("HOAT010", codes);

        // Reported, not fatal: JsonElement is a working answer, just the weakest one.
        foreach (var problem in problems) {
            if (problem.Code == "HOAT010") {
                Assert.False(problem.Fatal);
            }
        }
    }

    /// <summary>
    /// The converter writes the discriminator itself, from the runtime type.
    /// </summary>
    /// <remarks>
    /// It used to be left to the branch's own properties, which went wrong two ways: a branch whose
    /// discriminator property was never assigned wrote null and could not be read back, and one
    /// assigned the other branch's value round-tripped into that branch - a Cat written and a Dog
    /// read, silently. What the payload is is the document's statement, not the caller's.
    /// </remarks>
    [Fact]
    public void TheDiscriminatorIsWrittenFromTheTypeRatherThanTheModel() {
        var model = Parse(Discriminated);

        var choice = Choice(model)!;
        var emitted = EmitterHarness.Schema(choice);

        // Each branch is written with the value the mapping gives it, passed to the writer rather
        // than read back off the value.
        Assert.Contains("Write(writer, branch, options, \"kind\", \"Cat\");", emitted);
        Assert.Contains("Write(writer, branch, options, \"kind\", \"Dog\");", emitted);

        // And the property the model carries is skipped, so it cannot be written twice or disagree.
        Assert.Contains("writer.WriteString(discriminator, kind);", emitted);
        Assert.Contains("if (property.NameEquals(discriminator))", emitted);
    }

    /// <summary>A single branch is not a choice, and does not need a type to say so.</summary>
    [Fact]
    public void AOneOfWithOneBranchIsNotGivenAType() {
        var model = Parse(SingleBranch);

        Assert.Null(Choice(model));
    }

    private const string SingleBranch = """
        openapi: "3.0.3"
        info: { title: T, version: "1.0" }
        paths:
          /holders:
            get:
              tags: [Holder]
              operationId: getHolder
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Holder' }
        components:
          schemas:
            Holder:
              type: object
              properties:
                payload:
                  oneOf:
                    - $ref: '#/components/schemas/Cat'
                  discriminator: { propertyName: kind }
            Cat:
              type: object
              properties: { kind: { type: string }, meow: { type: boolean } }
        """;

    private const string Discriminated = """
        openapi: "3.0.3"
        info: { title: T, version: "1.0" }
        paths:
          /holders:
            get:
              tags: [Holder]
              operationId: getHolder
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Holder' }
        components:
          schemas:
            Holder:
              type: object
              properties:
                payload:
                  oneOf:
                    - $ref: '#/components/schemas/Cat'
                    - $ref: '#/components/schemas/Dog'
                  discriminator: { propertyName: kind }
            Cat:
              type: object
              properties: { kind: { type: string }, meow: { type: boolean } }
            Dog:
              type: object
              properties: { kind: { type: string }, bark: { type: boolean } }
        """;

    private const string ExplicitMapping = """
        openapi: "3.0.3"
        info: { title: T, version: "1.0" }
        paths:
          /holders:
            get:
              tags: [Holder]
              operationId: getHolder
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Holder' }
        components:
          schemas:
            Holder:
              type: object
              properties:
                payload:
                  oneOf:
                    - $ref: '#/components/schemas/Cat'
                    - $ref: '#/components/schemas/Dog'
                  discriminator:
                    propertyName: kind
                    mapping:
                      feline: '#/components/schemas/Cat'
                      canine: '#/components/schemas/Dog'
            Cat:
              type: object
              properties: { kind: { type: string }, meow: { type: boolean } }
            Dog:
              type: object
              properties: { kind: { type: string }, bark: { type: boolean } }
        """;

    /// <summary>A string beside a boolean, named by nothing - 59% of the corpus's choices.</summary>
    private const string InlinePrimitives = """
        openapi: "3.0.3"
        info: { title: T, version: "1.0" }
        paths:
          /holders:
            get:
              tags: [Holder]
              operationId: getHolder
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Holder' }
        components:
          schemas:
            Holder:
              type: object
              properties:
                payload:
                  oneOf:
                    - type: string
                    - type: boolean
        """;

    /// <summary>Two branches of the same shape, which nothing separates on paper.</summary>
    private const string Ambiguous = """
        openapi: "3.0.3"
        info: { title: T, version: "1.0" }
        paths:
          /holders:
            get:
              tags: [Holder]
              operationId: getHolder
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Holder' }
        components:
          schemas:
            Holder:
              type: object
              properties:
                payload:
                  oneOf:
                    - $ref: '#/components/schemas/Left'
                    - $ref: '#/components/schemas/Right'
            Left:
              type: object
              properties: { name: { type: string } }
            Right:
              type: object
              properties: { name: { type: string } }
        """;

    /// <summary>A string branch beside an object one - decided by the payload's kind alone.</summary>
    private const string DifferentKinds = """
        openapi: "3.0.3"
        info: { title: T, version: "1.0" }
        paths:
          /holders:
            get:
              tags: [Holder]
              operationId: getHolder
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Holder' }
        components:
          schemas:
            Holder:
              type: object
              properties:
                payload:
                  oneOf:
                    - $ref: '#/components/schemas/Slug'
                    - $ref: '#/components/schemas/Cat'
            Slug:
              type: string
            Cat:
              type: object
              properties: { kind: { type: string }, meow: { type: boolean } }
        """;

    private const string Undiscriminated = """
        openapi: "3.0.3"
        info: { title: T, version: "1.0" }
        paths:
          /holders:
            get:
              tags: [Holder]
              operationId: getHolder
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Holder' }
        components:
          schemas:
            Holder:
              type: object
              properties:
                payload:
                  oneOf:
                    - $ref: '#/components/schemas/Cat'
                    - $ref: '#/components/schemas/Dog'
            Cat:
              type: object
              properties: { kind: { type: string }, meow: { type: boolean } }
            Dog:
              type: object
              properties: { kind: { type: string }, bark: { type: boolean } }
        """;
}
