using System.Collections.Immutable;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Templates;
using Hardened.SourceGenerator.Validation;
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

        var invokeGenerator = new WebExecutionHandlerCodeGenerator();

        initializationContext.RegisterSourceOutput(
            modelProvider,
            SourceGeneratorWrapper.Wrap<RequestHandlerModel>(invokeGenerator.GenerateSource)
        );

        // One abstract base per [Enable<T>] marker, off the entry point alone - it depends on no
        // handler, and pairing it with the handler collection would rebuild every template base
        // whenever any route changed.
        initializationContext.RegisterSourceOutput(
            entryPointProvider,
            SourceGeneratorWrapper.Wrap<EntryPointSelector.Model>(TemplateBaseGenerator.Generate));

        var collection = modelProvider.Collect();

        var routeProvider = entryPointProvider.Combine(collection).WithComparer(new CombinedComparer());
        initializationContext.RegisterSourceOutput(routeProvider,
            SourceGeneratorWrapper.Wrap<
                (EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right)>(RoutingTableGenerator
                .GenerateRoute));
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