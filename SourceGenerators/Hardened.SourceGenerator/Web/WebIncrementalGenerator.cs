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
            IncrementalValuesProvider<ApplicationSelector.Model> entryPointProvider)
        {
            var requestModelGenerator = new WebRequestHandlerModelGenerator();


            var modelProvider = initializationContext.SyntaxProvider.CreateSyntaxProvider(
                requestModelGenerator.SelectWebRequestMethods,
                requestModelGenerator.GenerateRequestModel
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

        private static void GenerateInvokeEntryPoint(SourceProductionContext sourceProductionContext, RequestHandlerModel handlerModel)
        {
            
        }
    }
}
