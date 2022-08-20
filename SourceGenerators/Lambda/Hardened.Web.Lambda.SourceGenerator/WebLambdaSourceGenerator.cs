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

            var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
                ApplicationSelector.UsingAttribute("LambdaWebApplication"),
                ApplicationSelector.TransformModel(true)
            ).WithComparer(new ApplicationSelector.Comparer());

            DependencyInjectionIncrementalGenerator.Setup(context, applicationModel, dependencyRegistry);
            ConfigurationIncrementalGenerator.Setup(context, applicationModel);
            TemplateIncrementalGenerator.Setup(context, applicationModel, new [] {"html"});
            WebIncrementalGenerator.Setup(context, applicationModel);
            WebLambdaApplicationBootstrapGenerator.Setup(context, applicationModel);
        }
    }
}
