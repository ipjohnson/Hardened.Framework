using System.Linq;
using System.Threading;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Web.Authorization;

/// <summary>
/// The least a diagnostic needs to know about a handler: who it is, whether it said anything about
/// authorization, and where it was written.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <c>RequestHandlerModel</c>. That model feeds code generation - one class per
/// handler, the routing table, the OpenAPI document - and everything it carries is compared to
/// decide whether all of that is rebuilt. Putting a location on it makes an edit anywhere above a
/// handler rebuild everything below, because a span is an offset and offsets shift.
/// </para>
/// <para>
/// This carries a location and churns on exactly the same edits, but nothing downstream of it emits
/// source. Recomputing it costs reading a handful of attribute names.
/// </para>
/// </remarks>
public record HandlerAuthorizationModel(
    string ControllerName,
    string MethodName,
    bool SaysSomethingAboutAuthorization,
    LocationInfo? DeclaredAt) {

    public string Handler => ControllerName + "." + MethodName;
}

public static class HandlerAuthorizationSelector {
    private const string AuthorizeInterface =
        "Hardened.Requests.Abstract.Authorization.IAuthorizeAttribute";

    private const string AllowAnonymous =
        "Hardened.Requests.Runtime.Authorization.AllowAnonymousAttribute";

    /// <summary>
    /// Reads a handler method and its controller, and nothing else.
    /// </summary>
    /// <remarks>
    /// The controller's attributes count as well as the method's, because the pipeline reads them
    /// the same way - a handler's filters carry both - so a policy written once for a controller has
    /// to silence this for every handler in it.
    /// </remarks>
    public static HandlerAuthorizationModel Transform(
        GeneratorSyntaxContext context, CancellationToken cancellationToken) {
        var method = (MethodDeclarationSyntax)context.Node;
        var controller = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();

        var declared =
            Speaks(context, method.AttributeLists, cancellationToken) ||
            (controller != null &&
                Speaks(context, controller.AttributeLists, cancellationToken));

        return new HandlerAuthorizationModel(
            controller?.Identifier.Text ?? "",
            method.Identifier.Text,
            declared,
            // The identifier rather than the whole declaration, so the squiggle lands on the name
            // instead of underlining the entire method body.
            LocationInfo.From(method.Identifier));
    }

    /// <summary>
    /// Whether any of these attributes is one the authorization pipeline honours.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Matched by interface rather than by type name.</b> The pipeline honours anything
    /// implementing <c>IAuthorizeAttribute</c>, so that is what has to be asked about here - a name
    /// list only recognises the framework's own two attributes and reports <c>HAUTH001</c> against
    /// a handler guarded by an application's own, which is a false positive on the one diagnostic
    /// whose job is to prevent false negatives. Deriving from <c>[AuthorizeGrants]</c> to give a
    /// grant a name is the expected way to write authorization, and it must not warn.
    /// </para>
    /// <para>
    /// It is also the only check that works uniformly. An attribute from a referenced assembly
    /// exposes its interfaces in metadata, so this sees them; its constructor body is not there to
    /// be read, which is why nothing here tries to learn <em>which</em> grants an attribute names.
    /// The runtime asks the attribute instance for that.
    /// </para>
    /// <para>
    /// Still exact about what it accepts. Another framework's <c>[Authorize]</c> implements nothing
    /// of Hardened's and is not honoured by the pipeline, so recognising it by name would silence
    /// the warning on a handler that is genuinely unguarded.
    /// </para>
    /// </remarks>
    private static bool Speaks(
        GeneratorSyntaxContext context,
        SyntaxList<AttributeListSyntax> attributeLists,
        CancellationToken cancellationToken) {
        foreach (var attributeList in attributeLists) {
            foreach (var attribute in attributeList.Attributes) {
                cancellationToken.ThrowIfCancellationRequested();

                var type = context.SemanticModel.GetTypeInfo(attribute, cancellationToken).Type;

                if (type == null) {
                    continue;
                }

                if (type.ToDisplayString() == AllowAnonymous) {
                    return true;
                }

                // AllInterfaces rather than Interfaces, so an attribute that derives from one
                // implementing it - which is the whole point of the base attribute not being
                // sealed - is recognised as well as one implementing it directly.
                foreach (var contract in type.AllInterfaces) {
                    if (contract.ToDisplayString() == AuthorizeInterface) {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
