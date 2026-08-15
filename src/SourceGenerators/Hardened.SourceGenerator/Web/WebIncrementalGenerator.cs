using System.Collections.Immutable;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Templates;
using Hardened.SourceGenerator.Validation;
using Hardened.SourceGenerator.Web.Routing;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web;

public static class WebIncrementalGenerator {
    public static void Setup(
        IncrementalGeneratorInitializationContext initializationContext,
        IncrementalValuesProvider<EntryPointSelector.Model> entryPointProvider) {
        var requestModelGenerator = new WebRequestHandlerModelGenerator();

        // Validation runs the front half of this pipeline: it builds the handler model, emits the
        // validator for its Parameters class when the types the handler binds carry constraints,
        // and hands back the model with the filter that runs it attached. Everything below is
        // unchanged and does not know whether that happened.
        var modelProvider = HandlerValidationGenerator.Setup(
            initializationContext,
            requestModelGenerator,
            requestModelGenerator.SelectWebRequestMethods);

        // Every [RouteConstraint] the application declares, flattened and ordered so the value is
        // stable between runs - an unordered collection would rebuild everything downstream on any
        // edit that reshuffled the syntax provider.
        var constraints = initializationContext.SyntaxProvider.CreateSyntaxProvider(
                RouteConstraintSelector.Predicate,
                RouteConstraintSelector.Transform)
            .SelectMany((declared, _) => declared)
            .Collect()
            .Select((declared, _) =>
                declared.OrderBy(constraint => constraint.Name, StringComparer.Ordinal)
                    .ThenBy(constraint => constraint.Call, StringComparer.Ordinal)
                    .ToImmutableArray());

        // Once per compilation, so a wrong signature is reported once however many routes or
        // modules the assembly has.
        initializationContext.RegisterSourceOutput(
            constraints,
            SourceGeneratorWrapper.Wrap<ImmutableArray<RouteConstraintModel>>(
                (context, declared) =>
                    RouteConstraintSelector.ReportInvalidSignatures(context, declared)));

        var invokeGenerator = new WebExecutionHandlerCodeGenerator();

        // The handler stage reports a route token nothing declares, so it has to know what the
        // application declared. Combining here rebuilds every handler when a constraint is added,
        // which is the right trade for a diagnostic that would otherwise be wrong.
        initializationContext.RegisterSourceOutput(
            modelProvider.Combine(constraints),
            SourceGeneratorWrapper.Wrap<(RequestHandlerModel Left, ImmutableArray<RouteConstraintModel> Right)>(
                (context, pair) => invokeGenerator.GenerateSource(context, pair.Left, pair.Right))
        );

        // One abstract template base per [Enable<T>] marker, off the entry point alone - it
        // depends on no handler, and pairing it with the handler collection would rebuild every
        // template base whenever any route changed.
        initializationContext.RegisterSourceOutput(
            entryPointProvider,
            SourceGeneratorWrapper.Wrap<EntryPointSelector.Model>(TemplateBaseGenerator.Generate));

        var collection = modelProvider.Collect();

        // <HardenedAmbiguousRoutes> layers on top of the per-file .editorconfig mechanism, setting
        // the default severity for HRDR001. Selected to a string so the pipeline caches on the
        // value rather than on the options object, which is a new instance every run.
        var ambiguousRoutes = initializationContext.AnalyzerConfigOptionsProvider.Select(
            (options, _) =>
                options.GlobalOptions.TryGetValue("build_property.HardenedAmbiguousRoutes", out var value)
                    ? value
                    : null);

        var routeProvider = entryPointProvider.Combine(collection).WithComparer(new CombinedComparer());

        initializationContext.RegisterSourceOutput(
            routeProvider.Combine(ambiguousRoutes).Combine(constraints),
            SourceGeneratorWrapper.Wrap<
                (((EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) Left,
                    string? Right) Left, ImmutableArray<RouteConstraintModel> Right)>((context, pair) =>
                RoutingTableGenerator.GenerateRoute(
                    context, pair.Left.Left, pair.Left.Right, pair.Right)));
    }

    public class CombinedComparer : IEqualityComparer<(EntryPointSelector.Model Left,
        ImmutableArray<RequestHandlerModel> Right)> {
        public bool Equals((EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) x,
            (EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) y) {
            return x.Item1.Equals(y.Item1) && ((Object)x.Item2).Equals(y.Item2);
        }

        public int GetHashCode((EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) obj) {
            unchecked {
                return (obj.Item1.GetHashCode() * 397) ^ obj.Item2.GetHashCodeAggregation();
            }
        }
    }
}