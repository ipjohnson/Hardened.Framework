using System.Collections.Immutable;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Templates;
using Hardened.SourceGenerator.Validation;
using Hardened.SourceGenerator.Web.Authorization;
using Hardened.SourceGenerator.Web.Routing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

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

        // Through the shared bridge, the same one the described front-ends use. This changes no
        // emitted byte - the round trip is asserted lossless by SpecRoundTripTests - and exists so
        // the bridge is load-bearing for attribute-routed applications before the analysis behind it
        // moves onto the spec model. Every existing test then exercises it, rather than only the
        // corpus that suite covers.
        //
        // Per handler rather than per application, so editing one does not rebuild all of them.
        modelProvider = modelProvider.Select((model, _) =>
            CodeFirstSpecProjection.RoundTrip(
                model,
                model.ControllerType.Namespace,
                model.ControllerType.Namespace,
                model.InvokeHandlerType.Namespace,
                model.ControllerType.Namespace));

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

        // Authorization diagnostics run on their own provider, carrying a location, and feed nothing
        // that emits source.
        //
        // The alternative - a location on RequestHandlerModel - was measured: a comment above twenty
        // handlers took recomputed outputs from 0 of 23 to 21 of 23, and a genuine route change from
        // 2 to 14, because a span is an offset and every offset below an edit shifts. That model
        // builds a class per handler, the routing table and the OpenAPI document, so it has to stay
        // insensitive to where things sit in a file. This one does not.
        var authorizationRequired = entryPointProvider
            .Collect()
            .Select((entryPoints, _) =>
                entryPoints.Any(RequireAuthorizationDiagnostics.IsRequired));

        var handlerAuthorization = initializationContext.SyntaxProvider.CreateSyntaxProvider(
            requestModelGenerator.SelectWebRequestMethods,
            HandlerAuthorizationSelector.Transform);

        // Per handler rather than over the collected set, so an edit invalidates only the handlers
        // it moved.
        initializationContext.RegisterSourceOutput(
            handlerAuthorization.Combine(authorizationRequired),
            SourceGeneratorWrapper.Wrap<(HandlerAuthorizationModel Left, bool Right)>(
                (context, pair) =>
                    RequireAuthorizationDiagnostics.Report(context, pair.Left, pair.Right)));

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

        // Every MSBuild property this generator reads, selected once into a value.
        //
        // Selected to a record of strings rather than kept as the options object, so the pipeline
        // caches on the values: AnalyzerConfigOptionsProvider hands back a new instance every run,
        // and combining it directly would rebuild the routing table on every keystroke.
        //
        // One value rather than one provider each, because each provider costs a .Combine and the
        // tuple SourceGeneratorWrapper.Wrap<> has to name grows a level with it. At two properties
        // that type was already three deep and near-unreadable; the next one would have made it
        // four.
        var options = initializationContext.AnalyzerConfigOptionsProvider.Select(
            (provider, _) => new WebGeneratorOptions(
                Value(provider, "HardenedAmbiguousRoutes"),
                Value(provider, OpenApiVersionFacts.PropertyName)));

        var routeProvider = entryPointProvider.Combine(collection).WithComparer(new CombinedComparer());

        initializationContext.RegisterSourceOutput(
            routeProvider.Combine(options).Combine(constraints),
            SourceGeneratorWrapper.Wrap<
                (((EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) Left,
                    WebGeneratorOptions Right) Left, ImmutableArray<RouteConstraintModel> Right)>((context, pair) =>
                RoutingTableGenerator.GenerateRoute(
                    context, pair.Left.Left, pair.Left.Right, pair.Right)));
    }

    private static string? Value(AnalyzerConfigOptionsProvider provider, string property) =>
        provider.GlobalOptions.TryGetValue("build_property." + property, out var value) ? value : null;

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