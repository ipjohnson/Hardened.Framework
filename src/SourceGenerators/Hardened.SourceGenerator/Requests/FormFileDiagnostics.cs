using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// A file parameter bound from somewhere other than the form.
/// </summary>
/// <remarks>
/// <para>
/// <c>IFormFile</c> with no attribute is taken for a service, because it is an interface, and the
/// container has none to give. With <c>[FromBody]</c> the JSON deserializer is asked to build an
/// interface, and with <c>[FromQueryString]</c> or <c>[FromHeader]</c> the string converter is
/// asked to read one from text. All of them compile and refuse every request.
/// </para>
/// <para>
/// A custom binding attribute is left alone, because binding a file is exactly what one might be
/// written to do.
/// </para>
/// </remarks>
public static class FormFileDiagnostics
{
    public const string DiagnosticId = "HRDW008";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>FormAndBodyDiagnostics.Descriptor</c> is.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() =>
        new(
            id: DiagnosticId,
            title: "A file is bound from somewhere other than the form",
            messageFormat: "'{0}' binds '{1}', a file, from the {2}. A file only arrives as a part "
                + "of a multipart form, so bind it with [FromForm].",
            category: "Hardened.Web",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

    /// <summary>Every file parameter bound from a source that cannot carry one.</summary>
    public static IReadOnlyList<RequestParameterInformation> FindMisbound(
        RequestHandlerModel model
    ) =>
        model
            .RequestParameterInformationList.Where(parameter =>
                Source(parameter.BindingType) != null && FormFileType.Binds(parameter.ParameterType)
            )
            .ToList();

    public static void Report(SourceProductionContext context, RequestHandlerModel model)
    {
        foreach (var parameter in FindMisbound(model))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    Descriptor(),
                    Location.None,
                    model.ControllerType.Name + "." + model.HandlerMethod,
                    parameter.Name,
                    Source(parameter.BindingType)
                )
            );
        }
    }

    private static string? Source(ParameterBindType bindingType) =>
        bindingType switch
        {
            ParameterBindType.FromServiceProvider => "container",
            ParameterBindType.Body => "request body",
            ParameterBindType.QueryString => "query string",
            ParameterBindType.Header => "headers",
            ParameterBindType.Cookie => "cookies",
            ParameterBindType.Path => "path",
            _ => null,
        };
}
