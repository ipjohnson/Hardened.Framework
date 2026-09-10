namespace Hardened.SourceGenerator.Shared;

/// <summary>
/// What the filters an entry point declares answer, for the generator writing the document.
/// </summary>
/// <remarks>
/// <para>
/// An interface with one member, and that is the whole point of it. <c>Shared</c> is the folder
/// every generator compiles - the validation generator compiles this and one other file and nothing
/// else - so a type declared here may not reach into the response models, which only the generators
/// that write a document compile. <see cref="EntryPointSelector.Model"/> has to carry the facts
/// across that line, so it carries them as this.
/// </para>
/// <para>
/// <c>DeclaredOperationFacts</c> is the implementation and the only one. A generator that writes a
/// document names it directly to narrow the facts per operation; one that does not can still
/// compare two models and see that nothing changed.
/// </para>
/// </remarks>
public interface IEntryPointFilterFacts {
    /// <summary>Whether the entry point's declarations say anything about a document at all.</summary>
    bool IsEmpty { get; }
}
