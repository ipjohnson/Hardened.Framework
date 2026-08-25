namespace Hardened.Generation.Models;

/// <summary>
/// A keyword the description declared and the parser did not map.
/// </summary>
/// <remarks>
/// <para>
/// Recorded as the document is read, because it cannot be recovered afterwards.
/// <c>SpecDiagnostics.Find</c> is handed a <see cref="ServiceSpecModel"/> and nothing else, and a
/// keyword nobody mapped left no trace in it - the model can say what it holds and never what it
/// was told and dropped. So the parser is the only place with both halves in hand, and this is it
/// writing one of them down.
/// </para>
/// <para>
/// <b>This is not the same class as a collapsed keyword</b> and does not catch one. A collapse -
/// <c>summary</c> folded into <c>description</c>, every tag after the first discarded - is a
/// keyword that was read, used, and flattened; it is present in the model, wearing the wrong shape.
/// Nothing here would notice. That class is found by auditing the parser, and was.
/// </para>
/// <para>
/// Deliberately not serialized into the model file. The generator reads that file to write C#, and
/// what the build declined to map changes nothing about the C# it writes - the build task reports
/// this and the report is finished before a line is generated. Carrying it further would put a
/// diagnostic in a cache key.
/// </para>
/// </remarks>
internal sealed class UnmappedKeywordModel {

    public UnmappedKeywordModel(string keyword, string location) {
        Keyword = keyword;
        Location = location;
    }

    /// <summary>The keyword as the description spells it - <c>multipleOf</c>, not <c>MultipleOf</c>.</summary>
    /// <remarks>
    /// The document's spelling rather than the parser's, because the reader is looking at the
    /// document. A message naming a C# property sends them searching a file that does not contain it.
    /// </remarks>
    public string Keyword { get; }

    /// <summary>Where it was declared, as far as the parser knows - a schema and member name.</summary>
    public string Location { get; }
}
