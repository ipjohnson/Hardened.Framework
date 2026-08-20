using System.Collections.Generic;
using CSharpAuthor;
using Hardened.Idl.Models;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.Idl.SourceGenerator;

internal static class HandlerSelector {
    private const string HandlerAttributeName = "Handler";

    public static bool Predicate(SyntaxNode node, CancellationToken cancellationToken) {
        return node is ClassDeclarationSyntax && node.IsAttributed(HandlerAttributeName);
    }

    public static HandlerInfo? Transform(GeneratorSyntaxContext context, CancellationToken cancellationToken) {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;

        // Get class type from syntax (no semantic model needed)
        var implementationType = classDeclaration.GetTypeDefinition();

        // Every entry in the base list, not the first one.
        //
        // This used to take BaseList.Types.FirstOrDefault() and call it "the interface the class
        // implements". C# requires a base class to come first, so
        //
        //     class CatalogHandler : HandlerBase, ICatalogService
        //
        // registered HandlerBase and left ICatalogService unimplemented by anything - a clean build
        // with no diagnostic and every route on that service dead at request time. Which entry is
        // the service interface cannot be decided here, because the interface is generated and the
        // semantic model in this pass may not have it yet. So all of them are carried and the
        // routing table, which knows what the description declared, picks.
        var candidates = new List<ITypeDefinition>();

        if (classDeclaration.BaseList != null) {
            foreach (var baseType in classDeclaration.BaseList.Types) {
                var resolved = baseType.Type.GetTypeDefinition(context);

                if (resolved == null) {
                    // If semantic model can't resolve it (generated type), extract from syntax
                    var baseTypeName = baseType.Type.ToString();
                    var classNs = classDeclaration.GetNamespace();
                    resolved = TypeDefinition.Get(classNs, baseTypeName);
                }

                candidates.Add(resolved);
            }
        }

        if (candidates.Count == 0) {
            return null;
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

            var outputType = OutputAttributeSelector.Read(context, methodDeclaration);

            if (methodAttrs.Count > 0 || outputType != null) {
                methodFilters.Add(new HandlerMethodFilterInfo(
                    methodDeclaration.Identifier.Text,
                    methodAttrs,
                    outputType));
            }
        }

        return new HandlerInfo(implementationType, candidates, classFilters, methodFilters,
            LocationOf(classDeclaration));
    }

    /// <summary>
    /// The class name's own span, so a diagnostic underlines the handler rather than its whole body.
    /// </summary>
    private static Location LocationOf(ClassDeclarationSyntax classDeclaration) =>
        classDeclaration.Identifier.GetLocation();
}
