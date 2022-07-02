using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.Web.Lambda.SourceGenerator
{
    public static class WebLambdaApplicationBootstrapGenerator
    {
        public static void Setup(IncrementalGeneratorInitializationContext initializationContext)
        {
            var provider = initializationContext.SyntaxProvider.CreateSyntaxProvider(
                ApplicationClassSelector,
                ApplicationClassTransformer
            );

            initializationContext.RegisterSourceOutput(
                provider,
                ModelWriter
                );
        }

        private static void ModelWriter(SourceProductionContext arg1, ApplicationModel arg2)
        {
            var applicationFile = ApplicationFileWriter.WriteFile(arg2.ApplicationType);

            File.WriteAllText(@"c:\temp\Application.App.cs", applicationFile);

            arg1.AddSource(arg2.ApplicationType.Name + ".App", applicationFile);
        }

        private static ApplicationModel ApplicationClassTransformer(GeneratorSyntaxContext arg1, CancellationToken arg2)
        {
            var classDefinition = (ClassDeclarationSyntax)arg1.Node;

            return new ApplicationModel { ApplicationType = classDefinition.GetTypeDefinition() };
        }

        private static bool ApplicationClassSelector(SyntaxNode arg1, CancellationToken arg2)
        {
            return arg1 is ClassDeclarationSyntax &&
                   AttributedWithLambdaWebAttribute(arg1);
        }

        private static bool AttributedWithLambdaWebAttribute(SyntaxNode syntaxNode)
        {
            foreach (var attributeSyntax in syntaxNode.DescendantNodes().OfType<AttributeSyntax>())
            {
                var attributeName = attributeSyntax.Name.ToString();

                if (attributeName == "LambdaWebApplication" ||
                    attributeName == "LambdaWebApplicationAttribute")
                {
                    return true;
                }
            }

            return false;
        }
        
        public class ApplicationModel
        {
            public ITypeDefinition ApplicationType { get; set; }
        }
    }
}
