using System.Collections.Immutable;
using Hardened.OpenApi.SourceGenerator.Emitters;
using Hardened.OpenApi.SourceGenerator.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web;
using Microsoft.CodeAnalysis;

namespace Hardened.OpenApi.SourceGenerator;

[Generator]
public class OpenApiSourceGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var openApiFiles = context.AdditionalTextsProvider
            .Where(IsOpenApiFile)
            .Select(ParseOpenApiFile)
            .Where(model => model != null)!;

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

        // Emit records, enums, and interfaces
        context.RegisterSourceOutput(specWithConfig,
            SourceGeneratorWrapper.Wrap<(OpenApiSpecModel? Left, (string Namespace, bool ExcludeFromCoverage) Right)>(EmitTypes));

        // Build RequestHandlerModels from specs for handler generation
        var handlerModels = specWithNamespace.Select((pair, ct) => {
            if (pair.Left == null) return ImmutableArray<RequestHandlerModel>.Empty;
            var ns = pair.Right;
            return RequestModelBuilder.BuildModels(pair.Left, ns + ".Models", ns + ".Services", ns + ".Generated")
                .ToImmutableArray();
        });

        // Find [Handler] classes in user code
        var handlerInfoProvider = context.SyntaxProvider.CreateSyntaxProvider(
            HandlerSelector.Predicate,
            HandlerSelector.Transform
        ).Where(info => info != null).Collect();

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

        // Combine entry point with enriched handler models, handler infos, and config for routing table
        var routeProvider = entryPointProvider.Combine(enrichedModels).Combine(handlerInfoProvider).Combine(excludeFromCoverageProvider);

        context.RegisterSourceOutput(routeProvider,
            SourceGeneratorWrapper.Wrap<(((EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) Left, ImmutableArray<HandlerInfo?> Right) Left, bool Right)>(
                (ctx, pair) => OpenApiRoutingTableGenerator.GenerateRoute(ctx, pair.Left.Left, pair.Left.Right!, pair.Right)));
    }

    private static bool IsOpenApiFile(AdditionalText text) {
        var path = text.Path;
        return path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static OpenApiSpecModel? ParseOpenApiFile(AdditionalText text, CancellationToken cancellationToken) {
        var content = text.GetText(cancellationToken)?.ToString();
        if (string.IsNullOrEmpty(content)) return null;

        var fileName = System.IO.Path.GetFileNameWithoutExtension(text.Path);

        try {
            return OpenApiSpecParser.Parse(content!, fileName, cancellationToken);
        } catch {
            return null;
        }
    }

    private static void EmitTypes(SourceProductionContext context,
        (OpenApiSpecModel? Left, (string Namespace, bool ExcludeFromCoverage) Right) pair) {
        var spec = pair.Left;
        var ns = pair.Right.Namespace;
        var excludeFromCoverage = pair.Right.ExcludeFromCoverage;

        if (spec == null) return;

        foreach (var schema in spec.Schemas) {
            context.CancellationToken.ThrowIfCancellationRequested();

            switch (schema.Kind) {
                case SchemaKind.Object:
                    var recordSource = RecordEmitter.Emit(schema, ns, excludeFromCoverage);
                    context.AddSource($"{spec.FileName}.{schema.Name}.g.cs", recordSource);
                    break;

                case SchemaKind.Enum:
                    var enumSource = EnumEmitter.Emit(schema, ns);
                    context.AddSource($"{spec.FileName}.{schema.Name}.g.cs", enumSource);
                    break;
            }
        }

        foreach (var service in spec.Services) {
            context.CancellationToken.ThrowIfCancellationRequested();

            var interfaceSource = ServiceInterfaceEmitter.Emit(service, ns);
            var interfaceName = NamingHelper.ToInterfaceName(service.Tag);
            context.AddSource($"{spec.FileName}.{interfaceName}.g.cs", interfaceSource);
        }

        // Emit JsonTypeInfoResolver for AOT serialization
        var resolverSource = JsonTypeInfoEmitter.Emit(spec.Schemas, ns, excludeFromCoverage);
        context.AddSource($"{spec.FileName}.OpenApiJsonTypeInfoResolver.g.cs", resolverSource);

        // Emit validation filter providers for operations with validation constraints
        foreach (var service in spec.Services) {
            foreach (var operation in service.Operations) {
                context.CancellationToken.ThrowIfCancellationRequested();

                var validationSource = ValidationFilterEmitter.Emit(
                    operation, ns + ".Generated", ns + ".Models", excludeFromCoverage);
                if (validationSource != null) {
                    var filterName = NamingHelper.ToPascalCase(operation.OperationId);
                    context.AddSource(
                        $"{spec.FileName}.{filterName}_ValidationFilterProvider.g.cs",
                        validationSource);
                }
            }
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
