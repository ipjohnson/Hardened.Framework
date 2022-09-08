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
            var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
                EntryPointSelector.UsingAttribute(),
                EntryPointSelector.TransformModel(true)
            ).WithComparer(new EntryPointSelector.Comparer());
            
            WebLambdaApplicationBootstrapGenerator.Setup(context, applicationModel);
        }
    }
}
