using System.Collections.Immutable;
using CSharpAuthor;
using Hardened.SourceGenerator.Function;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Function;

/// <summary>
/// <see cref="FunctionIncrementalGenerator.CombinedComparer"/>, which decides whether the
/// registration stage may serve its previous output.
///
/// <para>
/// It is a second copy of the web generator's comparer of the same name, carrying the same
/// asymmetry: the hash is structural over the handlers, while equality on both halves is by
/// reference. Recorded for the web copy in <c>ModelComparisonTests</c> on 2026-08-11 and asserted
/// here as the function copy behaves, not as it reads. The end-to-end consequence is in
/// IncrementalFunctionGenerationTests — caching works because Roslyn hands back the same instances
/// when nothing changed, not because the comparer would recognise an equal rebuild.
/// </para>
/// </summary>
public class FunctionCombinedComparerTests {

    private static ITypeDefinition Type(string name) => TypeDefinition.Get("System", name);

    private static RequestHandlerModel Handler(string functionName = "Process") =>
        new(
            new RequestHandlerNameModel(functionName, "POST"),
            TypeDefinition.Get("TestApp", "TestFunctions"),
            "Process",
            TypeDefinition.Get("TestApp.Generated", "TestFunctions_Process"),
            [
                new RequestParameterInformation(
                    parameterType: TypeDefinition.Get("TestApp", "DataModel"),
                    name: "model",
                    required: true,
                    defaultValue: null,
                    bindingType: ParameterBindType.Body,
                    bindingName: "model",
                    parameterIndex: 0)
            ],
            new ResponseInformationModel { ReturnType = Type("String") },
            []);

    private static EntryPointSelector.Model EntryPoint(string name = "TestApplication") =>
        new() {
            EntryPointType = TypeDefinition.Get("TestApp", name),
            RootEntryPoint = false,
            AttributeModels = [],
            MethodDefinitions = [],
            PropertyDefinitions = null
        };

    /// <summary>
    /// The instance-reuse path Roslyn actually takes, and the two changes that must be noticed:
    /// a different entry point, and a different set of handlers.
    /// </summary>
    [Fact]
    public void TheCombinedComparerComparesBothHalvesByReference() {
        var comparer = new FunctionIncrementalGenerator.CombinedComparer();
        var entryPoint = EntryPoint();
        var handlers = ImmutableArray.Create(Handler());

        Assert.True(comparer.Equals((entryPoint, handlers), (entryPoint, handlers)));

        Assert.False(comparer.Equals((entryPoint, handlers), (EntryPoint("Other"), handlers)));

        Assert.False(comparer.Equals(
            (entryPoint, handlers),
            (entryPoint, ImmutableArray.Create(Handler(), Handler("second")))));

        // The missed cache hit: a rebuilt-but-identical entry point does not compare equal, because
        // EntryPointSelector.Model has no Equals override and the comparer does not use
        // EntryPointSelector.Comparer. This is the line that would flip to Assert.True if it did.
        Assert.False(comparer.Equals((entryPoint, handlers), (EntryPoint(), handlers)));
    }

    /// <summary>
    /// The handler half of the hash is structural: two arrays holding equal handlers agree, and a
    /// changed function name disagrees. The entry point instance is held constant because the other
    /// half of the hash is <see cref="object.GetHashCode"/>.
    /// </summary>
    [Fact]
    public void TheCombinedComparerHashesTheHandlersStructurally() {
        var comparer = new FunctionIncrementalGenerator.CombinedComparer();
        var entryPoint = EntryPoint();

        Assert.Equal(
            comparer.GetHashCode((entryPoint, ImmutableArray.Create(Handler()))),
            comparer.GetHashCode((entryPoint, ImmutableArray.Create(Handler()))));

        Assert.NotEqual(
            comparer.GetHashCode((entryPoint, ImmutableArray.Create(Handler()))),
            comparer.GetHashCode((entryPoint, ImmutableArray.Create(Handler("renamed")))));
    }
}
