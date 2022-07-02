using Hardened.SourceGenerator.Models;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web
{
    public static class WebIncrementalGenerator
    {
        public static void Setup(IncrementalGeneratorInitializationContext initializationContext, string libraryAttribute)
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

            var applicationModel = initializationContext.SyntaxProvider.CreateSyntaxProvider(
                ApplicationSelector.UsingAttribute(libraryAttribute),
                ApplicationSelector.TransformModel
            );

            var routeProvider = applicationModel.Combine(collection);
            initializationContext.RegisterSourceOutput(routeProvider, RoutingTableGenerator.GenerateRoute);
        }
    }
}
