using Hardened.Requests.Abstract.Outputs;

namespace Hardened.Requests.Abstract.Attributes;

/// <summary>
/// What writes this handler's response, instead of it being serialized.
///
/// <code>
/// [Get("/fortunes")]
/// [Output&lt;Views.Fortunes&gt;]
/// public FortunePage GetFortunes() => _repository.Load();
/// </code>
///
/// <para>
/// A type rather than a name. Because the attribute is applied in the application's own assembly,
/// RazorBlade's <c>internal</c> generated view classes are nameable here - which is the exact
/// problem a registry of named descriptors existed to work around.
/// </para>
///
/// <para>
/// <b>Declaring one takes the response out of negotiation.</b> The output either answers what the
/// client asked for or the request gets <c>406 Not Acceptable</c>; it never falls back to JSON. A
/// view usually renders a subset of what its model holds, so a fallback would put the rest of it on
/// the wire.
/// </para>
///
/// <para>
/// <b>What the constraint buys.</b> <c>[Output&lt;Views.Fortunes&gt;]</c> is your own source, bound
/// in the final compilation where RazorBlade's output exists, so a type that does not implement
/// <see cref="IHardenedResponseOutput"/> or lacks a parameterless constructor is an error
/// <em>on the attribute</em>, naming the type.
/// </para>
///
/// <para>
/// <b>What it cannot buy.</b> That the output's model matches the handler's return type. The
/// attribute cannot express it - it does not know the return type - and the generator cannot check
/// it, because the view is another generator's output and invisible to it. The generator emits an
/// assignment the compiler has to bind instead; see the <c>_outputCheck_</c> field beside each
/// handler.
/// </para>
///
/// <para>
/// <b>One construction shape only.</b> C# has <c>where T : new()</c> but no constraint for "has a
/// constructor taking <c>TModel</c>", so a second shape could not be compile-checked and would not
/// deliver the guarantee that makes this worth having. The value the handler returned is read from
/// the response.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class OutputAttribute<TOutput> : Attribute
    where TOutput : IHardenedResponseOutput, new() { }
