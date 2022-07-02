using System.Text;
using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Shared
{
    public static class SyntaxNodeExtensions
    {
        public static string GetNamespace(this BaseTypeDeclarationSyntax syntax)
        {
            var parentSyntaxNode = syntax.Parent;

            while (parentSyntaxNode != null &&
                   parentSyntaxNode is not NamespaceDeclarationSyntax &&
                   parentSyntaxNode is not FileScopedNamespaceDeclarationSyntax)
            {
                parentSyntaxNode = parentSyntaxNode.Parent;
            }

            if (parentSyntaxNode is BaseNamespaceDeclarationSyntax namespaceNode)
            {
                return WalkNamespaceNodes(namespaceNode);
            }

            return "";
        }

        private static string WalkNamespaceNodes(BaseNamespaceDeclarationSyntax? namespaceNode)
        {
            var stringBuilder = new StringBuilder();

            while (namespaceNode != null)
            {
                if (stringBuilder.Length > 0)
                {
                    stringBuilder.Insert(0, '.');
                }

                stringBuilder.Insert(0, namespaceNode.Name);

                namespaceNode = namespaceNode.Parent as BaseNamespaceDeclarationSyntax;
            }

            return stringBuilder.ToString();
        }

        public static ITypeDefinition GetTypeDefinition(this ClassDeclarationSyntax classDeclarationSyntax)
        {
            var namespaceSyntax = classDeclarationSyntax.Ancestors().OfType<NamespaceDeclarationSyntax>().First();

            return TypeDefinition.Get(namespaceSyntax.Name.ToFullString().TrimEnd(), classDeclarationSyntax.Identifier.Text);
        }

        public static bool IsAttributed(this SyntaxNode node, string attributeName)
        {
            return node.DescendantNodes()
                .OfType<AttributeSyntax>().Any(
                    a => a.Name.ToString().Equals(attributeName) ||
                                                    a.Name.ToString().Equals(attributeName + "Attribute"));
        }
    }
}
