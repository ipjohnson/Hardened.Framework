using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Hardened.SourceGenerator.Configuration;
using Hardened.SourceGenerator.DependencyInjection;
using Hardened.SourceGenerator.Shared;

namespace Hardened.Library.SourceGenerator;

[Generator]
public class LibrarySourceGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(false)
        ).WithComparer(new EntryPointSelector.Comparer());

        var generator = new ServiceProviderFileGenerator();

        context.RegisterSourceOutput(
            applicationModel,
            SourceGeneratorWrapper.Wrap<EntryPointSelector.Model>(generator.GenerateFile));

        ConfigurationIncrementalGenerator.Setup(context, applicationModel);

        ReportAMissingRoutingGenerator(context);
    }

    /// <summary>
    /// Says so when this project declares routes and nothing is compiling them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A module with handlers and no routing generator built without a warning into an application
    /// that answered 404 to everything - the trial arm deleted one PackageReference and lost an
    /// afternoon to it. The build cannot see a missing analyzer from inside the analyzer that is
    /// missing, so the question is asked here: this generator is referenced by every Hardened
    /// project, and the routing generators declare a marker type saying they ran.
    /// </para>
    /// <para>
    /// The route declarations are found through <c>ForAttributeWithMetadataName</c>, which is
    /// Roslyn's attribute index rather than a walk over every node - the same cost discipline
    /// DependencyModules' own generator is built around.
    /// </para>
    /// </remarks>
    private static void ReportAMissingRoutingGenerator(
        IncrementalGeneratorInitializationContext context) {
        IncrementalValueProvider<ImmutableArray<string>>? routes = null;

        foreach (var attribute in MissingRoutingGenerator.VerbAttributes) {
            var declared = context.SyntaxProvider.ForAttributeWithMetadataName(
                    attribute,
                    static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax,
                    static (target, _) =>
                        target.TargetSymbol.ContainingType?.ToDisplayString() ?? "")
                .Where(static name => name.Length > 0)
                .Collect();

            routes = routes == null
                ? declared
                : routes.Value.Combine(declared).Select(
                    static (pair, _) => pair.Left.AddRange(pair.Right));
        }

        if (routes == null) {
            return;
        }

        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(routes.Value),
            static (production, pair) => MissingRoutingGenerator.Report(
                production, pair.Left, pair.Right.Distinct().OrderBy(name => name).ToArray()));
    }
}
