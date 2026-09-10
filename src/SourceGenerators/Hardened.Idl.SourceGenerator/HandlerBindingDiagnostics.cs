using System.Collections.Generic;
using System.Linq;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Idl.SourceGenerator;

/// <summary>
/// The three ways a described operation ends up doing less than its implementation says, with a
/// clean build.
/// </summary>
/// <remarks>
/// <para>
/// All three are warnings rather than errors, and deliberately. Generating the interfaces without
/// implementing them is a supported thing to do - a package that carries a contract for a client to
/// consume, or one project describing a service another implements - so an error would make a
/// legitimate target impossible to build.
/// </para>
/// <para>
/// <c>TreatWarningsAsErrors</c> is set for continuous-integration builds, so a project that means to
/// ship interfaces alone silences these in its own csproj:
/// <c>&lt;NoWarn&gt;$(NoWarn);HOAG030;HOAG031;HOAG032&lt;/NoWarn&gt;</c>. That is the escape hatch, and it
/// is deliberate that it has to be written down rather than inferred.
/// </para>
/// </remarks>
internal static class HandlerBindingDiagnostics {

    /// <summary>A described service that nothing implements.</summary>
    public const string NoHandlerId = "HOAG030";

    /// <summary>A handler whose base list names no described service.</summary>
    public const string NoServiceInterfaceId = "HOAG031";

    /// <summary>A declaration the described path does not read, on a handler class or method.</summary>
    public const string InertDeclarationId = "HOAG032";

