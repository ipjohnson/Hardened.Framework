using System.Collections.Immutable;
using Hardened.SourceGenerator.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web
{
    public static class WebIncrementalGenerator
    {
        public static void Setup(
            IncrementalGeneratorInitializationContext initializationContext,
            IncrementalValuesProvider<EntryPointSelector.Model> entryPointProvider)
        {
            var requestModelGenerator = new WebRequestHandlerModelGenerator();

            var modelProvider = initializationContext.SyntaxProvider.CreateSyntaxProvider(
                requestModelGenerator.SelectWebRequestMethods,
                requestModelGenerator.GenerateRequestModel
            ).WithComparer(new RequestHandlerModelComparer());

            var invokeGenerator = new WebExecutionHandlerCodeGenerator();
            
            initializationContext.RegisterSourceOutput(
                modelProvider,
                invokeGenerator.GenerateSource
                );

            var collection = modelProvider.Collect();
            
            var routeProvider = entryPointProvider.Combine(collection).WithComparer(new CombinedComparer());
            initializationContext.RegisterSourceOutput(routeProvider, RoutingTableGenerator.GenerateRoute);
        }

        public class CombinedComparer : IEqualityComparer<(EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right)>
        {
            public bool Equals((EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) x, (EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) y)
            {
                return x.Item1.Equals(y.Item1) && ((Object)x.Item2).Equals(y.Item2);
            }

            public int GetHashCode((EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) obj)
            {
                unchecked
                {
                    return (obj.Item1.GetHashCode() * 397) ^ obj.Item2.GetHashCodeAggregation();
                }
            }
        }

        private static void GenerateInvokeEntryPoint(SourceProductionContext sourceProductionContext, RequestHandlerModel handlerModel)
        {
            
        }
    }
}
