using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;

namespace Hardened.SourceGenerator.Requests;

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
/// <b>Handed to the bridge beside the model, not carried on it.</b> The first attempt hung this off
/// <c>OperationModel</c>, which cannot work: that type lives in the spec spine, which builds
/// standalone precisely so a Roslyn or spine dependency creeping into it is caught — and the
/// interesting things to carry here are <c>ParameterBindType</c> and <c>ResponseInformationModel</c>,
/// both of which live in the spine. A property that can only ever hold what the spec model already
/// knows is a property worth nothing.
/// </para>
/// <para>
/// <b>Never serialized, by construction.</b> A described model crosses a process boundary — the task
/// writes it, the generator reads it — and this is only ever present in the process that created it.
/// Keeping it out of the model is also what keeps it out of the file format and out of the equality
/// Roslyn's incremental caches compare.
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

    /// <summary>
    /// How each parameter binds, for the kinds a description cannot name.
    /// </summary>
    /// <remarks>
    /// A description says <c>path</c>, <c>query</c>, <c>header</c> or <c>cookie</c>, and the bridge
    /// maps those. It has no way to say "inject this from the container" or "hand it the execution
    /// context", because those are not things a wire contract describes - and they are exactly what
    /// a code-first handler signature routinely asks for.
    /// </remarks>
    public Dictionary<string, ParameterBindType>? ParameterBindings { get; set; }

    /// <summary>
    /// The response shape, when the builder resolved it from a compilation.
    /// </summary>
    /// <remarks>
    /// A described response is assembled from a dozen fields - refs, formats, array items, status
    /// sets. A code-first one is read off the handler's return type in one step, and reassembling it
    /// through those fields only to have the bridge take it apart again would be a lossy way to say
    /// something already known exactly.
    /// </remarks>
    public ResponseInformationModel? ResponseInformation { get; set; }

    /// <summary>The resolved type for <paramref name="parameterName"/>, or null.</summary>
    public ITypeDefinition? Parameter(string parameterName) =>
        ParameterTypes != null && ParameterTypes.TryGetValue(parameterName, out var type) ? type : null;
}
