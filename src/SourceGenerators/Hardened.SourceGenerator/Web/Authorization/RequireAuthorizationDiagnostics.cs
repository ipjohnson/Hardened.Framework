using System.Collections.Generic;
using System.Linq;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web.Authorization;

/// <summary>
/// A handler that will start refusing requests, reported at build rather than found in staging.
/// </summary>
/// <remarks>
/// <para>
/// Under <c>[RequireAuthorization]</c> a handler carrying neither a policy attribute nor
/// <c>[AllowAnonymous]</c> is denied at run time. That is the correct behaviour and a terrible way
/// to learn about it: the failure is a 403 on one route, in whatever environment somebody exercised
/// it in, long after the handler was written. Only syntax can catch it earlier, and only a generator
/// sees syntax.
/// </para>
/// <para>
/// A <b>warning</b> by default, so that adopting the attribute does not break a large application on
/// day one. <c>TreatWarningsAsErrors</c> is on when <c>ContinuousIntegrationBuild</c> is, so an
/// unannotated handler already cannot merge while still not blocking a refactor in progress. It can
/// be raised permanently, or lowered per file, through the mechanism every .NET developer already
/// knows:
/// </para>
/// <code>
/// dotnet_diagnostic.HAUTH001.severity = error
/// </code>
/// <para>
/// <b>The runtime backstop is the authoritative check, not this.</b> A generator only sees the
/// assembly being compiled, so handlers arriving from a referenced assembly are guarded without ever
/// being reported here. Two consequences follow, and both are the right way round: a handler this
/// does not warn about is still denied, and this warning being silenced never makes anything
/// reachable.
/// </para>
/// </remarks>
public static class RequireAuthorizationDiagnostics {
    public const string DiagnosticId = "HAUTH001";

    /// <summary>
    /// The attributes that count as having said something about authorization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matched by namespace as well as name. Matching on the name alone would accept another
    /// framework's <c>[Authorize]</c> - which Hardened does not honour - and silence the warning on
    /// a handler that is genuinely unguarded, turning a false positive into a false negative.
    /// </para>
    /// <para>
    /// The consequence is the other way round for an application that writes its own
    /// <c>IAuthorizeAttribute</c>: the pipeline honours it, this does not recognise it, and the
    /// handler is reported anyway. <c>[AllowAnonymous]</c> is the wrong answer there; an
    /// <c>.editorconfig</c> entry for the file is the right one.
    /// </para>
    /// </remarks>
    private const string AuthorizationNamespace = "Hardened.Requests.Runtime.Authorization";

    private static readonly string[] SpeaksForItself = {
        "AuthorizeAttribute",
        "AuthorizeGrantsAttribute",
        "AllowAnonymousAttribute"
    };

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>AmbiguousRouteDiagnostics.Descriptor</c> is: RS2008 looks for the field, and these
    /// projects set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() => new(
        id: DiagnosticId,
        title: "Handler carries no authorization attribute",
        messageFormat:
        "'{0}' carries neither an authorization attribute nor [AllowAnonymous], and this application " +
        "requires one, so the handler will refuse every request at run time. Write [Authorize<T>] or " +
        "[AuthorizeGrants] to say what it needs, or [AllowAnonymous] to say it is public on purpose.",
        category: "Hardened.Authorization",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Whether the entry point turned on the default-deny posture.
    /// </summary>
    /// <remarks>
    /// Read off the entry point's attributes, which carry both the class's and the assembly's -
    /// the same place <c>[BasePath]</c> is read from, and for the same reason: a module class can
    /// live in a project other than the one the attribute is convenient to write in.
    /// </remarks>
    public static bool IsRequired(EntryPointSelector.Model applicationModel) =>
        applicationModel.AttributeModels?.Any(
            attribute => attribute.TypeDefinition.Name is "RequireAuthorizationAttribute") ?? false;

    /// <summary>
    /// Reports every handler that will be denied for want of an attribute.
    /// </summary>
    public static void ReportUnauthorizedHandlers(
        SourceProductionContext context, IReadOnlyList<RequestHandlerModel> handlers) {
        foreach (var handler in handlers) {
            if (SaysSomethingAboutAuthorization(handler)) {
                continue;
            }

            // Location.None, as everywhere else models are reported from: a syntax location would
            // travel with the model through the incremental caches, which compare models for
            // equality to decide whether to regenerate.
            context.ReportDiagnostic(Diagnostic.Create(
                Descriptor(),
                Location.None,
                handler.ControllerType.Name + "." + handler.HandlerMethod));
        }
    }

    /// <remarks>
    /// The handler's filters carry the attributes written on the method <em>and</em> on its
    /// controller, so a policy declared once for a whole controller counts for every handler in it.
    /// </remarks>
    private static bool SaysSomethingAboutAuthorization(RequestHandlerModel handler) {
        foreach (var filter in handler.Filters) {
            if (filter.TypeDefinition.Namespace == AuthorizationNamespace &&
                SpeaksForItself.Contains(filter.TypeDefinition.Name)) {
                return true;
            }
        }

        return false;
    }
}
