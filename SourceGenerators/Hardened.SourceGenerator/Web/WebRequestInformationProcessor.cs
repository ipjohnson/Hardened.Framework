using Hardened.SourceGenerator.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Web
{
    public class WebRequestInformationProcessor
    {
        
        public static (WebRouteInformation routeInformation, IReadOnlyList<RequestParameterInformation> parameterInformation) 
            ProcessParameterInfo(GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration, AttributeSyntax attribute)
        {
            var method = attribute.Name.ToString().ToUpperInvariant();

            var pathTemplate = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression.ToString().Trim('"') ??
                               GetDefaultPath(methodDeclaration);

            return (new WebRouteInformation(pathTemplate, method, Array.Empty<PathToken>()), Array.Empty<RequestParameterInformation>());
        }

        private static string GetDefaultPath(MethodDeclarationSyntax methodDeclaration)
        {
            return "/";
        }
    }
}