    /// <summary>
    /// The declarations that are code-first syntax only, by attribute name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each of these is read off a handler's own syntax by the attribute-routed generator. A
    /// described operation's signature is generated instead, so there is no syntax to read it from:
    /// the attribute compiles on the implementation, reads as a commitment in review, and changes
    /// nothing. Every one of them has a spelling in the contract, which is what the message names.
    /// </para>
    /// <para>
    /// A generic attribute keys on its simple name here. <c>AttributeModelHelper</c> puts the type
    /// arguments in the type definition rather than in <c>Name</c>, so <c>[Throws&lt;Gone&gt;]</c>
    /// arrives as <c>ThrowsAttribute</c> and an exact lookup finds it.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> InertDeclarations = new(StringComparer.Ordinal) {
        ["RawResponseAttribute"] =
            "the content type a described response commits to comes from the contract's media type",
        ["ThrowsAttribute"] =
            "a described operation answers the statuses its contract declares, and the generated " +
            "signature carries them",
        ["TagAttribute"] =
            "a described operation is grouped by the tag its contract gives it",
        ["ServerAttribute"] =
            "write a servers block in the contract, or declare [Server] on the [HardenedModule] " +
            "class whose compilation writes the document"
    };

    /// <summary>
    /// Built per call rather than held in a static field, for the RS2008 reason the other
    /// descriptors in this repository are - see <c>UnresolvedHandler</c>.
    /// </summary>
    private static DiagnosticDescriptor NoHandlerDescriptor() => new(
        id: NoHandlerId,
        title: "Described service has no handler",
        messageFormat:
        "'{0}' is declared by the description but no class carrying [Handler] implements it, so " +
        "its {1} route(s) exist and fail at request time. Implement it, or set " +
        "<NoWarn>$(NoWarn);" + NoHandlerId + "</NoWarn> if this project ships the interface alone.",
        category: "Hardened.Generation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static DiagnosticDescriptor InertDeclarationDescriptor() => new(
        id: InertDeclarationId,
        title: "Declaration is not read on a described handler",
        messageFormat:
        "'{0}' carries [{1}], which is read from a handler's own syntax and a described " +
        "operation's signature is generated - so it compiles, reads as a commitment, and changes " +
        "nothing. Remove it: {2}.",
        category: "Hardened.Generation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static DiagnosticDescriptor NoServiceInterfaceDescriptor() => new(
        id: NoServiceInterfaceId,
        title: "Handler implements no described service",
        messageFormat:
        "'{0}' carries [Handler] but its base list names no service the description declares - " +
        "it lists {1}. It is registered against '{2}', which routes nothing. A base class has to " +
        "be first in C#, so the service interface is found by name rather than by position.",
        category: "Hardened.Generation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports both directions of the match onto <paramref name="context"/>.
    /// </summary>
    public static void Report(
        SourceProductionContext context,
        IReadOnlyList<RequestHandlerModel> models,
        IReadOnlyList<HandlerInfo> handlers) {
        foreach (var diagnostic in Collect(models, handlers, context.CancellationToken)) {
            context.ReportDiagnostic(diagnostic);
        }
    }

    /// <summary>
    /// Both directions of the match, given everything the description declared and every
    /// <c>[Handler]</c> in the compilation.
    /// </summary>
    /// <remarks>
    /// Returned rather than reported, so the decision is testable. <c>SourceProductionContext</c> is
    /// a struct Roslyn alone can construct, and a rule that can only be exercised by running a whole
    /// generator tends not to be exercised at all.
    /// </remarks>
    public static IReadOnlyList<Diagnostic> Collect(
        IReadOnlyList<RequestHandlerModel> models,
        IReadOnlyList<HandlerInfo> handlers,
        CancellationToken cancellationToken = default) {
        var diagnostics = new List<Diagnostic>();

        if (models.Count == 0) {
            // No description in this project at all, so neither direction means anything. A
            // hand-written [Handler] belongs to the other generator.
            return diagnostics;
        }

        var declaredNames = new HashSet<string>(models.Select(model => model.ControllerType.Name));
        var implementedNames = new HashSet<string>();

        foreach (var handler in handlers) {
            cancellationToken.ThrowIfCancellationRequested();

            ReportInertDeclarations(handler, diagnostics);

            var service = handler.ServiceInterface(declaredNames);

            if (service != null) {
                implementedNames.Add(service.Name);
                continue;
            }

            diagnostics.Add(Diagnostic.Create(
                NoServiceInterfaceDescriptor(),
                handler.Location ?? Location.None,
                handler.ImplementationType.Name,
                string.Join(", ", handler.InterfaceCandidates.Select(candidate => candidate.Name)),
                handler.InterfaceType.Name));
        }

        // Ordered, so a project with two missing handlers reports them the same way every build.
        foreach (var name in declaredNames.OrderBy(name => name, StringComparer.Ordinal)) {
            if (implementedNames.Contains(name)) {
                continue;
            }

            var routeCount = models.Count(model => model.ControllerType.Name == name);

            diagnostics.Add(Diagnostic.Create(
                NoHandlerDescriptor(), Location.None, name, routeCount));
        }

        return diagnostics;
    }

    /// <summary>
    /// Every declaration on this handler that the described path does not read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read off what <c>HandlerSelector</c> already collected rather than from a walk of its own,
    /// so an attribute added to <see cref="InertDeclarations"/> costs a table entry and nothing
    /// else. The location is the class's, which is what <c>HandlerInfo</c> carries; the message
    /// names the method where there is one.
    /// </para>
    /// <para>
    /// Both rungs, because the attributes divide across them. <c>[RawResponse]</c> and
    /// <c>[Throws&lt;T&gt;]</c> are written on a method and <c>[Tag]</c> and <c>[Server]</c> are
    /// <c>AttributeTargets.Class</c>, so a walk over methods alone would take a table entry for
    /// either of the last two and report nothing - a diagnostic that looks configured and is not,
    /// which is the shape of the defect this whole file exists to catch.
    /// </para>
    /// </remarks>
    private static void ReportInertDeclarations(HandlerInfo handler, List<Diagnostic> diagnostics) {
        foreach (var filter in handler.ClassFilters) {
            Report(handler, filter, handler.ImplementationType.Name, diagnostics);
        }

        foreach (var method in handler.MethodFilters) {
            foreach (var filter in method.Filters) {
                Report(
                    handler, filter,
                    handler.ImplementationType.Name + "." + method.MethodName, diagnostics);
            }
        }
    }

    private static void Report(
        HandlerInfo handler, AttributeModel declaration, string where, List<Diagnostic> diagnostics) {
        if (!InertDeclarations.TryGetValue(declaration.TypeDefinition.Name, out var instead)) {
            return;
        }

        diagnostics.Add(Diagnostic.Create(
            InertDeclarationDescriptor(),
            handler.Location ?? Location.None,
            where,
            declaration.TypeDefinition.Name.Replace("Attribute", ""),
            instead));
    }
}
