using CSharpAuthor;

namespace Hardened.Idl.Models;

/// <summary>
/// The types an operation names, when whoever built the model already knew them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> A description names types as strings and <c>$ref</c>s, because the build
/// task that reads one has no compilation to resolve against — <see cref="Hardened.Idl.TypeMapper"/>
/// is 300 lines of spelling them back. A code-first application has the types already: the generator
/// reading its attributes holds a Roslyn compilation and can name any of them exactly.
/// </para>
/// <para>
/// So that both can produce one <see cref="OperationModel"/>, this carries what was known rather
/// than making the code-first side throw its types away, spell them, and pay to have them guessed
/// back. Where it is populated, <c>RequestModelBuilder</c> uses it; where it is null, the strings
/// answer exactly as they do today.
/// </para>
/// <para>
/// <b>Never serialized.</b> A description-driven model crosses a process boundary — the task writes
/// it, the generator reads it — and nothing here would survive that or need to: it is only ever
/// present in the process that created it. <c>SpecModelSerializer</c> does not write it, which is
/// also why adding this changes no file format.
/// </para>
/// <para>
/// <b>Outside equality, deliberately.</b> <see cref="OperationModel"/> implements value equality so
/// Roslyn's incremental caches can decide whether to rerun. These types are derived from the same
/// source the rest of the model came from, so they cannot differ between two otherwise equal
/// operations — and including reference-typed members that compare by reference would defeat the
/// caching rather than sharpen it.
/// </para>
/// </remarks>
public sealed class OperationSymbols {

    /// <summary>The type declaring the handler method — a controller, or a described service interface.</summary>
    public ITypeDefinition? ControllerType { get; set; }

    /// <summary>The generated per-operation handler class.</summary>
    public ITypeDefinition? InvokeHandlerType { get; set; }

    /// <summary>What the handler returns, unwrapped from any task.</summary>
    public ITypeDefinition? ResponseType { get; set; }

    /// <summary>The request body's type, if the operation takes one.</summary>
    public ITypeDefinition? RequestBodyType { get; set; }

    /// <summary>Parameter types by parameter name, for the ones already resolved.</summary>
    public Dictionary<string, ITypeDefinition>? ParameterTypes { get; set; }

    /// <summary>The resolved type for <paramref name="parameterName"/>, or null.</summary>
    public ITypeDefinition? Parameter(string parameterName) =>
        ParameterTypes != null && ParameterTypes.TryGetValue(parameterName, out var type) ? type : null;
}
