namespace Hardened.Generation.Models;

/// <summary>
/// Which serialization attributes a generated model carries.
/// </summary>
/// <remarks>
/// <para>
/// JSON is not one of the answers to that question - every generated model carries
/// <c>[JsonPropertyName]</c> and the rest whatever this says. What it selects is whether
/// MessagePack is described on the model as well, and how a member is identified when it is:
/// by its name, or by an integer the contract states.
/// </para>
/// <para>
/// <b>Selected by <c>$(HardenedSerializer)</c>, not by an attribute</b>, for the reason
/// <see cref="SpecResponseModel"/> is: the specification-first direction runs in an MSBuild task
/// that runs before the compiler, and a task that runs first cannot read an attribute.
/// </para>
/// <para>
/// It also has to survive into the generator, which reads only the written model. The attributes
/// are emitted by the build task and the document is published by the generator, so a mode that
/// reached one and not the other would serve a document describing a representation whose field
/// identity is invisible.
/// </para>
/// </remarks>
public enum SpecSerializer {

    /// <summary>
    /// No MessagePack attributes. What an unset property means, so a project generated before this
    /// existed keeps generating the models it had.
    /// </summary>
    Json,

    /// <summary>
    /// <c>[MessagePackObject(true)]</c> on the type, and nothing per member: the wire carries the
    /// member's name, exactly as JSON does.
    /// </summary>
    MessagePackNamed,

    /// <summary>
    /// <c>[MessagePackObject]</c> on the type and <c>[Key(n)]</c> on each member, with every
    /// <c>n</c> taken from the contract. A member the contract does not key is a build error.
    /// </summary>
    MessagePackKeyed
}
