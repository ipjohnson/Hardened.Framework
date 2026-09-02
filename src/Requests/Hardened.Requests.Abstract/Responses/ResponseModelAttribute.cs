namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The response model this module's handlers are written in.
/// </summary>
/// <remarks>
/// <para>
/// On the module entry point, which is where <c>[CaseInsensitiveRoutes]</c> and <c>[BasePath]</c>
/// already live and for the same reason: an assembly can hold two entry points, so neither the
/// csproj nor an <c>.editorconfig</c> can say that module A is <see cref="ResponseModel.Union"/>
/// and module B is <see cref="ResponseModel.Throws"/>. A project-level property would have to
/// pick one for both.
/// </para>
/// <para>
/// A build-time switch with nothing left to decide at run time, again like
/// <c>[CaseInsensitiveRoutes]</c>: the mode changes what the generator emits into the handler's
/// invoke method, and by the time a request arrives there is only the emitted code.
/// </para>
/// <para>
/// Absent means <see cref="ResponseModel.Throws"/>, so every application that has never heard of
/// this keeps building exactly as it did. New projects scaffold as
/// <see cref="ResponseModel.Response"/>: code-first, the return types say so on their own and the
/// template writes no attribute; spec-first, the template writes
/// <c>&lt;HardenedResponseModel&gt;</c> out for every mode.
/// </para>
/// <para>
/// Known limit: the module writer replays a module's attributes by re-instantiating them and
/// renders an enum argument as its bare integer, which C# converts implicitly only when it is 0 -
/// so today this attribute compiles on an entry point only with <see cref="ResponseModel.Throws"/>.
/// The fix belongs to DependencyModules' writer, not here.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ResponseModelAttribute : Attribute {

    public ResponseModelAttribute(ResponseModel model) {
        Model = model;
    }

    /// <summary>The model this module's handlers declare their responses in.</summary>
    public ResponseModel Model { get; }
}
