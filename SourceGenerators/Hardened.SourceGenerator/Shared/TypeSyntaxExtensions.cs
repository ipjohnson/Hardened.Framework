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
        public static ITypeDefinition? GetTypeDefinition(this SyntaxNode typeSyntax,
            GeneratorSyntaxContext generatorSyntaxContext)
        {
            var symbolInfo = generatorSyntaxContext.SemanticModel.GetSymbolInfo(typeSyntax);

            return GetTypeDefinitionFromSymbolInfo(symbolInfo);
        }

        public static string GetFullName(this INamespaceSymbol namespaceSymbol)
        {
            var baseString = namespaceSymbol.ContainingNamespace?.GetFullName();

            if (string.IsNullOrEmpty(baseString))
            {
                return namespaceSymbol.Name;
            }

            return baseString + "." + namespaceSymbol.Name;
        }

        public static ITypeDefinition GetTypeDefinition(this ITypeSymbol typeSymbol)
        {
            var typeEnum = GetTypeSymbolKind(typeSymbol);

            return TypeDefinition.Get(typeEnum, typeSymbol.ContainingNamespace.GetFullName(), GetTypeName(typeSymbol));
        }

        private static TypeDefinitionEnum GetTypeSymbolKind(ITypeSymbol typeSymbol)
        {
            var typeEnum = TypeDefinitionEnum.ClassDefinition;

            if (typeSymbol.TypeKind == TypeKind.Enum)
            {
                typeEnum = TypeDefinitionEnum.EnumDefinition;
            }
            else if (typeSymbol.TypeKind == TypeKind.Interface)
            {
                typeEnum = TypeDefinitionEnum.InterfaceDefinition;
            }

            return typeEnum;
        }

        private static string GetTypeName(ITypeSymbol typeSymbol)
        {
            if (typeSymbol.ContainingType != null)
            {
                return GetTypeName(typeSymbol.ContainingType) + "." + typeSymbol.Name;
            }

            return typeSymbol.Name;
        }

        public static ITypeDefinition? GetTypeDefinitionFromSymbolInfo(SymbolInfo symbolInfo)
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

                    var closingTypeSymbols = namedTypeSymbol.TypeParameters;
                    var closingTypes = new List<ITypeDefinition>();

                    foreach (var typeSymbol in closingTypeSymbols)
                    {
                        closingTypes.Add(GetTypeDefinitionFromType(typeSymbol));
                    }

                    return new GenericTypeDefinition(
                        GetTypeSymbolKind(namedTypeSymbol),
                        namedTypeSymbol.ContainingNamespace.GetFullName(),
                        GetTypeName(namedTypeSymbol),
                        closingTypes
                    );
                }
                else if (IsKnownType(namedTypeSymbol.Name))
                {
                }

                return TypeDefinition.Get(
                    GetTypeSymbolKind(namedTypeSymbol),
                    namedTypeSymbol.ContainingNamespace.GetFullName(), GetTypeName(namedTypeSymbol));
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

            return TypeDefinition.Get(typeSymbol.ContainingNamespace.GetFullName(), typeSymbol.Name);
        }

        private static bool IsKnownType(string name)
        {
            return false;
        }
    }
}
