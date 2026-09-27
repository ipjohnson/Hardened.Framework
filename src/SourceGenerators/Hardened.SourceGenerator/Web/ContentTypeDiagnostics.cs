using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web;

/// <summary>
/// What an operation says it produces, checked against what could produce it.
/// </summary>
/// <remarks>
/// <para>
/// Two findings, at two severities, and the difference is what the build can know. A handler
/// answering with <c>byte[]</c> or <c>Stream</c> and declaring nothing is a fault entirely inside
/// the code that wrote it: bytes have no media type anyone could infer, and nothing downstream can
/// supply one. That is an error.
/// </para>
/// <para>
/// <b>Answering with, not returning.</b> A handler returning <c>Response&lt;byte[], NotFound&gt;</c>
/// puts bytes on the wire for its success and a model for its refusal, and read off the return type
/// this saw a model and said nothing - so the shape that needed the declaration most was the one
/// that never got it. What the success case of a response set carries is what this asks about; see
/// <c>UnionResponseSelector.SuccessCaseType</c>.
/// </para>
/// <para>
/// A declared media type nothing visible produces is a warning, because a library declaring
/// <c>text/csv</c> has no way to know whether its host will register a CSV serializer - and a
/// library that cannot compile without its host is not a library. The entry point is the only place
/// the full registration set exists, and that is where strictness could be raised later if it earns
/// it. <c>ContentTypeNotProducibleException</c> still answers the case that reaches run time.
/// </para>
/// <para>
/// <b>Visible means declared, not registered.</b> This used to treat every media type that was not
/// <c>application/json</c> as unproducible, so installing a serializer package did not silence it
/// and an application adopting one could not build warning-free. A serializer package says what it
/// writes with <c>[assembly: WritesContentType]</c> and <see cref="SerializerContentTypes"/> reads
/// that off this compilation and its references, which is what makes the warning answerable.
/// </para>
/// <para>
/// Found in the syntax transform, where the return type is known. The error is reported at the
/// handler's name, found in the compilation through <see cref="HandlerDeclaration"/>. The warning
/// is reported from the routing generator, which knows what the compilation writes, at
/// <see cref="Location.None"/>: the handler model carries no location, by design, because a span
/// on it would rebuild every handler below an edit.
/// </para>
/// </remarks>
public static class ContentTypeDiagnostics
{
    public const string MissingDeclarationId = "HRDR011";

    public const string NothingProducesId = "HRDR012";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>StreamFramingDiagnostics.Descriptor</c> is: RS2008 looks for the field, and these
    /// projects set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor MissingDeclaration() =>
        new(
            id: MissingDeclarationId,
            title: "handler answers with bytes and declares no content type",
            messageFormat: "'{0}' answers with byte[] or Stream and carries no [Produces], so nothing says what the "
                + "bytes are. Answering with either means the handler writes its own response, and no serializer "
                + "is consulted - declare the media type with [Produces(\"application/pdf\")] or answer with a "
                + "model.",
            category: "Hardened.Web",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

    private static DiagnosticDescriptor NothingProduces() =>
        new(
            id: NothingProducesId,
            title: "nothing in this compilation produces a declared content type",
            messageFormat: "'{0}' declares [Produces(\"{1}\")] and returns a model, and nothing here writes a model as "
                + "that media type. Register an IResponseSerializer declaring it, or return string, byte[] "
                + "or Stream and write the bytes yourself. A host that registers one makes this correct, "
                + "which is why it is a warning.",
            category: "Hardened.Web",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

    /// <summary>
    /// The same finding for a described operation, whose media types a contract declared rather
    /// than <c>[Produces]</c>.
    /// </summary>
    private static DiagnosticDescriptor NothingProducesDescribed() =>
        new(
            id: NothingProducesId,
            title: "nothing in this compilation produces a declared content type",
            messageFormat: "'{0}' is declared by its contract as answering {1}, and nothing here writes a model as that "
                + "media type. Register an IResponseSerializer declaring it, or reference the package that writes "
                + "it. A host that registers one makes this correct, which is why it is a warning.",
            category: "Hardened.Web",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

    /// <summary>
    /// Whether the handler answers with bytes and declares no media type. A handler that is not
    /// generated is not asked, for the reason the routing table does not route to it.
    /// </summary>
    public static bool MissesDeclaration(RequestHandlerModel handler) =>
        handler.ResponseInformation.MissingContentTypeDiagnostic && !handler.CannotBeEmitted();

    /// <summary>Reports a handler that answers with bytes and declares no media type.</summary>
    public static void ReportMissingDeclaration(
        SourceProductionContext context,
        RequestHandlerModel handler,
        IMethodSymbol? method
    )
    {
        if (!MissesDeclaration(handler))
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                MissingDeclaration(),
                HandlerDeclaration.Of(method),
                handler.ControllerType.Name + "." + handler.HandlerMethod
            )
        );
    }

    /// <summary>
    /// Reports the declared media types nothing in reach writes, if any survive what this
    /// compilation can write.
    /// </summary>
    /// <param name="unproducible">
    /// The declared media types the transform could not rule producible from the return type alone,
    /// comma-joined.
    /// </param>
    /// <param name="writable">
    /// The media types a serializer in reach declares, comma-joined - see
    /// <see cref="SerializerContentTypes"/>. A candidate named here is produced and is not reported.
    /// </param>
    /// <param name="described">
    /// Whether a contract declared the media types, which only changes what the warning names as
    /// their source.
    /// </param>
    public static void Report(
        SourceProductionContext context,
        string handler,
        string? unproducible,
        string writable,
        bool described = false
    )
    {
        if (string.IsNullOrEmpty(unproducible))
        {
            return;
        }

        foreach (var contentType in unproducible!.Split(','))
        {
            if (SerializerContentTypes.Writes(writable, contentType))
            {
                continue;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    described ? NothingProducesDescribed() : NothingProduces(),
                    Location.None,
                    handler,
                    contentType
                )
            );
        }
    }
}
