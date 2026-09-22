using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// Whether a type is <c>IFormFile</c>, or a collection of it, which binds from a multipart form's
/// file parts rather than through the string converter.
/// </summary>
public static class FormFileType
{
    public const string Namespace = "Hardened.Requests.Abstract.Forms";

    public const string Name = "IFormFile";

    /// <summary>Whether <paramref name="type"/> is <c>IFormFile</c>.</summary>
    public static bool Is(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: Name } named
        && named.ContainingNamespace?.ToDisplayString() == Namespace;

    /// <summary>Whether <paramref name="type"/> is <c>IFormFile</c>, nullable or not.</summary>
    /// <remarks>
    /// An array is excluded here by name only: <c>IFormFile[]</c> carries the element's name and
    /// namespace, and is a collection for <see cref="Binds"/> to answer.
    /// </remarks>
    public static bool Is(ITypeDefinition type) =>
        !type.IsArray && type.Name == Name && type.Namespace == Namespace;

    /// <summary>
    /// Whether <paramref name="type"/> is <c>IFormFile</c> or a collection of it, in the shapes
    /// <c>CollectionParameter</c> recognises.
    /// </summary>
    public static bool Binds(ITypeDefinition type) =>
        Is(type) || (CollectionParameter.ItemType(type) is { } item && Is(item));
}
