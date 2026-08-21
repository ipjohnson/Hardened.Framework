namespace Hardened.Idl.Emitters;

/// <summary>
/// How a generated service interface states the responses an operation declares.
/// </summary>
/// <remarks>
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
    /// <c>Task&lt;Pet&gt;</c>, with declared errors thrown. The default, and what every generated
    /// interface says today.
    /// </summary>
    Standard,

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
