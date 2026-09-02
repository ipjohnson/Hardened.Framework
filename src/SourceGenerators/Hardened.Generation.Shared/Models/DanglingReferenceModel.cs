namespace Hardened.Generation.Models;

/// <summary>
/// A reference the description makes to something it never declares.
/// </summary>
/// <remarks>
/// <para>
/// Recorded as the document is read, for the reason <see cref="UnmappedKeywordModel"/> is: by the
/// time <c>SpecDiagnostics.Find</c> sees the model the reference has been cleared, because a
/// reference naming nothing generates C# naming nothing. The model can say what it holds and never
/// what it was told and dropped.
/// </para>
/// <para>
/// <b>Not the same thing as a reference to a schema that produced no type.</b> A top-level array
/// alias, an <c>anyOf</c> with no shape of its own, an undecidable <c>oneOf</c> - each is declared,
/// read, and deliberately resolved to something else, which the parser's own passes handle and say
/// nothing about because there is nothing wrong. This is the other case: the name is not in the
/// document at all.
/// </para>
/// <para>
/// Deliberately not serialized into the model file, and deliberately absent from the model's
/// equality. The build stops on one of these, so nothing downstream ever reads a model carrying
/// any - and a diagnostic in a cache key is a cache miss for a build that generates identical code.
/// </para>
/// </remarks>
internal sealed class DanglingReferenceModel {

    public DanglingReferenceModel(string reference, string location) {
        Reference = reference;
        Location = location;
    }

    /// <summary>The reference as the document spells it - <c>#/components/schemas/Pet</c>.</summary>
    public string Reference { get; }

    /// <summary>Where it was made, as far as the parser knows.</summary>
    public string Location { get; }
}
