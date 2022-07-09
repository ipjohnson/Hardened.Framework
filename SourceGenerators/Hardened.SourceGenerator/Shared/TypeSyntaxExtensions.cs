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

            return GetTypeDefinitionFromSymbolInfo(symbolInfo);
        }

        private static ITypeDefinition? GetTypeDefinitionFromSymbolInfo(SymbolInfo symbolInfo)
        {
            if (symbolInfo.Symbol is INamedTypeSymbol namedTypeSymbol)
            {
                if (namedTypeSymbol.IsGenericType)
                {
                    if (namedTypeSymbol.Name == "Nullable")
                    {
                        var baseType = namedTypeSymbol.TypeArguments.First();

                        return GetTypeDefinitionFromType(baseType).MakeNullable();
                    }
                }
                else if (IsKnownType(namedTypeSymbol.Name))
                {
                }

                return TypeDefinition.Get(namedTypeSymbol.ContainingNamespace.Name, namedTypeSymbol.Name);
            }

            return null;
        }

        private static ITypeDefinition GetTypeDefinitionFromType(ITypeSymbol typeSymbol)
        {
            switch (typeSymbol.SpecialType)
            {
                case SpecialType.System_Int16:
                    return TypeDefinition.Get(typeof(short));

                case SpecialType.System_Int32:
                    return TypeDefinition.Get(typeof(int));

                case SpecialType.System_Int64:
                    return TypeDefinition.Get(typeof(long));

                case SpecialType.System_UInt16:
                    return TypeDefinition.Get(typeof(ushort));

                case SpecialType.System_UInt32:
                    return TypeDefinition.Get(typeof(uint));

                case SpecialType.System_UInt64:
                    return TypeDefinition.Get(typeof(ulong));

                case SpecialType.System_String:
                    return TypeDefinition.Get(typeof(string));
            }

            return TypeDefinition.Get(typeSymbol.ContainingNamespace.Name, typeSymbol.Name);
        }

        private static bool IsKnownType(string name)
        {
            return false;
        }
    }
}
