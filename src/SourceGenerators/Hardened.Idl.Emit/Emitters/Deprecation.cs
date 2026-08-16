using CSharpAuthor;

namespace Hardened.OpenApi.SourceGenerator.Emitters;

/// <summary>
/// A schema or operation marked <c>deprecated</c>, as <c>[Obsolete]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Wrapped in <c>#pragma warning disable 618</c> on purpose. A generated interface member carrying
/// <c>[Obsolete]</c> produces CS0618 wherever it is implemented or called - which for a spec-first
/// project is the consumer's own handler, code they did not write and cannot annotate away. This
/// repository escalates warnings to errors under <c>ContinuousIntegrationBuild</c>, and consumers
/// commonly do the same, so one deprecated operation in a specification would break the build of
/// every project implementing it.
/// </para>
/// <para>
/// The attribute is emitted as a warning rather than an error for the same reason: deprecation is
/// notice that something will go, not that it has gone.
/// </para>
/// </remarks>
internal static class Deprecation {

    public static void Apply(BaseOutputComponent component) {
        component.AddAttribute(
            TypeDefinition.Get("System", "ObsoleteAttribute"),
            new CodeOutputComponent("\"Declared deprecated by the specification.\"") { Indented = false },
            new CodeOutputComponent("false") { Indented = false });

        component.WrapInPragma("618");
    }
}
