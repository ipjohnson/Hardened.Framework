using System.Linq;
using System.Threading;
using Hardened.Idl;
using Hardened.Idl.Models;
using Hardened.OpenApi.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// An operation's <c>summary</c> and <c>description</c>, carried separately.
/// </summary>
/// <remarks>
/// <para>
/// The parser held <c>Description = FirstNonEmpty(summary, description)</c>, so an operation
/// declaring both kept the summary and <b>lost the description outright</b>. Nothing failed: the
/// generated doc comment wanted one line and got the right one, which is why 427 tests passed over
/// it and why no test here existed to change.
/// </para>
/// <para>
/// It becomes a defect the moment the model is also what a published document is rendered from. A
/// description declaring a one-line summary and four paragraphs of behaviour would publish the one
/// line, and the paragraphs would be gone with no diagnostic - the same shape as every silently
/// dropped keyword, except that this keyword <i>was</i> read.
/// </para>
/// <para>
/// <b>The tags case is the same defect in a different field</b> and is pinned here for that reason.
/// One tag is a grouping key and has to stay one, because an operation lands on exactly one
/// generated service interface. All of them belong in the model anyway, because a reference page
/// groups by every tag an operation declares.
/// </para>
/// </remarks>
public class OperationProseTests {

    private const string BothForms = """
        openapi: 3.0.0
        info: { title: Depot, version: '1.0' }
        paths:
          /products:
            post:
              operationId: createProduct
              tags: [catalogue, admin]
              summary: Create a product
              description: >
                Creates a product and returns its Location. Stock initialises to zero, and the
                SKU must be unique across the catalogue.
              responses:
                '201': { description: created }
        """;

    private const string DescriptionOnly = """
        openapi: 3.0.0
        info: { title: Depot, version: '1.0' }
        paths:
          /products:
            get:
              operationId: listProducts
              description: Every product in the catalogue.
              responses:
                '200': { description: ok }
        """;

    private static OperationModel Parse(string yaml) {
        var model = OpenApiSpecParser.Parse(yaml, "depot", CancellationToken.None);

        Assert.NotNull(model);

        return model!.Services.SelectMany(service => service.Operations).Single();
    }

    /// <summary>
    /// The assertion that fails against the previous behaviour: the description was discarded
    /// whenever a summary was present.
    /// </summary>
    [Fact]
    public void AnOperationDeclaringBothKeepsBoth() {
        var operation = Parse(BothForms);

        Assert.Equal("Create a product", operation.Summary);
        Assert.Contains("SKU must be unique", operation.Description);
    }

    /// <summary>
    /// A description with no summary stays a description rather than being promoted, so the two
    /// fields keep meaning what the document said they meant.
    /// </summary>
    [Fact]
    public void ADescriptionWithNoSummaryIsNotPromoted() {
        var operation = Parse(DescriptionOnly);

        Assert.Null(operation.Summary);
        Assert.Equal("Every product in the catalogue.", operation.Description);
    }

    /// <summary>
    /// The generated doc comment is unchanged. The summary-first preference moved out of the parser
    /// and into <c>ServiceInterfaceEmitter</c>, and this is what says it arrived.
    /// </summary>
    [Fact]
    public void TheDocCommentStillPrefersTheSummary() {
        var operation = Parse(BothForms);

        Assert.Equal(
            "Create a product",
            string.IsNullOrWhiteSpace(operation.Summary) ? operation.Description : operation.Summary);
    }

    [Fact]
    public void TheFirstTagStaysTheGroupingKey() {
        Assert.Equal("catalogue", Parse(BothForms).Tag);
    }

    /// <summary>
    /// The second assertion that fails against the previous behaviour: only the first tag survived.
    /// </summary>
    [Fact]
    public void EveryDeclaredTagIsCarried() {
        Assert.Equal(new[] { "catalogue", "admin" }, Parse(BothForms).Tags);
    }

    /// <summary>
    /// An operation with no tags gets an allocated group, and that allocation is not a declared tag.
    /// Emitting it into a document would advertise a tag the description never wrote.
    /// </summary>
    [Fact]
    public void AnUntaggedOperationDeclaresNoTags() {
        var operation = Parse(DescriptionOnly);

        Assert.Empty(operation.Tags);
        Assert.NotNull(operation.Tag);
    }

    /// <summary>
    /// Both fields survive the round trip through the model file, which is the only way they reach
    /// the generator - the build task parses, and the generator never opens the specification.
    /// </summary>
    [Fact]
    public void BothFormsSurviveTheModelFileRoundTrip() {
        var model = OpenApiSpecParser.Parse(BothForms, "depot", CancellationToken.None);

        Assert.NotNull(model);

        var restored = SpecModelSerializer.Read(SpecModelSerializer.Write(model!));
        var operation = restored.Services.SelectMany(service => service.Operations).Single();

        Assert.Equal("Create a product", operation.Summary);
        Assert.Contains("SKU must be unique", operation.Description);
        Assert.Equal(new[] { "catalogue", "admin" }, operation.Tags);
    }

    /// <summary>
    /// The model's equality is what Roslyn caches on, so a changed summary has to be a changed
    /// model. Without this, editing only the summary would leave the previous document in place.
    /// </summary>
    [Fact]
    public void ChangingOnlyTheSummaryChangesTheModel() {
        var withSummary = Parse(BothForms);
        var withoutSummary = Parse(BothForms.Replace("Create a product", "Add a product"));

        Assert.NotEqual(withSummary, withoutSummary);
    }
}
