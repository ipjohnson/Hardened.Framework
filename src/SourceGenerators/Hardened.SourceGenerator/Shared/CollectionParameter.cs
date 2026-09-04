using CSharpAuthor;

namespace Hardened.SourceGenerator.Shared;

/// <summary>
/// Whether a handler parameter is a collection of items, and of what.
/// </summary>
/// <remarks>
/// Read twice for every parameter and both readings have to agree: the binder emits a different
/// conversion call for a collection, and the published document describes it as an array rather
/// than as whatever its C# type name falls through to.
/// </remarks>
public static class CollectionParameter {

    /// <summary>
    /// The interfaces a <c>List</c> already satisfies, which is what the converter answers with.
    /// <c>HashSet</c> and the rest are deliberately absent: they would need their own construction,
    /// and nothing describes a query parameter as a set.
    /// </summary>
    private static readonly HashSet<string> TypeNames = new() {
        "List", "IList", "ICollection", "IEnumerable", "IReadOnlyList", "IReadOnlyCollection"
    };

    /// <summary>
    /// The item type, or null when the parameter is not a collection.
    /// </summary>
    /// <remarks>
    /// A jagged or multi-dimensional array is not a collection of items, so it answers null and
    /// binds as the scalar it is not - the same refusal it produced before, from the place that
    /// already explains it.
    /// </remarks>
    public static ITypeDefinition? ItemType(ITypeDefinition parameterType) {
        if (parameterType.IsArray) {
            if (parameterType.ArrayRanks.Count != 1 || parameterType.ArrayRanks[0] != 1) {
                return null;
            }

            return new TypeDefinition(
                parameterType.TypeDefinitionEnum,
                parameterType.Namespace,
                parameterType.Name,
                null,
                false,
                parameterType.ContainingType);
        }

        if (parameterType.TypeArguments.Count != 1 ||
            parameterType.Namespace != "System.Collections.Generic" ||
            !TypeNames.Contains(parameterType.Name)) {
            return null;
        }

        return parameterType.TypeArguments[0];
    }
}
