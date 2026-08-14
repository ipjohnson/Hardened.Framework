using System.Collections.Immutable;
using System.Linq;
using Hardened.OpenApi.SourceGenerator.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web;
using Microsoft.CodeAnalysis;

namespace Hardened.OpenApi.SourceGenerator;

/// <summary>
/// Turns normalised OpenAPI models into handlers and a routing table.
/// </summary>
/// <remarks>
/// <para>
/// <b>This generator does not read yaml.</b> <c>Hardened.OpenApi.BuildTask</c> parses each spec
/// before the compiler runs and writes a normalised model into <c>obj/</c>, which arrives here as an
/// <c>AdditionalFile</c>. That is why there is no Microsoft.OpenApi, no SharpYaml, no embedded
/// dependency assemblies, no <c>AssemblyResolve</c> hook and no RS1035 suppression - an analyzer is
/// not allowed to touch the file system, and none of this needs to.
/// </para>
/// <para>
/// What stays here is what needs the semantic model: handler classes, which are matched against
/// <c>[Handler]</c> declarations, and the routing table, which is anchored on the entry point.
/// Everything else is a pure spec-to-C# transformation and belongs in the task.
/// </para>
/// </remarks>
[Generator]
public class OpenApiSourceGenerator : IIncrementalGenerator {

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        // Read the models the build task wrote, keeping both successes and errors for diagnostics
        var parseResults = context.AdditionalTextsProvider
            .Where(IsSpecModelFile)
            .Select(ReadSpecModel);

        var openApiFiles = parseResults
            .Where(r => r.Model != null)
            .Select((r, _) => r.Model)!;

