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

    /// <summary>
    /// The type this class declares.
    ///
    /// <para>
    /// A class in the global namespace has no <c>BaseNamespaceDeclarationSyntax</c> ancestor, and
    /// this used to take <c>.First()</c> of that empty sequence. Because it runs inside an
    /// incremental generator's transform it escaped <c>SourceGeneratorWrapper</c> entirely, so
    /// Roslyn reported <c>CS8785</c> at warning severity and the generator contributed nothing to
    /// the whole assembly. Recorded against the web generator in TESTING-PLAN.md section 12 as
    /// "a controller in the global namespace crashes the generator"; the same call is reached from
    /// the template and console generators. Fixed 2026-08-12 — the global namespace is the empty
    /// string, which is what <see cref="TypeDefinition"/> already means by it.
    /// </para>
    /// </summary>
    public static ITypeDefinition GetTypeDefinition(this ClassDeclarationSyntax classDeclarationSyntax) {
        var namespaceSyntax = classDeclarationSyntax.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault();

        return TypeDefinition.Get(
            namespaceSyntax?.Name.ToFullString().TrimEnd() ?? "",
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

    /// <summary>
    /// Whether <paramref name="node"/> carries the named attribute, however the author qualified it.
    ///
    /// <para>
    /// This is a syntactic pre-filter for an incremental generator's predicate, so it sees the text
    /// as written and no symbols. It used to compare only against the bare name, the bare name plus
    /// <c>Attribute</c>, and those two prefixed with <paramref name="ns"/> — which meant a fully
    /// qualified <c>[global::Hardened.Shared.Runtime.Attributes.HardenedModule]</c> matched
    /// nothing, and the class carrying it was simply not seen. Legal C#, and the form to reach for
    /// when avoiding a using or emitting from another generator. Found 2026-08-12.
    /// </para>
    ///
    /// <para>
    /// Comparing the last dotted segment covers every qualification of the name. It can in
    /// principle match an unrelated attribute that ends the same way, which the bare-name check
    /// above could already do; over-matching here costs a transform that resolves the symbol
    /// properly and discards it, while under-matching loses the type silently.
    /// </para>
    /// </summary>
    public static bool IsAttributed(this SyntaxNode node, string attributeName, string ns = "") {
        return node.DescendantNodes()
            .OfType<AttributeSyntax>().Any(
                a => {
                    var name = a.Name.ToString();

                    if (name.Equals(attributeName) || name.Equals(attributeName + "Attribute") ||
                        name.Equals(ns + "." + attributeName) || name.Equals(ns + "." + attributeName + "Attribute")) {
                        return true;
                    }

                    var lastDot = name.LastIndexOf('.');

                    if (lastDot < 0) {
                        return false;
                    }

                    var simpleName = name.Substring(lastDot + 1);

                    return simpleName.Equals(attributeName) || simpleName.Equals(attributeName + "Attribute");
                });
    }

    public static bool IsAttributed(this SyntaxNode node, ITypeDefinition typeDefinition) {
        var ns = typeDefinition.Namespace;
        var attributeName = typeDefinition.Name.Replace("Attribute", "");

        return IsAttributed(node, attributeName, ns);
    }
}