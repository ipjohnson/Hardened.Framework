using System.Text;
using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Shared;

public static class SyntaxNodeExtensions {

    /// <summary>
    /// The value of an attribute's first argument as a string, or empty when there is none.
    ///
    /// This deliberately does not use ToFullString(). That returns the argument's source
    /// text, so a name written as [FromHeader("X-Tenant")] comes back including its quotes
    /// and gets quoted a second time when emitted, producing ""X-Tenant"" - which does not
    /// compile. The constant value is what callers actually want.
    /// </summary>
    public static string GetFirstStringArgumentValue(
        this AttributeSyntax attribute, GeneratorSyntaxContext generatorSyntaxContext) {
        var expression = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression;

        if (expression == null) {
            return "";
        }

        // Handles literals and references to string constants alike.
        var constant = generatorSyntaxContext.SemanticModel.GetConstantValue(expression);

        if (constant is { HasValue: true, Value: string value }) {
            return value;
        }

        // Fall back to the source text with any surrounding quotes removed, so an argument
        // the semantic model cannot fold still yields something usable rather than nothing.
        return expression.ToFullString().Trim().Trim('"');
    }

    public static string GetNamespace(this BaseTypeDeclarationSyntax syntax) {
        var parentSyntaxNode = syntax.Parent;

        while (parentSyntaxNode != null &&
               parentSyntaxNode is not NamespaceDeclarationSyntax &&
               parentSyntaxNode is not FileScopedNamespaceDeclarationSyntax) {
            parentSyntaxNode = parentSyntaxNode.Parent;
        }

        if (parentSyntaxNode is BaseNamespaceDeclarationSyntax namespaceNode) {
            return WalkNamespaceNodes(namespaceNode);
        }

        return "";
    }

    private static string WalkNamespaceNodes(BaseNamespaceDeclarationSyntax? namespaceNode) {
        var stringBuilder = new StringBuilder();

        while (namespaceNode != null) {
            if (stringBuilder.Length > 0) {
                stringBuilder.Insert(0, '.');
            }

            stringBuilder.Insert(0, namespaceNode.Name);

            namespaceNode = namespaceNode.Parent as BaseNamespaceDeclarationSyntax;
        }

        return stringBuilder.ToString();
    }

    public static ITypeDefinition GetTypeDefinition(this ClassDeclarationSyntax classDeclarationSyntax) {
        var namespaceSyntax = classDeclarationSyntax.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().First();

        return TypeDefinition.Get(namespaceSyntax.Name.ToFullString().TrimEnd(),
            classDeclarationSyntax.Identifier.Text);
    }

    public static AttributeSyntax? GetAttribute(this SyntaxNode node, string attributeName, string ns = "") {
        return node.DescendantNodes()
            .OfType<AttributeSyntax>().FirstOrDefault(
                a => {
                    var name = a.Name.ToString();

                    return name.Equals(attributeName) || name.Equals(attributeName + "Attribute") ||
                           name.Equals(ns + "." + attributeName) || name.Equals(ns + "." + attributeName + "Attribute");
                });
    }

    public static IEnumerable<AttributeSyntax>
        GetAttributes(this SyntaxNode node, string attributeName, string ns = "") {
        return node.DescendantNodes()
            .OfType<AttributeSyntax>().Where(
                a => {
                    var name = a.Name.ToString();

                    return name.Equals(attributeName) || name.Equals(attributeName + "Attribute") ||
                           name.Equals(ns + "." + attributeName) || name.Equals(ns + "." + attributeName + "Attribute");
                });
    }

    public static bool IsAttributed(this SyntaxNode node, string attributeName, string ns = "") {
        return node.DescendantNodes()
            .OfType<AttributeSyntax>().Any(
                a => {
                    var name = a.Name.ToString();

                    return name.Equals(attributeName) || name.Equals(attributeName + "Attribute") ||
                           name.Equals(ns + "." + attributeName) || name.Equals(ns + "." + attributeName + "Attribute");
                });
    }

    public static bool IsAttributed(this SyntaxNode node, ITypeDefinition typeDefinition) {
        var ns = typeDefinition.Namespace;
        var attributeName = typeDefinition.Name.Replace("Attribute", "");

        return IsAttributed(node, attributeName, ns);
    }
}