using System.Linq;
using System.Threading;
using Hardened.Generation.Models;
using Hardened.OpenApi.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// Constraints written on an array's inline item schema, which are read by nothing.
/// </summary>
/// <remarks>
/// <para>
/// An array property keeps three things from its <c>items</c>: the reference, the type and the
/// format. Every constraint beside them was dropped without a word - so
/// <c>items: { type: string, minLength: 3 }</c> declared a rule, generated
/// <c>List&lt;string&gt;</c>, and accepted the empty string at runtime.
/// </para>
/// <para>
/// This is not the nested-array case the previous review closed. An <c>items</c> naming a schema
/// gets its properties walked and constrained as any other model's are; what was dropped is a
/// constraint on the element <em>itself</em> - the shape somebody writes for an array of primitives,
/// and the shape with nowhere else to put the rule.
/// </para>
/// </remarks>
public class ArrayItemConstraintTests {

    private static string Document(string itemSchema) => $$"""
        openapi: 3.0.0
        info: { title: Depot, version: '1.0' }
        paths:
          /orders:
            post:
              operationId: placeOrder
              requestBody:
                content:
                  application/json:
                    schema:
                      $ref: '#/components/schemas/Order'
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        type: string
        components:
          schemas:
            Order:
              type: object
              properties:
                skus:
                  type: array
                  items:
                    {{itemSchema}}
            Part:
              type: object
              properties:
                sku:
                  type: string
                  minLength: 3
        """;

    private static ServiceSpecModel Parse(string yaml) {
        var model = OpenApiSpecParser.Parse(yaml, "depot", CancellationToken.None);

        Assert.NotNull(model);

        return model!;
    }

    private static UnmappedKeywordModel[] Unmapped(string itemSchema) =>
        Parse(Document(itemSchema)).UnmappedKeywords.ToArray();

    [Theory]
    [InlineData("{ type: string, minLength: 3 }", "minLength")]
    [InlineData("{ type: string, maxLength: 32 }", "maxLength")]
    [InlineData("{ type: string, pattern: '^[A-Z]+$' }", "pattern")]
    [InlineData("{ type: integer, minimum: 1 }", "minimum")]
    [InlineData("{ type: integer, maximum: 99 }", "maximum")]
    [InlineData("{ type: integer, multipleOf: 5 }", "multipleOf")]
    public void AConstraintOnAnInlineItemIsRecordedAsUnmapped(string itemSchema, string keyword) {
        Assert.Contains(Unmapped(itemSchema), u => u.Keyword == keyword);
    }

    /// <summary>
    /// The location names the member and marks it as the element rather than the array, because
    /// "minLength is not enforced" against a property that is a list reads as a rule about the list.
    /// </summary>
    /// <remarks>
    /// Asserted over every entry rather than a single one. A schema reachable both as a component
    /// and through an operation's body is walked once for each, so one declaration is recorded at
    /// two locations - which is how every keyword here has always behaved, and what
    /// <c>SpecDiagnostics</c> collapses into "at X and 1 other place" when it reports.
    /// </remarks>
    [Fact]
    public void TheLocationNamesTheElementNotTheArray() {
        var located = Unmapped("{ type: string, minLength: 3 }")
            .Where(u => u.Keyword == "minLength")
            .ToArray();

        Assert.NotEmpty(located);
        Assert.All(located, u => Assert.EndsWith("skus[]", u.Location));
    }

    /// <summary>
    /// Every dropped keyword on one element is reported, not just the first - a document declaring
    /// three rules has lost three.
    /// </summary>
    [Fact]
    public void EveryDroppedKeywordOnOneElementIsReported() {
        var keywords = Unmapped("{ type: string, minLength: 3, maxLength: 32, pattern: '^[A-Z]+$' }")
            .Select(u => u.Keyword)
            .ToArray();

        Assert.Contains("minLength", keywords);
        Assert.Contains("maxLength", keywords);
        Assert.Contains("pattern", keywords);
    }

    /// <summary>
    /// An item naming a schema is generated as a type and constrained through its own properties, so
    /// nothing is lost and reporting it would be a false positive on the arrangement that works.
    /// </summary>
    [Fact]
    public void AReferencedItemSchemaIsNotReported() {
        Assert.Empty(Unmapped("$ref: '#/components/schemas/Part'"));
    }

    /// <summary>
    /// An array of plain primitives declares no rule and must stay silent, or the diagnostic fires
    /// on every string list in existence.
    /// </summary>
    [Fact]
    public void AnUnconstrainedItemIsNotReported() {
        Assert.Empty(Unmapped("{ type: string }"));
    }

    /// <summary>
    /// The constraint the parser <em>does</em> read is still read. This reports what is dropped; it
    /// does not stop anything being honoured.
    /// </summary>
    [Fact]
    public void ConstraintsOnAnOrdinaryPropertyAreStillCompiled() {
        var part = Parse(Document("{ type: string }")).Schemas
            .Single(schema => schema.Name == "Part");

        var sku = part.Properties.Single(property => property.Name == "sku");

        Assert.Equal(3, sku.MinLength);
    }
}
