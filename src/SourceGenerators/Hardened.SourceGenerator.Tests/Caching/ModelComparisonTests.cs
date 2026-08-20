using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web;
using System.Collections.Immutable;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Caching;

/// <summary>
/// The comparisons incremental caching is built on, member by member.
///
/// <para>
/// ModelEqualityCachingTests establishes that a handler model rebuilt from identical inputs compares
/// equal. This covers the parts underneath it — the parameter model, the collection helpers the
/// model equality delegates to, and the two comparers wired directly into the generator pipeline —
/// where a member left out of Equals means a real edit is cached away and never regenerated.
/// </para>
/// </summary>
public class ModelComparisonTests {

    private static ITypeDefinition Type(string name) => TypeDefinition.Get("System", name);

    private static RequestParameterInformation Parameter(
        string typeName = "String",
        string name = "id",
        bool required = true,
        string? defaultValue = null,
        ParameterBindType bindingType = ParameterBindType.Path,
        string bindingName = "id",
        int parameterIndex = 0,
        AttributeModel? customAttribute = null) =>
        new(Type(typeName), name, required, defaultValue, bindingType, bindingName, parameterIndex,
            customAttribute);

    [Fact]
    public void IdenticallyBuiltParametersAreEqual() {
        Assert.True(Parameter().Equals(Parameter()));
        Assert.Equal(Parameter().GetHashCode(), Parameter().GetHashCode());
    }

    /// <summary>
    /// One test per member that changes generated code. A member missing from Equals is invisible
    /// in every other test — the model still compares equal, so the generator serves the previous
    /// output for a source file that no longer matches it.
    /// </summary>
    [Fact]
    public void AParameterDifferingInItsTypeIsNotEqual() {
        Assert.False(Parameter().Equals(Parameter(typeName: "Int32")));
    }

    [Fact]
    public void AParameterDifferingInItsNameIsNotEqual() {
        Assert.False(Parameter().Equals(Parameter(name: "orderId")));
    }

    [Fact]
    public void AParameterDifferingInWhetherItIsRequiredIsNotEqual() {
        Assert.False(Parameter().Equals(Parameter(required: false)));
    }

    [Fact]
    public void AParameterDifferingInItsDefaultValueIsNotEqual() {
        Assert.False(Parameter().Equals(Parameter(defaultValue: "\"none\"")));
        Assert.False(Parameter(defaultValue: "\"none\"").Equals(Parameter()));
    }

    /// <summary>
    /// The binding source decides which collection the generated binder reads from, so a parameter
    /// that moves from the path to the query string must not compare equal.
    /// </summary>
    [Fact]
    public void AParameterDifferingInItsBindingSourceIsNotEqual() {
        Assert.False(Parameter().Equals(Parameter(bindingType: ParameterBindType.QueryString)));
    }

    [Fact]
    public void AParameterDifferingInItsBindingNameIsNotEqual() {
        Assert.False(Parameter().Equals(Parameter(bindingName: "orderId")));
    }

    /// <summary>
    /// A custom binding attribute is constructed verbatim in the generated binder, so a change to
    /// its arguments changes the emitted code.
    /// </summary>
    [Fact]
    public void AParameterDifferingInItsCustomAttributeArgumentsIsNotEqual() {
        var claim = new AttributeModel(TypeDefinition.Get("TestApp", "FromClaimAttribute"), "\"sub\"", "");
        var other = claim with { Arguments = "\"tenant\"" };

        Assert.False(
            Parameter(bindingType: ParameterBindType.CustomAttribute, customAttribute: claim)
                .Equals(Parameter(bindingType: ParameterBindType.CustomAttribute, customAttribute: other)));
    }

    [Fact]
    public void AParameterIsNotEqualToAnUnrelatedObject() {
        Assert.False(Parameter().Equals("not a parameter"));
    }

    [Fact]
    public void AParameterDescribesItselfByTypeAndName() {
        Assert.Equal("System.String id", Parameter().ToString());
    }

    /// <summary>
    /// Differing parameters must produce differing hash codes as well as comparing unequal —
    /// Roslyn's caching tables hash before they compare.
    /// </summary>
    [Fact]
    public void ParametersDifferingInAnyComparedMemberHashDifferently() {
        var baseline = Parameter().GetHashCode();

        Assert.NotEqual(baseline, Parameter(typeName: "Int32").GetHashCode());
        Assert.NotEqual(baseline, Parameter(name: "orderId").GetHashCode());
        Assert.NotEqual(baseline, Parameter(required: false).GetHashCode());
        Assert.NotEqual(baseline, Parameter(defaultValue: "\"none\"").GetHashCode());
        Assert.NotEqual(baseline, Parameter(bindingType: ParameterBindType.Header).GetHashCode());
        Assert.NotEqual(baseline, Parameter(bindingName: "orderId").GetHashCode());
    }

