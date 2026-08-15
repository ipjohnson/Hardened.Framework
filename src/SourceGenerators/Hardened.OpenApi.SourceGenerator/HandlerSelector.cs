using CSharpAuthor;
using Hardened.OpenApi.SourceGenerator.Models;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.OpenApi.SourceGenerator;

internal static class HandlerSelector {
    private const string HandlerAttributeName = "Handler";

    public static bool Predicate(SyntaxNode node, CancellationToken cancellationToken) {
        return node is ClassDeclarationSyntax && node.IsAttributed(HandlerAttributeName);
    }

    public static HandlerInfo? Transform(GeneratorSyntaxContext context, CancellationToken cancellationToken) {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;

        // Get class type from syntax (no semantic model needed)
        var implementationType = classDeclaration.GetTypeDefinition();

        // Get the first base type from syntax — this is the interface the class implements.
        // We use syntax rather than semantic model because the interface may be source-generated
        // and not yet available in the semantic model.
        var baseType = classDeclaration.BaseList?.Types.FirstOrDefault();
        if (baseType == null) return null;

        var interfaceType = baseType.Type.GetTypeDefinition(context);
        if (interfaceType == null) {
            // If semantic model can't resolve it (generated type), extract from syntax
            var baseTypeName = baseType.Type.ToString();
            var classNs = classDeclaration.GetNamespace();
            interfaceType = TypeDefinition.Get(classNs, baseTypeName);
        }

        // Collect class-level attributes (excluding Handler)
        var classFilters = AttributeModelHelper.GetAttributes(
            context,
            classDeclaration.AttributeLists,
            cancellationToken,
            attr => {
                var name = attr.Name.ToString();
                return !name.Equals(HandlerAttributeName) &&
                       !name.Equals(HandlerAttributeName + "Attribute");
            }).ToList();

        // Collect method-level attributes
        var methodFilters = new List<HandlerMethodFilterInfo>();
        foreach (var member in classDeclaration.Members) {
            if (member is not MethodDeclarationSyntax methodDeclaration) continue;

            var methodAttrs = AttributeModelHelper.GetAttributes(
                context,
                methodDeclaration.AttributeLists,
                cancellationToken).ToList();

            var templateType = TemplateAttributeSelector.Read(context, methodDeclaration);

            if (methodAttrs.Count > 0 || templateType != null) {
                methodFilters.Add(new HandlerMethodFilterInfo(
                    methodDeclaration.Identifier.Text,
                    methodAttrs,
                    templateType));
            }
        }

        return new HandlerInfo(implementationType, interfaceType, classFilters, methodFilters);
    }
}
