using System.Linq;
using System.Threading;
using Hardened.Idl;
using Hardened.Generation;
using Hardened.Generation.Models;
using Hardened.OpenApi.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// A keyword the description declares and the parser does not map, said out loud.
/// </summary>
/// <remarks>
/// <para>
/// <c>multipleOf</c>, <c>uniqueItems</c> and <c>not</c> were accepted and ignored, with no
/// diagnostic anywhere. That is worse than not supporting them: the served description still
/// promises the rule, so a caller reads <c>multipleOf: 5</c>, sends 3, and is told it was fine.
/// </para>
/// <para>
/// <b>The check has to run in the parser</b>, because <c>SpecDiagnostics.Find</c> is handed a
/// <c>ServiceSpecModel</c> and a dropped keyword is by definition not in one. The model carries the
/// note; the parser is what writes it.
/// </para>
/// <para>
/// <b>It is explicitly not a diff.</b> The reader hands back a typed object model with no record of
/// which keywords the document spelled, so each keyword here is a deliberate entry rather than
/// anything discovered. A keyword nobody has thought about is still silent - unchanged for that
/// keyword, and better for these.
/// </para>
/// </remarks>
public class UnmappedKeywordTests {

    private const string Dropping = """
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
                      schema: { $ref: '#/components/schemas/Product' }
        components:
          schemas:
            Product:
              type: object
              properties:
                unitPriceCents:
                  type: integer
                  multipleOf: 5
                tags:
                  type: array
                  uniqueItems: true
                  items: { type: string }
        """;

    private const string TwiceDropped = """
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
                      schema: { $ref: '#/components/schemas/Product' }
        components:
          schemas:
            Product:
              type: object
              properties:
                labels:
                  type: array
                  uniqueItems: true
                  items: { type: string }
                tags:
                  type: array
                  uniqueItems: true
                  items: { type: string }
        """;

    private const string Clean = """
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
                      schema: { $ref: '#/components/schemas/Product' }
        components:
          schemas:
            Product:
              type: object
              properties:
                sku:
                  type: string
                  minLength: 3
                  maxLength: 32
        """;

    private static ServiceSpecModel Parse(string yaml) {
        var model = OpenApiSpecParser.Parse(yaml, "depot", CancellationToken.None);

        Assert.NotNull(model);

        return model!;
    }

    [Fact]
    public void MultipleOfIsRecordedAsUnmapped() {
        Assert.Contains(Parse(Dropping).UnmappedKeywords, u => u.Keyword == "multipleOf");
    }

    [Fact]
    public void UniqueItemsIsRecordedAsUnmapped() {
        Assert.Contains(Parse(Dropping).UnmappedKeywords, u => u.Keyword == "uniqueItems");
    }

    /// <summary>
    /// <c>not</c> is the one of the three with no attribute anywhere that could enforce it, so it is
    /// the one most likely to be assumed unsupported-and-therefore-rejected. It is neither.
    /// </summary>
    [Fact]
    public void NotIsRecordedAsUnmapped() {
        var withNot = Dropping.Replace(
            "          type: integer\n          multipleOf: 5",
            "          type: integer\n          not: { const: 0 }");

        // The replacement has to have happened, or this asserts nothing.
        Assert.DoesNotContain("multipleOf", withNot);

        Assert.Contains(Parse(withNot).UnmappedKeywords, u => u.Keyword == "not");
    }

    /// <summary>
    /// The location names the member, because "multipleOf is not enforced" in a description with
    /// four hundred schemas is not an actionable sentence.
    /// </summary>
    [Fact]
    public void TheLocationNamesTheDeclaringMember() {
        var unmapped = Parse(Dropping).UnmappedKeywords.Single(u => u.Keyword == "multipleOf");

        Assert.Contains("unitPriceCents", unmapped.Location);
    }

    /// <summary>
    /// <c>uniqueItems: false</c> is the default restated, not a declaration, and warning about it
    /// would train people to ignore the warning.
    /// </summary>
    [Fact]
    public void UniqueItemsSetToFalseIsNotADeclaration() {
        var model = Parse(Dropping.Replace("uniqueItems: true", "uniqueItems: false"));

        Assert.DoesNotContain(model.UnmappedKeywords, u => u.Keyword == "uniqueItems");
    }

    /// <summary>
    /// A description using only keywords the parser honours produces no noise at all. Without this,
    /// the diagnostic could pass its other tests by firing on everything.
    /// </summary>
    [Fact]
    public void ADescriptionUsingOnlyMappedKeywordsIsSilent() {
        Assert.Empty(Parse(Clean).UnmappedKeywords);
        Assert.DoesNotContain(SpecDiagnostics.Find(Parse(Clean)), p => p.Code == "HOAT013");
    }

    [Fact]
    public void TheDiagnosticIsAWarningRatherThanAnError() {
        var problems = SpecDiagnostics.Find(Parse(Dropping)).Where(p => p.Code == "HOAT013");

        Assert.NotEmpty(problems);
        Assert.All(problems, problem => Assert.False(problem.Fatal));
    }

    /// <summary>
    /// One message per keyword rather than per site. Forty arrays with <c>uniqueItems</c> have one
    /// thing wrong with them, and forty messages would bury it.
    /// </summary>
    [Fact]
    public void OneMessagePerKeywordRegardlessOfHowManySitesDeclareIt() {
        var model = Parse(TwiceDropped);

        Assert.Equal(2, model.UnmappedKeywords.Count(u => u.Keyword == "uniqueItems"));
        Assert.Single(
            SpecDiagnostics.Find(model),
            p => p.Code == "HOAT013" && p.Message.Contains("uniqueItems"));
    }

    /// <summary>
    /// Two sites is enough to tell "one per keyword" from "one per site"; the message says how many
    /// the rest are.
    /// </summary>
    [Fact]
    public void TheMessageCountsTheSitesItDidNotName() {
        var problem = SpecDiagnostics.Find(Parse(TwiceDropped))
            .First(p => p.Code == "HOAT013" && p.Message.Contains("uniqueItems"));

        Assert.Contains("1 other place", problem.Message);
    }

    /// <summary>
    /// The message says the description and the application disagree, which is the part that costs
    /// someone a debugging session. "Not supported" alone does not say a caller has been misled.
    /// </summary>
    [Fact]
    public void TheMessageSaysThePromiseIsNotKept() {
        var problem = SpecDiagnostics.Find(Parse(Dropping))
            .First(p => p.Code == "HOAT013" && p.Message.Contains("multipleOf"));

        Assert.Contains("not enforced", problem.Message);
        Assert.Contains("accepted at runtime", problem.Message);
    }

    /// <summary>
    /// The note is build-task state, not description. Serializing it would put a diagnostic in the
    /// generator's cache key, and the generator has nothing to do with it.
    /// </summary>
    [Fact]
    public void TheNoteDoesNotSurviveIntoTheModelFile() {
        var restored = SpecModelSerializer.Read(SpecModelSerializer.Write(Parse(Dropping)));

        Assert.Empty(restored.UnmappedKeywords);
    }

    /// <summary>
    /// Two models differing only in what was dropped generate identical C#, so they must not miss a
    /// cache hit over it.
    /// </summary>
    [Fact]
    public void ADroppedKeywordDoesNotChangeModelEquality() {
        var withKeyword = Parse(Dropping);
        var withoutKeyword = Parse(Dropping.Replace("                  multipleOf: 5\n", ""));

        Assert.NotEmpty(withKeyword.UnmappedKeywords);
        Assert.Equal(withKeyword.Schemas.Count, withoutKeyword.Schemas.Count);
        Assert.Equal(withKeyword, withoutKeyword);
    }
}