    /// <summary>
    /// A handler model whose parameter list differs only in one parameter. The list is compared
    /// element by element, which is what makes adding, removing or retyping one parameter visible.
    /// </summary>
    [Fact]
    public void HandlerModelsDifferingInOneParameterAreNotEqual() {
        Assert.False(Handler(Parameter()).Equals(Handler(Parameter(name: "orderId"))));
        Assert.False(Handler(Parameter()).Equals(Handler(Parameter(), Parameter(name: "page"))));
    }

    [Fact]
    public void HandlerModelsDifferingInOneFilterAreNotEqual() {
        var audit = new AttributeModel(TypeDefinition.Get("TestApp", "AuditAttribute"), "\"orders\"", "");

        Assert.False(Handler(filters: [audit]).Equals(Handler(filters: [audit with { Arguments = "\"x\"" }])));
        Assert.False(Handler(filters: [audit]).Equals(Handler()));
    }

    [Fact]
    public void HandlerModelsDifferingInTheirInvokeTypeAreNotEqual() {
        var first = Handler();
        var second = new RequestHandlerModel(
            first.Name,
            first.ControllerType,
            first.HandlerMethod,
            TypeDefinition.Get("TestApp.Generated", "Different"),
            first.RequestParameterInformationList,
            first.ResponseInformation,
            first.Filters);

        Assert.False(first.Equals(second));
    }

    [Fact]
    public void AHandlerModelDescribesItselfByRouteAndTarget() {
        Assert.Equal("GET:/orders/{id}:TestApp.OrderController.GetOrder", Handler().ToString());
    }

    [Fact]
    public void ARequestHandlerNameDescribesItselfByMethodAndPath() {
        Assert.Equal("GET:/orders/{id}", new RequestHandlerNameModel("/orders/{id}", "GET").ToString());
    }

    [Fact]
    public void ARequestHandlerNameIsNotEqualToAnUnrelatedObject() {
        Assert.False(new RequestHandlerNameModel("/a", "GET").Equals("GET:/a"));
    }

    /// <summary>
    /// The response model's async flags select which of six constructors the handler is built with,
    /// so each has to take part in equality.
    /// </summary>
    [Fact]
    public void ResponseModelsDifferingInTheirAsyncShapeAreNotEqual() {
        var baseline = new ResponseInformationModel { ReturnType = Type("String") };

        Assert.NotEqual(baseline, baseline with { IsAsync = true });
        Assert.NotEqual(baseline, baseline with { IsAsyncEnumerable = true });
        Assert.NotEqual(baseline, baseline with { AsyncEnumerableItemType = Type("String") });
        Assert.NotEqual(baseline, baseline with { OutputType = Type("Fortunes") });
        Assert.NotEqual(baseline, baseline with { RawResponseContentType = "text/csv" });
        Assert.NotEqual(baseline, baseline with { DefaultStatusCode = 201 });
    }

    /// <summary>
    /// Every response annotation appears, not whichever one was added most recently.
    /// </summary>
    /// <remarks>
    /// This string is what a caching failure gets read through, and it has silently dropped one of
    /// them twice - once each way - as a side effect of removing and re-adding the template
    /// annotation. A model that reports the same text for two different responses is the thing that
    /// makes such a failure unreadable. Streaming framing is the third, and adding it here is what
    /// this test is for.
    /// </remarks>
    [Fact]
    public void AResponseModelDescribesAllOfItsResponseAnnotations() {
        var model = new ResponseInformationModel {
            IsAsync = true,
            OutputType = Type("Fortunes"),
            RawResponseContentType = "text/csv",
            StreamFraming = "sse",
            ReturnType = Type("String"),
            DefaultStatusCode = 201,
            NullResponseBodyExpression = "Models.DefaultErrorBodies.NotFoundProblem",
            ProducedContentTypes = "text/plain,text/csv"
        };

        Assert.Equal(
            "True:System.Fortunes:text/csv:sse:System.String:201:" +
            "Models.DefaultErrorBodies.NotFoundProblem:text/plain,text/csv",
            model.ToString());
    }

