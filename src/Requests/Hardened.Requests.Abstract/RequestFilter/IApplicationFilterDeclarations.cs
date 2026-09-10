using System.Reflection;

namespace Hardened.Requests.Abstract.RequestFilter;

/// <summary>
/// The filter declarations written on an application's entry point, known at startup.
/// </summary>
/// <remarks>
/// <para>
/// A filter can be declared on a handler method and on its containing class, and the generator
/// reads both into that handler's <see cref="Execution.IExecutionRequestHandlerInfo.Metadata"/>.
/// A declaration covering the whole application has no handler to sit on, so it is collected once
/// from the entry point's own attribute list and emitted beside the routing table. This is how it
/// reaches the pipeline.
/// </para>
/// <para>
/// <b>A rung rather than a registration, so the document can read it too.</b>
/// <c>[Enable&lt;ConditionalGet&gt;]</c> reaches every handler through
/// <see cref="IGlobalFilterRegistry"/>, and what decides which handlers it covers is a predicate
/// evaluated at startup - which the generator writing the OpenAPI document cannot evaluate,
/// because the document is written at build time. A declaration read inside the compilation is
/// visible to both halves.
/// </para>
/// <para>
/// Every entry is an attribute instance, constructed once for the application. It reaches a
/// handler through the same merge that decides whether it applies at all: an entry whose type the
/// handler already declares nearer is dropped, and the rest are appended to that handler's
/// metadata as its filter chain is built. See
/// <c>ExecutionRequestHandlerInfoExtensions.WithWiderRungs</c>.
/// </para>
/// <para>
/// Nothing is registered for an entry point that declares no filter, which is almost every
/// application. A process that composes several modules carries one of these per module that does,
/// each covering its own compilation - see <see cref="DeclaringAssembly"/>.
/// </para>
/// </remarks>
public interface IApplicationFilterDeclarations {
    /// <summary>
    /// The declarations, in the order they were written on the entry point.
    /// </summary>
    IReadOnlyList<object> Declared { get; }

    /// <summary>
    /// The compilation these were declared in, whose handlers they cover.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A process composes as many modules as it references and each brings its own, so what makes
    /// one declaration a fact about one set of handlers is the assembly it was written in. A host
    /// that references a library cannot declare a filter for that library's handlers: the library
    /// was compiled first, and its document was written then. <c>TimeoutResolver</c> answers the
    /// same rung the same way, and says so - the assembly rung is the <em>handler's</em> assembly.
    /// </para>
    /// <para>
    /// Defaulted to the implementation's own assembly, which is the answer for the class the
    /// routing generator emits and for anything written by hand beside the handlers it covers.
    /// </para>
    /// </remarks>
    Assembly DeclaringAssembly => GetType().Assembly;
}
