using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Shared;

public static class MethodDefinitionSyntaxExtensions
{
    public static HardenedMethodDefinition GetMethodDefinition(this MethodDeclarationSyntax methodDeclarationSyntax,
        GeneratorSyntaxContext generatorSyntaxContext)
    {
        return new HardenedMethodDefinition(
            methodDeclarationSyntax.Identifier.ValueText,
            GetReturnType(methodDeclarationSyntax, generatorSyntaxContext),
            GenerateSyntaxParameters(methodDeclarationSyntax, generatorSyntaxContext));
    }

    private static ITypeDefinition? GetReturnType(MethodDeclarationSyntax methodDeclarationSyntax,
        GeneratorSyntaxContext generatorSyntaxContext)
    {
        return methodDeclarationSyntax.ReturnType.GetTypeDefinition(generatorSyntaxContext);
    }

    private static IReadOnlyList<HardenedParameterDefinition> GenerateSyntaxParameters(
        MethodDeclarationSyntax methodDeclarationSyntax, GeneratorSyntaxContext generatorSyntaxContext)
    {
        var parameters = new List<HardenedParameterDefinition>();

        foreach (var parameter in methodDeclarationSyntax.ParameterList.Parameters)
        {
            if (parameter.Type != null)
            {
                parameters.Add(new HardenedParameterDefinition(
                    parameter.Identifier.ValueText,
                    parameter.Type!.GetTypeDefinition(generatorSyntaxContext)!
                ));
            }
        }
        return parameters;
    }
}