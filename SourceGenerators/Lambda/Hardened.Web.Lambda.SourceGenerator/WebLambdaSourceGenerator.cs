using Hardened.SourceGenerator.Configuration;
using Hardened.SourceGenerator.DependencyInjection;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Templates.Generator;
using Hardened.SourceGenerator.Web;
using Microsoft.CodeAnalysis;

namespace Hardened.Web.Lambda.SourceGenerator
{
    [Generator]
    public class WebLambdaSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var dependencyRegistry = new[]
            {
                KnownTypes.DI.Registry.StandardDependencies,
                KnownTypes.DI.Registry.RequestRuntimeDI,
                KnownTypes.DI.Registry.TemplateDI,
                KnownTypes.DI.Registry.WebRuntimeDI,
                KnownTypes.DI.Registry.LambdaWebDI
            };

            DependencyInjectionIncrementalGenerator.Setup(context, "LambdaWebApplication", dependencyRegistry);
            ConfigurationIncrementalGenerator.Setup(context, "LambdaWebApplication");
            TemplateIncrementalGenerator.Setup(context, "LambdaWebApplication", new [] {"html"});
            WebIncrementalGenerator.Setup(context, "LambdaWebApplication");
            WebLambdaApplicationBootstrapGenerator.Setup(context);
        }
    }
}
