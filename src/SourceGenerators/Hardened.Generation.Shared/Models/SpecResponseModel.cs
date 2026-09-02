namespace Hardened.Generation.Models;

/// <summary>
/// How a generated service interface states the responses an operation declares.
/// </summary>
/// <remarks>
/// <para>
/// <b>In the shared model layer, not beside the emitters.</b> It is read on both sides of the
/// build: the emitters write the interface from it, and the generator writes the dispatch that
/// fills that interface from it. The generator compiles Hardened.Idl.Shared and not
/// Hardened.Idl.Emit, so an enum that lived beside the emitters was unreachable from the half that
/// also needs it - which is why the dispatch was emitted as though every operation were Standard.
/// </para>
/// <para>
/// The specification-first half of <c>Hardened.Requests.Abstract.Responses.ResponseModel</c>, and a
/// separate type for the reason the generator's own copy of that enum is separate: this assembly
/// targets <c>netstandard2.0</c> and declares no package references at all, which is the property
/// that keeps the IDL layer unable to reference an OpenAPI reader. Buying one enum with the first
/// package reference here would be a poor trade.
/// </para>
/// <para>
/// <b>Selected by an MSBuild property, not by <c>[ResponseModel]</c> on the entry point.</b> The
/// plan named the attribute, and that is not available here: the specification-first direction runs
/// in an MSBuild task, and that task runs <em>before</em> the compiler - the same ordering that
/// stops it from seeing routes. An attribute is read by a compiler; a task that runs first cannot
/// see one. <c>&lt;HardenedOpenApiVersion&gt;</c> is already the precedent for a build property
/// deciding what this emitter writes.
/// </para>
/// <para>
/// The two-entry-point argument that puts the attribute on a module rather than in the csproj does
/// not bite here, because what this selects is the shape of a generated service interface, and a
/// specification is listed in a project rather than declared by a module. A per-specification
/// override is item metadata away if one is ever wanted.
/// </para>
/// </remarks>
public enum SpecResponseModel {

    /// <summary>
    /// <c>Task&lt;Pet&gt;</c>, with declared errors thrown. What an unset
    /// <c>$(HardenedResponseModel)</c> still means, so a project scaffolded before the property
    /// existed keeps generating the interfaces it had. Named <c>Standard</c> until 0.19.0; the
    /// build task still reads the old property value and says so.
    /// </summary>
    Throws,

    /// <summary>
    /// <c>Task&lt;GetPetResponse&gt;</c>, where the container is a generated struct matching the
    /// basic union pattern.
    /// </summary>
    Response,

    /// <summary>
    /// The same, with the container declared as a C# 15 language union.
    /// </summary>
    Union
}