    /// <summary>
    /// Two operations differing only in the status they declare are two different responses.
    /// </summary>
    /// <remarks>
    /// The failure this prevents: an operation edited from <c>'200'</c> to <c>'201'</c> keeping its
    /// cached handler and continuing to answer 200 - which is the defect the status wiring exists to
    /// close, arriving by the back door.
    /// </remarks>
    [Fact]
    public void TwoResponsesDifferingOnlyInDeclaredStatusAreDifferent() {
        var baseline = new ResponseInformationModel { IsAsync = true, ReturnType = Type("String") };

        Assert.NotEqual(baseline.ToString(), (baseline with { DefaultStatusCode = 201 }).ToString());
    }

    /// <summary>
    /// And two differing only in the body a null return writes.
    /// </summary>
    /// <summary>
    /// And two differing only in what they produce, which is what negotiation reads.
    /// </summary>
    [Fact]
    public void TwoResponsesDifferingOnlyInProducedContentTypesAreDifferent() {
        var baseline = new ResponseInformationModel { IsAsync = true, ReturnType = Type("String") };

        Assert.NotEqual(
            baseline.ToString(),
            (baseline with { ProducedContentTypes = "text/plain" }).ToString());
    }

    [Fact]
    public void TwoResponsesDifferingOnlyInNullBodyAreDifferent() {
        var baseline = new ResponseInformationModel { IsAsync = true, ReturnType = Type("String") };

        Assert.NotEqual(
            baseline.ToString(),
            (baseline with { NullResponseBodyExpression = "Models.DefaultErrorBodies.NotFoundProblem" })
            .ToString());
    }

    /// <summary>
    /// Two streams differing only in their framing are two different responses.
    /// </summary>
    /// <remarks>
    /// The failure this prevents is specific: the same handler, edited from newline-delimited JSON
    /// to server-sent events, keeping its cached output and continuing to answer as NDJSON. The
    /// framing is the whole of the difference between them, so it has to be the whole of the
    /// difference here too.
    /// </remarks>
    [Fact]
    public void TwoStreamsDifferingOnlyInFramingDescribeThemselvesDifferently() {
        var baseline = new ResponseInformationModel { ReturnType = Type("String") };

        Assert.NotEqual(
            (baseline with { StreamFraming = "sse" }).ToString(),
            (baseline with { StreamFraming = null }).ToString());
    }

    /// <summary>
    /// And two responses differing only in the annotation the other one carries do not describe
    /// themselves identically.
    /// </summary>
    [Fact]
    public void TwoResponsesDifferingOnlyInWhichAnnotationTheyCarryDescribeThemselvesDifferently() {
        var baseline = new ResponseInformationModel { ReturnType = Type("String") };

        Assert.NotEqual(
            (baseline with { OutputType = Type("Fortunes") }).ToString(),
            (baseline with { RawResponseContentType = "Fortunes" }).ToString());
    }

    /// <summary>
    /// DeepEquals is what model equality delegates to for every list member. Its three exits —
    /// length mismatch, element mismatch and a null element — are each a way for a real change to
    /// be reported as no change.
    /// </summary>
    [Fact]
    public void DeepEqualsComparesElementwise() {
        Assert.True(new[] { "a", "b" }.DeepEquals(new[] { "a", "b" }));
        Assert.False(new[] { "a", "b" }.DeepEquals(new[] { "a" }));
        Assert.False(new[] { "a", "b" }.DeepEquals(new[] { "a", "c" }));
        Assert.True(Array.Empty<string>().DeepEquals(Array.Empty<string>()));
    }

    [Fact]
    public void DeepEqualsTreatsANullElementAsEqualOnlyToAnotherNull() {
        Assert.True(new string?[] { null }.DeepEquals(new string?[] { null }));
        Assert.False(new string?[] { null }.DeepEquals(new string?[] { "a" }));
        Assert.False(new string?[] { "a" }.DeepEquals(new string?[] { null }));
    }

    [Fact]
    public void HashCodeAggregationDependsOnOrderAndContent() {
        Assert.Equal(
            new[] { "a", "b" }.GetHashCodeAggregation(),
            new[] { "a", "b" }.GetHashCodeAggregation());

        Assert.NotEqual(
            new[] { "a", "b" }.GetHashCodeAggregation(),
            new[] { "b", "a" }.GetHashCodeAggregation());

        Assert.NotEqual(
            new[] { "a", "b" }.GetHashCodeAggregation(),
            new[] { "a" }.GetHashCodeAggregation());
    }

