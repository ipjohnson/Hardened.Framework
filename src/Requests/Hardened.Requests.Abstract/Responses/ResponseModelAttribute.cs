namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The response model this module's handlers are written in.
/// </summary>
/// <remarks>
/// <para>
/// On the module entry point, which is where <c>[CaseInsensitiveRoutes]</c> and <c>[BasePath]</c>
/// already live and for the same reason: an assembly can hold two entry points, so neither the
/// csproj nor an <c>.editorconfig</c> can say that module A is <see cref="ResponseModel.Union"/>
/// and module B is <see cref="ResponseModel.Standard"/>. A project-level property would have to
/// pick one for both.
/// </para>
/// <para>
/// A build-time switch with nothing left to decide at run time, again like
/// <c>[CaseInsensitiveRoutes]</c>: the mode changes what the generator emits into the handler's
/// invoke method, and by the time a request arrives there is only the emitted code.
/// </para>
/// <para>
/// Absent means <see cref="ResponseModel.Standard"/>, so every application that has never heard of
/// this keeps building exactly as it did.
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
