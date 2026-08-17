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
    private const string AuthorizationNamespace = "Hardened.Requests.Runtime.Authorization";

    private static readonly string[] SpeaksForItself = {
        "AuthorizeAttribute",
        "AuthorizeGrantsAttribute",
        "AllowAnonymousAttribute"
    };

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

                // Matched by namespace as well as name. Accepting another framework's [Authorize] -
                // which Hardened does not honour - would silence the warning on a handler that is
                // genuinely unguarded, turning a false positive into a false negative.
                var name = type.Name.EndsWith("Attribute") ? type.Name : type.Name + "Attribute";

                if (SpeaksForItself.Contains(name) &&
                    type.ContainingNamespace?.ToDisplayString() == AuthorizationNamespace) {
                    return true;
                }
            }
        }

        return false;
    }
}