    /// <summary>
    /// The entry point comparer is wired to the provider that feeds the routing table, so an
    /// application class that changed has to compare unequal or the table is never rebuilt.
    /// </summary>
    [Fact]
    public void EntryPointModelsComparedByTheirTypeMethodsAndAttributes() {
        var comparer = new EntryPointSelector.Comparer();

        Assert.True(comparer.Equals(EntryPoint(), EntryPoint()));
        Assert.False(comparer.Equals(EntryPoint(), EntryPoint(name: "Other")));
        Assert.False(comparer.Equals(EntryPoint(), EntryPoint(rootEntryPoint: true)));
        Assert.False(comparer.Equals(EntryPoint(), EntryPoint(methodName: "Other")));
    }

    [Fact]
    public void EntryPointModelsDifferingInTheirAttributesAreNotEqual() {
        var comparer = new EntryPointSelector.Comparer();
        var basePath = new AttributeModel(TypeDefinition.Get("TestApp", "BasePathAttribute"), "\"/v1\"", "");

        Assert.False(comparer.Equals(EntryPoint(), EntryPoint(attributes: [basePath])));
        Assert.False(comparer.Equals(
            EntryPoint(attributes: [basePath]),
            EntryPoint(attributes: [basePath with { Arguments = "\"/v2\"" }])));
    }

    [Fact]
    public void EntryPointModelsDifferingInTheirPropertiesAreNotEqual() {
        var comparer = new EntryPointSelector.Comparer();
        var property = new HardenedPropertyDefinition("Name", Type("String"));

        Assert.False(comparer.Equals(EntryPoint(), EntryPoint(properties: [property])));
        Assert.False(comparer.Equals(EntryPoint(properties: [property]), EntryPoint(properties: null)));
        Assert.True(comparer.Equals(EntryPoint(properties: null), EntryPoint(properties: null)));
    }

    [Fact]
    public void TheSameEntryPointModelInstanceIsEqualToItself() {
        var comparer = new EntryPointSelector.Comparer();
        var model = EntryPoint();

        Assert.True(comparer.Equals(model, model));
        Assert.Equal(comparer.GetHashCode(model), comparer.GetHashCode(model));
    }

    /// <summary>
    /// The routing table's provider combines the entry point with every handler, and compares
    /// <em>both</em> halves by reference rather than by value.
    ///
    /// <para>
    /// The handler array goes through <c>ImmutableArray.Equals(object)</c>, which is reference
    /// equality over the underlying array. The entry point goes through
    /// <c>x.Item1.Equals(y.Item1)</c>, and <see cref="EntryPointSelector.Model"/> declares no
    /// <c>Equals</c> override — so that binds to <see cref="object.Equals(object)"/>, which is also
    /// reference equality.
    /// </para>
    ///
    /// <para>
    /// This works because Roslyn hands back the same instances when nothing changed, which is
    /// asserted end to end by IncrementalGenerationTests.AnUnrelatedEditReusesTheRoutingTable. It
    /// is weaker than it was meant to be, though: <see cref="EntryPointSelector.Comparer"/> sits in
    /// the same file, compares the model structurally, and the combined comparer never uses it. A
    /// rebuilt-but-identical entry point therefore misses the cache and regenerates. Recorded
    /// 2026-08-11 — safe, because regenerating produces the same output, but it means the
    /// incremental-caching guarantee rests on instance reuse rather than on the comparer. Asserted
    /// here as it behaves, not as it was intended; see TESTING-PLAN.md.
    /// </para>
    /// </summary>
    [Fact]
    public void TheCombinedRoutingComparerComparesBothHalvesByReference() {
        var comparer = new WebIncrementalGenerator.CombinedComparer();
        var entryPoint = EntryPoint();
        var handlers = ImmutableArray.Create(Handler());

        // The instance-reuse path Roslyn actually takes.
        Assert.True(comparer.Equals((entryPoint, handlers), (entryPoint, handlers)));
        Assert.Equal(
            comparer.GetHashCode((entryPoint, handlers)),
            comparer.GetHashCode((entryPoint, handlers)));

        // A change on either side is noticed.
        Assert.False(comparer.Equals(
            (entryPoint, handlers), (EntryPoint(name: "Other"), handlers)));
        Assert.False(comparer.Equals(
            (entryPoint, handlers),
            (entryPoint, ImmutableArray.Create(Handler(), Handler(path: "/orders")))));

        // And so is a rebuilt-but-identical entry point: this is the missed cache hit, and the
        // line that would flip to Assert.True if CombinedComparer used EntryPointSelector.Comparer.
        Assert.False(comparer.Equals((entryPoint, handlers), (EntryPoint(), handlers)));
    }

