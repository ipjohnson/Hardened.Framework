using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Shared
{
    public static class TypeSyntaxExtensions
    {
        public static ITypeDefinition? GetTypeDefinition(this TypeSyntax typeSyntax,
            GeneratorSyntaxContext generatorSyntaxContext)
        {
            var symbolInfo = generatorSyntaxContext.SemanticModel.GetSymbolInfo(typeSyntax);

            if (symbolInfo.Symbol is INamedTypeSymbol namedTypeSymbol)
            {
                return TypeDefinition.Get(namedTypeSymbol.ContainingNamespace.Name, namedTypeSymbol.Name);
            }

            return null;
        }
    }
}
