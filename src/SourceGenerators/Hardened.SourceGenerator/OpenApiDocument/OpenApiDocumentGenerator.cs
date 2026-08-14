using System.Collections.Generic;
using System.Linq;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.OpenApiDocument;

/// <summary>
/// An OpenAPI document describing the routes an application declares with attributes.
/// </summary>
/// <remarks>
/// <para>
/// The other direction. Everything else in this repository turns a document into code; this turns
/// code into a document, so an attribute-routed application can hand a client the same contract a
/// specification-first one starts from.
/// </para>
/// <para>
/// Written as a C# constant rather than a file. An analyzer is not permitted to touch the file
/// system - which is why the specification-first direction parses through an MSBuild task - and
/// that task runs before the compiler, so it cannot see routes. A constant is the one place a
/// generator can put this, and it is also what lets the document be served without reading anything
/// at run time.
/// </para>
/// <para>
/// The JSON is written by hand for the same reason: <c>Microsoft.OpenApi</c> can write a document,
/// but this assembly deliberately carries no dependencies, and taking one would undo what the
/// specification-first generator was restructured to achieve.
/// </para>
/// </remarks>
public static class OpenApiDocumentGenerator {

    /// <summary>
    /// The document for one entry point.
    /// </summary>
    public static string Write(
        EntryPointSelector.Model appModel, IReadOnlyList<RequestHandlerModel> handlers, string basePath) {
        var builder = new StringBuilder();

        builder.Append("{\"openapi\":\"3.0.0\",\"info\":{\"title\":\"")
            .Append(JsonSchemaWriter.Escape(appModel.EntryPointType.Name))
            .Append("\",\"version\":\"1.0.0\"},\"paths\":{");

        var components = new SortedDictionary<string, string>(System.StringComparer.Ordinal);

        // Grouped by path, because a document keys operations under one path entry rather than
        // repeating the path per verb.
        var byPath = handlers
            .GroupBy(handler => basePath + handler.Name.Path)
            .OrderBy(group => group.Key, System.StringComparer.Ordinal);

        var firstPath = true;

        foreach (var group in byPath) {
            if (!firstPath) {
                builder.Append(',');
            }

            builder.Append('"').Append(JsonSchemaWriter.Escape(ToTemplate(group.Key))).Append("\":{");

            var firstOperation = true;

            foreach (var handler in group.OrderBy(h => h.Name.Method, System.StringComparer.Ordinal)) {
                if (!firstOperation) {
                    builder.Append(',');
                }

                WriteOperation(builder, handler, components);

                firstOperation = false;
            }

            builder.Append('}');

            firstPath = false;
        }

        builder.Append('}');

        if (components.Count > 0) {
            builder.Append(",\"components\":{\"schemas\":{");

            var firstComponent = true;

            foreach (var component in components) {
                if (!firstComponent) {
                    builder.Append(',');
                }

                builder.Append('"').Append(JsonSchemaWriter.Escape(component.Key)).Append("\":")
                    .Append(component.Value);

                firstComponent = false;
            }

            builder.Append("}}");
        }

        return builder.Append('}').ToString();
    }

    private static void WriteOperation(
        StringBuilder builder, RequestHandlerModel handler, SortedDictionary<string, string> components) {
        builder.Append('"').Append(handler.Name.Method.ToLowerInvariant()).Append("\":{");

        builder.Append("\"operationId\":\"")
            .Append(JsonSchemaWriter.Escape(OperationId(handler)))
            .Append('"');

        WriteParameters(builder, handler);
        WriteRequestBody(builder, handler, components);
        WriteResponses(builder, handler, components);

        builder.Append('}');
    }

    private static void WriteParameters(StringBuilder builder, RequestHandlerModel handler) {
        var bound = handler.RequestParameterInformationList
            .Where(p => Location(p.BindingType) != null)
            .ToList();

        if (bound.Count == 0) {
            return;
        }

        builder.Append(",\"parameters\":[");

        for (var i = 0; i < bound.Count; i++) {
            var parameter = bound[i];

            if (i > 0) {
                builder.Append(',');
            }

            var name = string.IsNullOrEmpty(parameter.BindingName)
                ? parameter.Name
                : parameter.BindingName;

            builder.Append("{\"name\":\"").Append(JsonSchemaWriter.Escape(name))
                .Append("\",\"in\":\"").Append(Location(parameter.BindingType))
                .Append("\",\"required\":").Append(parameter.Required ? "true" : "false")
                .Append(",\"schema\":{\"type\":\"string\"}}");
        }

        builder.Append(']');
    }

    private static void WriteRequestBody(
        StringBuilder builder, RequestHandlerModel handler, SortedDictionary<string, string> components) {
        if (handler.RequestSchema == null) {
            return;
        }

        Merge(components, handler.RequestSchema);

        builder.Append(",\"requestBody\":{\"required\":true,\"content\":{\"application/json\":{\"schema\":")
            .Append(handler.RequestSchema.Schema)
            .Append("}}}");
    }

    private static void WriteResponses(
        StringBuilder builder, RequestHandlerModel handler, SortedDictionary<string, string> components) {
        builder.Append(",\"responses\":{\"200\":{\"description\":\"OK\"");

        if (handler.ResponseSchema != null) {
            Merge(components, handler.ResponseSchema);

            var contentType = string.IsNullOrEmpty(handler.ResponseInformation.RawResponseContentType)
                ? "application/json"
                : handler.ResponseInformation.RawResponseContentType!;

            builder.Append(",\"content\":{\"").Append(JsonSchemaWriter.Escape(contentType))
                .Append("\":{\"schema\":").Append(handler.ResponseSchema.Schema).Append("}}");
        }

        builder.Append("}}");
    }

    private static void Merge(SortedDictionary<string, string> components, HandlerSchema schema) {
        foreach (var component in schema.Components) {
            components[component.Name] = component.Json;
        }
    }

    /// <summary>
    /// A stable identifier for the operation, from the verb and the route - the same derivation the
    /// specification-first side uses when a document omits <c>operationId</c>.
    /// </summary>
    private static string OperationId(RequestHandlerModel handler) {
        var builder = new StringBuilder(handler.Name.Method.ToLowerInvariant());

        foreach (var segment in handler.Name.Path.Split('/')) {
            if (segment.Length == 0 || segment[0] == '{') {
                continue;
            }

            builder.Append(char.ToUpperInvariant(segment[0])).Append(segment.Substring(1));
        }

        return builder.ToString();
    }

    /// <summary>The route as a document writes it. Hardened's own form already matches.</summary>
    private static string ToTemplate(string path) => path;

    private static string? Location(ParameterBindType bindType) =>
        bindType switch {
            ParameterBindType.Path => "path",
            ParameterBindType.QueryString => "query",
            ParameterBindType.Header => "header",
            ParameterBindType.Cookie => "cookie",
            _ => null
        };
}
