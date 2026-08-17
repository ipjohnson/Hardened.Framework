using System.Linq;
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
/// unannotated handler already cannot merge while still not blocking a refactor in progress.
/// </para>
/// <para>
/// <b><c>&lt;NoWarn&gt;</c> is the only lever that works on it.</b> Measured: neither
/// <c>#pragma warning disable</c> nor a <c>dotnet_diagnostic</c> severity in <c>.editorconfig</c>
/// has any effect, scoped to a file or applied globally, and whether or not the diagnostic carries a
/// location - a diagnostic a source generator reports does not pass through the filtering those two
/// mechanisms drive. It is why <c>AmbiguousRouteDiagnostics</c> takes its severity from an MSBuild
/// property instead, and this should grow the same lever when something needs it.
/// </para>
/// <para>
/// <b>The runtime backstop is the authoritative check, not this.</b> A generator only sees the
/// assembly being compiled, so handlers arriving from a referenced assembly are guarded without ever
/// being reported here. Two consequences follow, and both are the right way round: a handler this
/// does not warn about is still denied, and this warning being silenced never makes anything
/// reachable.
/// </para>
/// <para>
/// An application writing its own <c>IAuthorizeAttribute</c> is reported even though the pipeline
/// honours it, because only the framework's own attributes are recognised here. <c>[AllowAnonymous]</c>
/// is emphatically the wrong answer to that - it would make the route genuinely public - so the
/// answer is <c>&lt;NoWarn&gt;</c> for the project.
/// </para>
/// </remarks>
public static class RequireAuthorizationDiagnostics {
    public const string DiagnosticId = "HAUTH001";

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
    /// Whether an entry point turned on the default-deny posture.
    /// </summary>
    /// <remarks>
    /// Read off the entry point's attributes, which carry both the class's and the assembly's - the
    /// same place <c>[BasePath]</c> is read from, and for the same reason: a module class can live in
    /// a project other than the one the attribute is convenient to write in.
    /// </remarks>
    public static bool IsRequired(EntryPointSelector.Model applicationModel) =>
        applicationModel.AttributeModels?.Any(
            attribute => attribute.TypeDefinition.Name is "RequireAuthorizationAttribute") ?? false;

    /// <summary>
    /// Reports one handler, if it needs reporting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One handler at a time rather than over the collected set, so an edit invalidates only the
    /// handlers it actually moved. Reporting off the collected array would make every keystroke in
    /// any controller rebuild one step covering all of them - cheap, but pointlessly so.
    /// </para>
    /// <para>
    /// The location is rebuilt from plain data the model carried, which is what puts this on the
    /// handler's own name: an editor squiggles it where it is written and the build prints a file
    /// and line rather than a bare message. It buys nothing for suppression - see the type's
    /// remarks - but a warning a developer sees while writing the handler is a different thing from
    /// one they scroll past in build output.
    /// </para>
    /// </remarks>
    public static void Report(
        SourceProductionContext context, HandlerAuthorizationModel handler, bool required) {
        if (!required || handler.SaysSomethingAboutAuthorization) {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptor(),
            handler.DeclaredAt?.ToLocation() ?? Location.None,
            handler.Handler));
    }
}
