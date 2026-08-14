using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Shared;

public record AttributeModel(
    ITypeDefinition TypeDefinition,
    string Arguments,
    string PropertyAssignment);

public static class AttributeModelHelper {
    public static IEnumerable<AttributeModel> GetAttributes(
        GeneratorSyntaxContext context,
        SyntaxList<AttributeListSyntax> attributeListSyntax,
        CancellationToken cancellationToken,
        Func<AttributeSyntax, bool>? filter = null) {
        foreach (var attributeList in attributeListSyntax) {
            foreach (var attribute in attributeList.Attributes) {
                cancellationToken.ThrowIfCancellationRequested();

                var operation = context.SemanticModel.GetTypeInfo(attribute);

                if (filter?.Invoke(attribute) ?? true) {
                    if (operation.Type != null) {
                        yield return InternalAttributeModel(context.SemanticModel, attribute, operation);
                    }
                }
            }
        }
    }

    public static AttributeModel? GetAttribute(GeneratorSyntaxContext context, AttributeSyntax attribute) {
        var operation = context.SemanticModel.GetTypeInfo(attribute);
        
        return operation.Type != null ?
            InternalAttributeModel(context.SemanticModel, attribute, operation) :
            null;
    }

    private static AttributeModel InternalAttributeModel(
        SemanticModel semanticModel, AttributeSyntax attribute, TypeInfo operation) {
        var arguments = "";
        var propertyAssignment = "";

        if (attribute.ArgumentList != null) {
            var rewriter = new QualifyNamesRewriter(semanticModel);

            foreach (var attributeArgumentSyntax in
                     attribute.ArgumentList.Arguments) {
                // NameEquals is "Property = value", NameColon is "parameter: value". Distinguishing
                // them syntactically rather than by looking for an "=" in the text is what keeps a
                // positional argument that merely contains one - a string "a=b", a comparison, a
                // lambda - from being emitted as a property initializer the attribute does not have.
                var value = rewriter.Rewrite(attributeArgumentSyntax.Expression);

                if (attributeArgumentSyntax.NameEquals != null) {
                    if (propertyAssignment.Length > 0) {
                        propertyAssignment += ", ";
                    }

                    propertyAssignment += attributeArgumentSyntax.NameEquals.Name + " = " + value;
                }
                else {
                    if (arguments.Length > 0) {
                        arguments += ", ";
                    }

                    arguments += attributeArgumentSyntax.NameColon != null
                        ? attributeArgumentSyntax.NameColon.Name + ": " + value
                        : value;
                }
            }
        }

        if (operation.Type == null) {
            throw new ArgumentNullException("operation.Type", "The type argument cannot be null.");
        }
        
        var type = operation.Type.GetTypeDefinition();

        if (!type.Name.EndsWith("Attribute")) {
            type = TypeDefinition.Get(type.Namespace, type.Name + "Attribute");
        }
                        
        return new AttributeModel(type,
            arguments,
            propertyAssignment);
    }

    /// <summary>
    /// Rewrites every name in an attribute argument to its <c>global::</c>-qualified form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attribute arguments are copied from the consumer's source into a generated file, and that
    /// file is written with <see cref="TypeOutputMode.Global"/> and carries none of the consumer's
    /// <c>using</c> directives. So the natural spelling of an enum argument -
    /// <c>[CacheControl(Type = CacheControlEnum.NoStore)]</c> under
    /// <c>using Hardened.Web.Runtime.CacheControl;</c> - emitted an unqualified name and failed
    /// with CS0103. The workaround was to write the enum fully qualified at every call site.
    /// </para>
    /// <para>
    /// Resolving through the semantic model rather than folding to a constant, because an enum's
    /// constant value is its underlying integer: <c>MaxAge | Public</c> would emit <c>33</c>, which
    /// needs a cast to assign back to the property and reads as nothing at all in generated output.
    /// </para>
    /// </remarks>
    private class QualifyNamesRewriter : CSharpSyntaxRewriter {
        private readonly SemanticModel _semanticModel;

        public QualifyNamesRewriter(SemanticModel semanticModel) {
            _semanticModel = semanticModel;
        }

        /// <summary>
        /// The argument as it should appear in generated source. Falls back to the original text if
        /// the visit produces nothing, so an expression this does not understand is copied through
        /// rather than dropped.
        /// </summary>
        public string Rewrite(ExpressionSyntax expression) =>
            Visit(expression)?.ToString() ?? expression.ToString();

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node) =>
            Qualify(node) ?? base.VisitMemberAccessExpression(node);

        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node) =>
            Qualify(node) ?? base.VisitQualifiedName(node);

        public override SyntaxNode? VisitGenericName(GenericNameSyntax node) =>
            IsRightHandOfAName(node) ? node : Qualify(node) ?? base.VisitGenericName(node);

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) =>
            IsRightHandOfAName(node) ? node : Qualify(node) ?? base.VisitIdentifierName(node);

        /// <summary>
        /// <c>nameof</c> evaluates to the source spelling of its argument, so qualifying inside it
        /// changes nothing and only makes the generated output harder to read.
        /// </summary>
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node) {
            if (node.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == "nameof") {
                return node;
            }

            return base.VisitInvocationExpression(node);
        }

        /// <summary>
        /// Whether this name is the trailing half of a larger one - the <c>B</c> of <c>A.B</c>.
        /// </summary>
        /// <remarks>
        /// It binds to the same symbol as the whole, so qualifying it in place would produce
        /// <c>A.global::A.B</c>. The enclosing node is what gets rewritten.
        /// </remarks>
        private static bool IsRightHandOfAName(SimpleNameSyntax node) =>
            (node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node) ||
            (node.Parent is QualifiedNameSyntax qualifiedName && qualifiedName.Right == node);

        private SyntaxNode? Qualify(ExpressionSyntax node) {
            var symbol = _semanticModel.GetSymbolInfo(node).Symbol;

            string? qualified = null;

            switch (symbol) {
                case ITypeSymbol type:
                    qualified = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    break;
                // Covers enum members and consts, both of which are static fields.
                case IFieldSymbol { IsStatic: true } field:
                    qualified = FullyQualifiedMember(field.ContainingType, field.Name);
                    break;
                case IPropertySymbol { IsStatic: true } property:
                    qualified = FullyQualifiedMember(property.ContainingType, property.Name);
                    break;
            }

            return qualified == null
                ? null
                : SyntaxFactory.ParseExpression(qualified).WithTriviaFrom(node);
        }

        private static string? FullyQualifiedMember(INamedTypeSymbol? containingType, string name) =>
            containingType == null
                ? null
                : containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + name;
    }
}