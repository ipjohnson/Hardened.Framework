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
            TemplateIncrementalGenerator.Setup(context, "LambdaWebApplication", new [] {"html"});
            WebIncrementalGenerator.Setup(context, "LambdaWebApplication");
            WebLambdaApplicationBootstrapGenerator.Setup(context);
        }
    }
}
