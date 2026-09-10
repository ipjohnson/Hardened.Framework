using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Shared;

/// <summary>
/// The filters a <c>[HardenedModule]</c> class declares, as the transform reads them.
/// </summary>
/// <remarks>
/// <para>
/// Two members of one model, and they have to agree: <c>FilterDeclarations</c> is what the routing
/// table constructs and <c>FilterFacts</c> is what the document publishes. A declaration reaching
/// one and not the other is the disagreement the rung exists to close, so both are asserted from
/// the same capture.
/// </para>
/// <para>
/// Selected by interface rather than by the denylist the handler rungs use. An entry point's
/// attribute list is mostly markers - <c>[HardenedModule]</c> itself, a runtime, every
/// <c>[Enable&lt;T&gt;]</c> - and a denylist there would construct all of them into the filter
/// chain.
/// </para>
/// </remarks>
public class EntryPointFilterRungTests {

    private const string ConditionalGet = "[Hardened.Web.Runtime.Conditional.ConditionalGet]";

    private static EntryPointSelector.Model Model(string attributes) =>
        EntryPointCapture.Single(EntryPointCapture.Application(attributes: attributes));

    private static DeclaredOperationFacts Facts(EntryPointSelector.Model model) =>
        Assert.IsType<DeclaredOperationFacts>(model.FilterFacts);

    [Fact]
    public void AFilterAttributeReachesTheModelReadyToConstruct() {
        var model = Model(ConditionalGet);

        var declaration = Assert.Single(model.FilterDeclarations);

        Assert.Equal("ConditionalGetAttribute", declaration.TypeDefinition.Name);
        Assert.Equal("Hardened.Web.Runtime.Conditional", declaration.TypeDefinition.Namespace);
    }

    /// <summary>
    /// And carries what it answers, unnarrowed. Which operations it reaches depends on their verb
    /// and response shape, which this pass does not know.
    /// </summary>
    [Fact]
    public void TheSameDeclarationCarriesWhatItAnswers() {
        var facts = Facts(Model(ConditionalGet));

        Assert.False(facts.IsEmpty);
        Assert.Contains(facts.Refusals, refusal => refusal.Response.Status == 304);
        Assert.Contains(facts.ResponseHeaders, header => header.Name == "ETag");
        Assert.Contains(facts.RequestHeaders, header => header.Name == "If-None-Match");
    }

    /// <summary>
    /// Narrowed per operation by the scope the declaration stated for itself, which is how the same
    /// facts publish a 304 on a read and nothing on a write.
    /// </summary>
    [Theory]
    [InlineData("GET", false, true)]
    [InlineData("HEAD", false, true)]
    [InlineData("POST", false, false)]
    [InlineData("GET", true, false)]
    public void WhatItAnswersIsNarrowedByVerbAndResponseShape(
        string httpMethod, bool streams, bool reaches) {
        var reaching = Facts(Model(ConditionalGet)).For(httpMethod, streams);

        Assert.Equal(reaches, reaching.Refusals.Any(refusal => refusal.Status == 304));
        Assert.Equal(reaches, reaching.HeaderParameters().Count > 0);
    }

    /// <summary>
    /// An entry point declaring no filter carries none, which is almost every application and is
    /// what keeps the routing table it generates unchanged.
    /// </summary>
    [Fact]
    public void AnEntryPointDeclaringNoFilterCarriesNone() {
        var model = Model("");

        Assert.Empty(model.FilterDeclarations);
        Assert.Null(model.FilterFacts);
    }

    /// <summary>
    /// The markers around it are not filters. <c>[HardenedModule]</c> is on every entry point and
    /// <c>[Enable&lt;T&gt;]</c> names a module rather than providing a filter itself, so a denylist
    /// would have constructed both.
    /// </summary>
    [Fact]
    public void AMarkerOnTheEntryPointIsNotAFilter() {
        var model = Model(
            "[Enable<Hardened.Web.Runtime.OpenApi.OpenApiDocumentPublishing>]\n" + ConditionalGet);

        var declaration = Assert.Single(model.FilterDeclarations);

        Assert.Equal("ConditionalGetAttribute", declaration.TypeDefinition.Name);
    }

    /// <summary>
    /// Two declarations both reach the model, in the order they were written - which is the order
    /// they are merged into a handler's metadata and therefore the order they break ties in.
    /// </summary>
    [Fact]
    public void EveryFilterDeclaredOnTheEntryPointReachesTheModelInOrder() {
        var model = Model(
            "[Hardened.Requests.Runtime.Filters.Retry]\n" + ConditionalGet);

        Assert.Equal(
            ["RetryAttribute", "ConditionalGetAttribute"],
            model.FilterDeclarations.Select(declaration => declaration.TypeDefinition.Name));
    }

    /// <summary>
    /// The arguments come with it. A declaration covering an application is written once, so this
    /// is the only place its arguments are spelled.
    /// </summary>
    [Fact]
    public void TheArgumentsADeclarationWasWrittenWithComeWithIt() {
        var model = Model("[Hardened.Requests.Runtime.Filters.Retry(Attempts = 5)]");

        var declaration = Assert.Single(model.FilterDeclarations);

        Assert.Equal("Attempts = 5", declaration.PropertyAssignment);
    }

    /// <summary>
    /// Every filter attribute this framework ships is declarable there, which is what the reference
    /// table and the feature guides say. Selected by the interface, so an application's own is too.
    /// </summary>
    [Theory]
    [InlineData("[Hardened.Web.Runtime.Conditional.ConditionalGet]", "ConditionalGetAttribute")]
    [InlineData("[Hardened.Web.Runtime.Compression.Compress]", "CompressAttribute")]
    [InlineData("[Hardened.Requests.Runtime.Filters.Retry]", "RetryAttribute")]
    [InlineData("[Hardened.Requests.Runtime.RateLimiting.RateLimit]", "RateLimitAttribute")]
    [InlineData(
        "[Hardened.Requests.Runtime.Caching.CacheResponse<Hardened.Web.Runtime.Caching.VaryByRoute>(Duration = 60)]",
        "CacheResponseAttribute")]
    public void EveryShippedFilterAttributeIsDeclarableOnTheEntryPoint(
        string attribute, string expected) {
        var declaration = Assert.Single(Model(attribute).FilterDeclarations);

        Assert.Equal(expected, declaration.TypeDefinition.Name);
    }

    /// <summary>
    /// And one that declares a status publishes it from there. <c>[RateLimit]</c> on a module caps
    /// every handler compiled with it, so every one of them can answer the 429.
    /// </summary>
    [Fact]
    public void AFilterThatDeclaresAStatusPublishesItFromTheEntryPoint() {
        var facts = Facts(Model("[Hardened.Requests.Runtime.RateLimiting.RateLimit]"));

        Assert.Contains(facts.For("GET", streams: false).Refusals, refusal => refusal.Status == 429);
    }

    /// <summary>
    /// Two models with the same declarations compare equal, so an edit elsewhere in the entry point
    /// does not invalidate every routing table in the compilation.
    /// </summary>
    [Fact]
    public void TwoEntryPointsDeclaringTheSameFilterCompareEqual() {
        var comparer = new EntryPointSelector.Comparer();

        Assert.True(comparer.Equals(Model(ConditionalGet), Model(ConditionalGet)));
        Assert.False(comparer.Equals(Model(ConditionalGet), Model("")));
    }
}
