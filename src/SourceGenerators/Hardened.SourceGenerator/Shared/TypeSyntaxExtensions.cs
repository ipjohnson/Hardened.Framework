using CSharpAuthor;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Shared;

public static class TypeSyntaxExtensions
{
    public static ITypeDefinition? GetTypeDefinition(
        this SyntaxNode typeSyntax,
        GeneratorSyntaxContext generatorSyntaxContext
    )
    {
        var symbolInfo = generatorSyntaxContext.SemanticModel.GetSymbolInfo(typeSyntax);

        var type = GetTypeDefinitionFromSymbolInfo(
            symbolInfo,
            new ReferenceSite(generatorSyntaxContext.SemanticModel, typeSyntax.SpanStart)
        );

        if (typeSyntax.ToString().EndsWith("?"))
        {
            return type?.MakeNullable();
        }

        return type;
    }

    public static string GetFullName(this INamespaceSymbol? namespaceSymbol)
    {
        if (namespaceSymbol == null)
        {
            return "";
        }

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

        return TypeDefinition.Get(
            typeEnum,
            typeSymbol.ContainingNamespace.GetFullName(),
            GetTypeName(typeSymbol)
        );
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

    public static ITypeDefinition? GetTypeDefinitionFromSymbolInfo(
        SymbolInfo symbolInfo,
        ReferenceSite? site = null
    )
    {
        if (symbolInfo.Symbol is INamedTypeSymbol namedTypeSymbol)
        {
            return GetTypeDefinitionFromNamedSymbol(namedTypeSymbol, site);
        }

        if (symbolInfo.Symbol is IArrayTypeSymbol arrayTypeSymbol)
        {
            return GetTypeDefinitionFromType(arrayTypeSymbol.ElementType, site).MakeArray();
        }

        return null;
    }

    private static ITypeDefinition? GetTypeDefinitionFromNamedSymbol(
        INamedTypeSymbol namedTypeSymbol,
        ReferenceSite? site
    )
    {
        if (
            site != null
            && namedTypeSymbol.TypeKind == TypeKind.Error
            && ConfigurationModelInterface(namedTypeSymbol, site) is { } modelInterface
        )
        {
            return modelInterface;
        }

        if (namedTypeSymbol.IsGenericType)
        {
            if (namedTypeSymbol.Name == "Nullable")
            {
                var baseType = namedTypeSymbol.TypeArguments.First();
                return GetTypeDefinitionFromType(baseType, site).MakeNullable();
            }

            var closingTypeSymbols = namedTypeSymbol.TypeArguments;

            var closingTypes = new List<ITypeDefinition>();

            foreach (var typeSymbol in closingTypeSymbols)
            {
                var finalType = GetTypeDefinitionFromType(typeSymbol, site);

                closingTypes.Add(finalType);
            }

            var genericType = new GenericTypeDefinition(
                GetTypeSymbolKind(namedTypeSymbol),
                namedTypeSymbol.ContainingNamespace.GetFullName(),
                GetTypeName(namedTypeSymbol),
                closingTypes
            );

            if (namedTypeSymbol.NullableAnnotation == NullableAnnotation.Annotated)
            {
                return genericType.MakeNullable();
            }

            return genericType;
        }
        else if (IsKnownType(namedTypeSymbol.Name)) { }

        var ns = namedTypeSymbol.ContainingNamespace.GetFullName();
        var getName = GetTypeName(namedTypeSymbol);

        var typeDef = TypeDefinition.Get(
            GetTypeSymbolKind(namedTypeSymbol),
            namedTypeSymbol.ContainingNamespace.GetFullName(),
            GetTypeName(namedTypeSymbol)
        );

        if (namedTypeSymbol.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return typeDef.MakeNullable();
        }

        return typeDef;
    }

    /// <summary>
    /// The type as the emitters write it, with its generic arguments and its array rank.
    /// </summary>
    /// <remarks>
    /// Public because a lambda registered as a route has no type syntax to read - its parameter and
    /// return types are only symbols. <see cref="GetTypeDefinition(ITypeSymbol)"/> is the older
    /// entry point and answers the bare name, which turns <c>Task&lt;Order&gt;</c> into <c>Task</c>.
    /// </remarks>
    public static ITypeDefinition GetTypeDefinitionFromType(
        ITypeSymbol typeSymbol,
        ReferenceSite? site = null
    )
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

        if (typeSymbol is ITypeParameterSymbol typeParameterSymbol)
        {
            // A TypeParameterDefinition, not TypeDefinition.Get("", name): an empty namespace now
            // means the global namespace, which Global mode qualifies - and global::T is not a type.
            return new TypeParameterDefinition(typeParameterSymbol.Name);
        }

        if (typeSymbol is IArrayTypeSymbol arrayTypeSymbol)
        {
            return GetTypeDefinitionFromType(arrayTypeSymbol.ElementType, site).MakeArray();
        }

        if (typeSymbol is INamedTypeSymbol namedTypeSymbol)
        {
            return GetTypeDefinitionFromNamedSymbol(namedTypeSymbol, site)!;
        }

        return TypeDefinition.Get(typeSymbol.ContainingNamespace.GetFullName(), typeSymbol.Name);
    }

    /// <summary>
    /// The interface the configuration generator writes for a <c>[ConfigurationModel]</c> class.
    /// </summary>
    /// <remarks>
    /// One generator cannot see another's output, so <c>ITodoListOptions</c> written beside its
    /// class <c>TodoListOptions</c> is an error type here, and an error type's namespace is the
    /// global one. The generated interface shares the class's namespace, so the class's name is
    /// looked up where the reference was written, under the same usings.
    /// </remarks>
    private static ITypeDefinition? ConfigurationModelInterface(
        INamedTypeSymbol errorType,
        ReferenceSite site
    )
    {
        var name = errorType.Name;

        if (
            name.Length < 2
            || name[0] != 'I'
            || errorType.Arity != 0
            || errorType.ContainingNamespace is not { IsGlobalNamespace: true }
        )
        {
            return null;
        }

        var model = site
            .SemanticModel.LookupNamespacesAndTypes(site.Position, name: name.Substring(1))
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault(candidate =>
                candidate.ContainingType == null
                && candidate
                    .GetAttributes()
                    .Any(attribute =>
                        attribute.AttributeClass?.Name
                            == KnownTypes.Configuration.ConfigurationModelAttribute.Name
                        && attribute.AttributeClass.ContainingNamespace.GetFullName()
                            == KnownTypes.Configuration.ConfigurationModelAttribute.Namespace
                    )
            );

        if (model == null)
        {
            return null;
        }

        var typeDef = TypeDefinition.Get(
            TypeDefinitionEnum.InterfaceDefinition,
            model.ContainingNamespace.GetFullName(),
            name
        );

        return errorType.NullableAnnotation == NullableAnnotation.Annotated
            ? typeDef.MakeNullable()
            : typeDef;
    }

    /// <summary>Where a type was written, so a name the compilation cannot bind yet can be looked up.</summary>
    public sealed class ReferenceSite
    {
        public ReferenceSite(SemanticModel semanticModel, int position)
        {
            SemanticModel = semanticModel;
            Position = position;
        }

        public SemanticModel SemanticModel { get; }

        public int Position { get; }
    }

    private static bool IsKnownType(string name)
    {
        return false;
    }
}