    /// <summary>
    /// The handler half of the hash is structural: two arrays holding equal handlers agree, and a
    /// changed path disagrees. Equality then decides, and it is that pairing — a structural hash
    /// with a referential equality — that keeps the routing table correct while leaving it able to
    /// regenerate more often than it strictly must.
    ///
    /// <para>
    /// The entry point instance is held constant because the other half of the hash is
    /// <see cref="object.GetHashCode"/> — see
    /// <see cref="TheCombinedRoutingComparerComparesBothHalvesByReference"/>.
    /// </para>
    /// </summary>
    [Fact]
    public void TheCombinedRoutingComparerHashesTheHandlersStructurally() {
        var comparer = new WebIncrementalGenerator.CombinedComparer();
        var entryPoint = EntryPoint();

        Assert.Equal(
            comparer.GetHashCode((entryPoint, ImmutableArray.Create(Handler()))),
            comparer.GetHashCode((entryPoint, ImmutableArray.Create(Handler()))));

        Assert.NotEqual(
            comparer.GetHashCode((entryPoint, ImmutableArray.Create(Handler()))),
            comparer.GetHashCode((entryPoint, ImmutableArray.Create(Handler(path: "/other")))));
    }

    /// <summary>
    /// Method definitions carry the entry point's shape into the comparison. Name, return type and
    /// parameters each take part.
    /// </summary>
    [Fact]
    public void MethodDefinitionsCompareByNameReturnTypeAndParameters() {
        var parameter = new HardenedParameterDefinition("id", Type("String"));
        var method = new HardenedMethodDefinition("Configure", Type("Void"), [parameter]);

        Assert.True(method.Equals(new HardenedMethodDefinition("Configure", Type("Void"), [parameter])));
        Assert.False(method.Equals(new HardenedMethodDefinition("Other", Type("Void"), [parameter])));
        Assert.False(method.Equals(new HardenedMethodDefinition("Configure", Type("String"), [parameter])));
        Assert.False(method.Equals(new HardenedMethodDefinition("Configure", Type("Void"), [])));
        Assert.False(method.Equals(new HardenedMethodDefinition("Configure", Type("Void"),
            [new HardenedParameterDefinition("other", Type("String"))])));
        Assert.False(method.Equals("not a method"));
    }

    [Fact]
    public void ParameterDefinitionsCompareByNameAndType() {
        var parameter = new HardenedParameterDefinition("id", Type("String"));

        Assert.True(parameter.Equals(new HardenedParameterDefinition("id", Type("String"))));
        Assert.False(parameter.Equals(new HardenedParameterDefinition("other", Type("String"))));
        Assert.False(parameter.Equals(new HardenedParameterDefinition("id", Type("Int32"))));
        Assert.False(parameter.Equals("not a parameter"));
        Assert.Equal("System.String id", parameter.ToString());
    }

    private static RequestHandlerModel Handler(
        params RequestParameterInformation[] parameters) =>
        Handler(path: "/orders/{id}", filters: null, parameters: parameters);

    private static RequestHandlerModel Handler(
        string path = "/orders/{id}",
        IReadOnlyList<AttributeModel>? filters = null,
        params RequestParameterInformation[] parameters) =>
        new(
            new RequestHandlerNameModel(path, "GET"),
            TypeDefinition.Get("TestApp", "OrderController"),
            "GetOrder",
            TypeDefinition.Get("TestApp.Generated", "OrderController_GetOrder"),
            parameters.Length > 0 ? parameters : [Parameter()],
            new ResponseInformationModel { ReturnType = Type("String") },
            filters ?? []);

    private static EntryPointSelector.Model EntryPoint(
        string name = "Application",
        bool rootEntryPoint = false,
        string methodName = "Configure",
        IReadOnlyList<AttributeModel>? attributes = null,
        IReadOnlyList<HardenedPropertyDefinition>? properties = null) =>
        new() {
            EntryPointType = TypeDefinition.Get("TestApp", name),
            RootEntryPoint = rootEntryPoint,
            AttributeModels = attributes ?? [],
            MethodDefinitions = [new HardenedMethodDefinition(methodName, Type("Void"), [])],
            PropertyDefinitions = properties
        };
}
