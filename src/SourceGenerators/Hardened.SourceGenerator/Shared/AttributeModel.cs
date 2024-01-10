using CSharpAuthor;
using Microsoft.CodeAnalysis;
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
                        var arguments = "";
                        var propertyAssignment = "";

                        if (attribute.ArgumentList != null) {
                            foreach (var attributeArgumentSyntax in
                                     attribute.ArgumentList.Arguments) {
                                if (attributeArgumentSyntax.ToString().Contains("=")) {
                                    if (propertyAssignment.Length > 0) {
                                        propertyAssignment += ", ";
                                    }

                                    propertyAssignment += attributeArgumentSyntax.ToString();
                                }
                                else {
                                    if (arguments.Length > 0) {
                                        arguments += ", ";
                                    }

                                    arguments += attributeArgumentSyntax.ToString();
                                }
                            }
                        }

                        var type = operation.Type.GetTypeDefinition();

                        if (!type.Name.EndsWith("Attribute")) {
                            type = TypeDefinition.Get(type.Namespace, type.Name + "Attribute");
                        }
                        
                        yield return new AttributeModel(type,
                            arguments,
                            propertyAssignment);
                    }
                }
            }
        }
    }
}