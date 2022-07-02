using CSharpAuthor;
using Hardened.SourceGenerator.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Web
{
    public static class WebEndPointModelGenerator
    {
        private static readonly HashSet<string> _attributeNames = GetAttributeNames();

        public static WebEndPointModel GenerateWebModel(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        {
            var methodDeclaration = (MethodDeclarationSyntax)context.Node;
            var attribute = GetWebAttribute(methodDeclaration, cancellationToken);

            if (attribute == null)
            {
                return WebEndPointModel.Empty;
            }

            var (routeInformation, parameterInfo) = WebRequestInformationProcessor.ProcessParameterInfo(context, methodDeclaration, attribute);

            return new WebEndPointModel(
                GetHandlerType(methodDeclaration),
                GetControllerType(context.Node),
                GetControllerMethod(methodDeclaration),
               routeInformation,
                parameterInfo,
                GetResponseInformation(context),
                GetFilters(context)
                );
        }

        private static ITypeDefinition GetHandlerType(MethodDeclarationSyntax contextNode)
        {
            var classDeclarationSyntax =
                contextNode.Ancestors().OfType<ClassDeclarationSyntax>().First();

            var namespaceSyntax = classDeclarationSyntax.Ancestors().OfType<NamespaceDeclarationSyntax>().First();

            return TypeDefinition.Get(namespaceSyntax.Name.ToFullString().TrimEnd() + ".Generated", classDeclarationSyntax.Identifier + "_" + contextNode.Identifier.Text);
        }

        private static string GetControllerMethod(MethodDeclarationSyntax methodDeclaration)
        {
            return methodDeclaration.Identifier.Text;
        }

        private static ITypeDefinition GetControllerType(SyntaxNode contextNode)
        {
            var classDeclarationSyntax =
                contextNode.Ancestors().OfType<ClassDeclarationSyntax>().First();

            var namespaceSyntax = classDeclarationSyntax.Ancestors().OfType<NamespaceDeclarationSyntax>().First();

            return TypeDefinition.Get(namespaceSyntax.Name.ToFullString().TrimEnd(), classDeclarationSyntax.Identifier.Text);
        }

        private static ResponseInformation GetResponseInformation(GeneratorSyntaxContext context)
        {
            var templateAttribute =
                context.Node.DescendantNodes()
                    .OfType<AttributeSyntax>()
                    .FirstOrDefault(a => a.Name.ToString() == "Template" || a.Name.ToString() == "TemplateAttribute");

            var template = "";

            if (templateAttribute is { ArgumentList.Arguments.Count: > 0 })
            {
                template = templateAttribute.ArgumentList.Arguments[0].ToString().Trim('"');
            }

            return new ResponseInformation{ TemplateName = template};
        }
        
        private static IReadOnlyCollection<FilterInformation> GetFilters(GeneratorSyntaxContext context)
        {
            return new List<FilterInformation>();
        }

        public static bool SelectWebRequestMethods(SyntaxNode arg1, CancellationToken arg2)
        {
            return arg1 is MethodDeclarationSyntax methodDeclarationSyntax &&
                   GetWebAttribute(methodDeclarationSyntax, arg2) != null;
        }

        private static AttributeSyntax? GetWebAttribute(MethodDeclarationSyntax node, CancellationToken cancellationToken)
        {
            var attributeNames =
                node.DescendantNodes().OfType<AttributeSyntax>();

            foreach (var attributeNode in attributeNames)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var name = attributeNode.Name.ToString();

                if (_attributeNames.Contains(name))
                {
                    return attributeNode;
                }
            }

            return null;
        }

        private static HashSet<string> GetAttributeNames()
        {
            var returnSet = new HashSet<string>();
            var names = new List<string> { "Get", "Put", "Post", "Patch", "Delete", "HttpMethod" };

            foreach (var name in names)
            {
                returnSet.Add(name);
                returnSet.Add(name + "Attribute");
            }

            return returnSet;
        }
    }
}