        // Diagnostic: report AdditionalTexts state and any parse errors
        var diagProvider = context.AdditionalTextsProvider.Collect()
            .Combine(parseResults.Collect());
        context.RegisterSourceOutput(diagProvider, (ctx, pair) => {
            var allTexts = pair.Left;
            var results = pair.Right;
            var paths = string.Join("\n//   ", allTexts.Select(t => t.Path));
            var errors = results.Where(r => r.Error != null).ToList();
            var errorLines = errors.Count > 0
                ? "\n// Parse errors:\n" + string.Join("\n", errors.Select(e => $"//   {e.Error}"))
                : "";
            ctx.AddSource("_OpenApiDiagnostic.g.cs",
                $@"// OpenAPI Generator Diagnostic
// Total AdditionalTexts: {allTexts.Length}
// OpenAPI files parsed: {results.Count(r => r.Model != null)}
// AdditionalText paths:
//   {(allTexts.Length > 0 ? paths : "(none)")}{errorLines}
");

            // Also emit as a compiler warning so it shows in the error list
            foreach (var error in errors) {
                var descriptor = new DiagnosticDescriptor(
                    id: "HOAG002",
                    title: "OpenAPI Parse Error",
                    messageFormat: "{0}",
                    category: "Hardened.OpenApi",
                    defaultSeverity: DiagnosticSeverity.Warning,
                    isEnabledByDefault: true);
                ctx.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, error.Error));
            }
        });

        var configProvider = context.AnalyzerConfigOptionsProvider.Select((options, _) => {
            options.GlobalOptions.TryGetValue("build_property.HardenedOpenApiNamespace", out var ns);
            if (string.IsNullOrEmpty(ns)) {
                options.GlobalOptions.TryGetValue("build_property.RootNamespace", out ns);
            }

            var excludeFromCoverage = true;
            if (options.GlobalOptions.TryGetValue("build_property.ExcludeGeneratedCodeFromCoverage", out var excludeValue)) {
                excludeFromCoverage = !string.Equals(excludeValue, "false", StringComparison.OrdinalIgnoreCase);
            }

            return (Namespace: ns ?? "Generated", ExcludeFromCoverage: excludeFromCoverage);
        });

        var namespaceProvider = configProvider.Select((cfg, _) => cfg.Namespace);
        var specWithNamespace = openApiFiles.Combine(namespaceProvider);
        var specWithConfig = openApiFiles.Combine(configProvider);

        // Build RequestHandlerModels from specs for handler generation
        var handlerModels = specWithNamespace.Select((pair, ct) => {
            if (pair.Left == null) return ImmutableArray<RequestHandlerModel>.Empty;
            var ns = pair.Right;
            return RequestModelBuilder.BuildModels(pair.Left, ns + ".Models", ns + ".Services", ns + ".Generated", ns + ".Validation")
                .ToImmutableArray();
        });

        // Find [Handler] classes in user code
        var handlerInfoProvider = context.SyntaxProvider.CreateSyntaxProvider(
            HandlerSelector.Predicate,
            HandlerSelector.Transform
        ).Where(info => info != null).Collect();

        // The resolver names the task emitted, fully qualified. Collected across every spec, because
        // each spec now produces its own resolver - one flat OpenApiJsonTypeInfoResolver per project
        // is what made two spec files in one project fail to compile.
        var resolverNames = specWithNamespace
            .Select((pair, _) => pair.Left is { JsonTypeInfoResolverName.Length: > 0 } spec
                ? $"{pair.Right}.Models.{spec.JsonTypeInfoResolverName}"
                : null)
            .Where(name => name is not null)
            .Select((name, _) => name!)
            .Collect();

        // Collect all handler models across all spec files
        var allHandlerModels = handlerModels.Collect().Select((arrays, _) => {
            var builder = ImmutableArray.CreateBuilder<RequestHandlerModel>();
            foreach (var array in arrays) {
                builder.AddRange(array);
            }
            return builder.ToImmutable();
        });

        // Enrich handler models with filters from [Handler] classes
        var enrichedModels = allHandlerModels.Combine(handlerInfoProvider)
            .Select((pair, _) => {
                var models = pair.Left;
                var handlerInfos = pair.Right;
                if (handlerInfos.Length == 0) return models;
                return RequestModelBuilder.EnrichWithHandlerFilters(
                    models.ToList(), handlerInfos!).ToImmutableArray();
            });

        // Combine enriched models with config for handler generation
        var excludeFromCoverageProvider = configProvider.Select((cfg, _) => cfg.ExcludeFromCoverage);
        var enrichedModelsWithConfig = enrichedModels.Combine(excludeFromCoverageProvider);

        // Emit handler classes (one per operation) using enriched models
        context.RegisterSourceOutput(enrichedModelsWithConfig, (ctx, pair) => {
            var models = pair.Left;
            var excludeCoverage = pair.Right;
            var invokeGenerator = new WebExecutionHandlerCodeGenerator();
            foreach (var model in models) {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                try {
                    invokeGenerator.GenerateSource(ctx, model, excludeCoverage);
                } catch (Exception exp) {
                    ReportError(ctx, $"Error generating handler: {exp.Message}");
                }
            }
        });

        // Find entry points for routing table generation
        var entryPointProvider = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(false)
        ).WithComparer(new EntryPointSelector.Comparer());

        // Combine entry point with enriched handler models, handler infos, resolvers and config
        var routeProvider = entryPointProvider
            .Combine(enrichedModels)
            .Combine(handlerInfoProvider)
            .Combine(resolverNames)
            .Combine(excludeFromCoverageProvider);

        context.RegisterSourceOutput(routeProvider,
            SourceGeneratorWrapper.Wrap<((((EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) Left, ImmutableArray<HandlerInfo?> Right) Left, ImmutableArray<string> Right) Left, bool Right)>(
                (ctx, pair) => OpenApiRoutingTableGenerator.GenerateRoute(
                    ctx, pair.Left.Left.Left, pair.Left.Left.Right!, pair.Left.Right, pair.Right)));
    }

    /// <summary>
    /// Matches only what the build task writes.
    /// </summary>
    /// <remarks>
    /// This used to match <c>.yaml</c>, <c>.yml</c> and <c>.json</c>, which meant every unrelated
    /// AdditionalFile in a project - an editorconfig fragment, a settings file - was handed to the
    /// OpenAPI reader and reported as a parse failure. The suffix is ours, so nothing else claims it.
    /// </remarks>
    private static bool IsSpecModelFile(AdditionalText text) =>
        text.Path.EndsWith(SpecModelSuffix, StringComparison.OrdinalIgnoreCase);

    private const string SpecModelSuffix = ".openapi-model.txt";

    private static (OpenApiSpecModel? Model, string? Error) ReadSpecModel(AdditionalText text, CancellationToken cancellationToken) {
        var content = text.GetText(cancellationToken)?.ToString();
        if (string.IsNullOrEmpty(content)) {
            return (null, $"Empty content for {text.Path}");
        }

        try {
            return (SpecModelSerializer.Read(content!), null);
        } catch (Exception ex) {
            return (null, $"{text.Path}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ReportError(SourceProductionContext context, string message) {
        var descriptor = new DiagnosticDescriptor(
            id: "HOAG001",
            title: "OpenAPI Generator Error",
            messageFormat: "{0}",
            category: "Hardened.OpenApi",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, message));
    }
}
