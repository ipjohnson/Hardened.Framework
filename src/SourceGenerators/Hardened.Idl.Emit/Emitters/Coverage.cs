using CSharpAuthor;

namespace Hardened.Idl.Emitters;

/// <summary>
/// Marks generated types as excluded from code coverage, so generated code does not count against a
/// project's numbers.
/// </summary>
/// <remarks>
/// <para>
/// One place decides, rather than a <c>excludeFromCoverage</c> parameter threaded through every
/// emitter. The emitters produce a type; whether it is measured is not their business.
/// </para>
/// <para>
/// <b>It cannot be applied uniformly.</b> <c>ExcludeFromCodeCoverageAttribute</c> is declared for
/// assembly, class, struct, constructor, method, property, indexer and event - not for interfaces or
/// enums, where it is CS0592 rather than a harmless no-op. So the check here is a language rule, not
/// a judgement about what is worth measuring.
/// </para>
/// </remarks>
internal static class Coverage {
    private static readonly ITypeDefinition ExcludeFromCodeCoverage =
        TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "ExcludeFromCodeCoverageAttribute");

    /// <summary>
    /// Applies the attribute to <paramref name="definition"/> when <paramref name="exclude"/> is set
    /// and the declaration can carry it.
    /// </summary>
    public static void Apply(IOutputComponent? definition, bool exclude) {
        if (!exclude) {
            return;
        }

        // Only ClassDefinition covers a declaration the attribute is valid on - records and structs
        // are class definitions here too. Interfaces and enums are passed in and skipped rather than
        // filtered by the caller, so the rule lives in one place.
        if (definition is ClassDefinition classDefinition) {
            classDefinition.AddAttribute(ExcludeFromCodeCoverage);
        }
    }
}
