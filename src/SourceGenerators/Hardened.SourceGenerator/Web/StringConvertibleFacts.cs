using CSharpAuthor;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web;

/// <summary>
/// Whether a value of this type can be read from a string.
/// </summary>
/// <remarks>
/// <para>
/// The question a route template used to answer. An attribute route has a literal path, so a
/// parameter is a path token when its name appears in the template and the body otherwise. A route
/// registered at run time has no literal, so the type decides instead: a type that can be read from
/// a string is a path token, matched by name at registration, and anything else is the body.
/// </para>
/// <para>
/// The set is the one <c>StringConverterService</c> converts, and it is stated here rather than
/// inferred because it is a contract: a type this says yes to and the converter says no to is a
/// route that builds and answers 400 to every request.
/// </para>
/// <para>
/// <b><c>byte[]</c> is deliberately not in it.</b> The converter does convert one, from base64,
/// but a <c>byte[]</c> parameter is the request body itself and has been since raw bodies landed.
/// A path token of that type is not a thing anyone writes, and reading it as one would take the
/// body away from the handler that declared it.
/// </para>
/// </remarks>
internal static class StringConvertibleFacts
{
    /// <summary>
    /// Whether <paramref name="type"/> can be bound from one path token.
    /// </summary>
    /// <param name="symbol">
    /// The bound type, where the compilation has one. Only an enum needs it: an enum is named by
    /// the application rather than by this list, and the generator emits an
    /// <c>IStringConverter</c> for each one's wire vocabulary.
    /// </param>
    public static bool IsStringConvertible(ITypeDefinition type, ITypeSymbol? symbol)
    {
        if (type.IsArray)
        {
            return false;
        }

        var underlying = Underlying(symbol);

        if (underlying is { TypeKind: TypeKind.Enum })
        {
            return true;
        }

        // The symbol's name as well as the type definition's. A nullable type definition carries
        // the name CSharpAuthor gave it, which is not always the underlying type's - and int? binds
        // exactly as int does. What differs is whether the token has to be there, which is the
        // binder's question rather than this one.
        return Names.Contains(type.Name) || (underlying != null && Names.Contains(underlying.Name));
    }

    /// <summary>
    /// The type inside a <c>Nullable&lt;T&gt;</c>, or the type itself.
    /// </summary>
    /// <remarks>
    /// <c>int?</c> and <c>int</c> bind the same way. What differs is whether the token has to be
    /// there, which is the binder's question rather than this one.
    /// </remarks>
    private static ITypeSymbol? Underlying(ITypeSymbol? symbol) =>
        symbol is INamedTypeSymbol { IsGenericType: true, Name: "Nullable" } nullable
            ? nullable.TypeArguments[0]
            : symbol;

    /// <summary>
    /// Both spellings of every name, because a type definition carries whichever the source used.
    /// </summary>
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        "String",
        "string",
        "Boolean",
        "bool",
        "Byte",
        "byte",
        "SByte",
        "sbyte",
        "Int16",
        "short",
        "UInt16",
        "ushort",
        "Int32",
        "int",
        "UInt32",
        "uint",
        "Int64",
        "long",
        "UInt64",
        "ulong",
        "Single",
        "float",
        "Double",
        "double",
        "Decimal",
        "decimal",
        "Char",
        "char",
        "DateTime",
        "DateTimeOffset",
        "DateOnly",
        "TimeOnly",
        "TimeSpan",
        "Guid",
        "Uri",
    };
}
