using Hardened.SourceGenerator.Models;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web
{
    public static class WebIncrementalGenerator
    {
        public static void Setup(
            IncrementalGeneratorInitializationContext initializationContext,
            IncrementalValuesProvider<ApplicationSelector.Model> entryPointProvider)
        {
            var modelProvider = initializationContext.SyntaxProvider.CreateSyntaxProvider(
                WebEndPointModelGenerator.SelectWebRequestMethods,
                WebEndPointModelGenerator.GenerateWebModel
            );

            var invokeGenerator = new WebExecutionHandlerCodeGenerator();
            
            initializationContext.RegisterSourceOutput(
                modelProvider,
                invokeGenerator.GenerateSource
                );

            var collection = modelProvider.Collect();
            
            var routeProvider = entryPointProvider.Combine(collection);
            initializationContext.RegisterSourceOutput(routeProvider, RoutingTableGenerator.GenerateRoute);
        }
    }
}
