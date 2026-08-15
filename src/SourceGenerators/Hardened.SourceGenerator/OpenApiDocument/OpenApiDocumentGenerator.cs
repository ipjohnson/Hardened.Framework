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
            .Append("\",\"version\":\"1.0.0\"}");

        WriteServers(builder, appModel);

        builder.Append(",\"paths\":{");

        var components = new SortedDictionary<string, string>(System.StringComparer.Ordinal);
        var operationIds = OperationIds(handlers);

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

                WriteOperation(builder, handler, components, operationIds);

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
        StringBuilder builder,
        RequestHandlerModel handler,
        SortedDictionary<string, string> components,
        IReadOnlyDictionary<string, string> operationIds) {
        builder.Append('"').Append(handler.Name.Method.ToLowerInvariant()).Append("\":{");

        builder.Append("\"tags\":[\"")
            .Append(JsonSchemaWriter.Escape(Tag(handler)))
            .Append("\"],");

        builder.Append("\"operationId\":\"")
            .Append(JsonSchemaWriter.Escape(operationIds[HandlerKey(handler)]))
            .Append('"');

        WriteText(builder, "summary", handler.Summary);
        WriteText(builder, "description", handler.Description);

        if (handler.IsDeprecated) {
            builder.Append(",\"deprecated\":true");
        }

        WriteParameters(builder, handler);
        WriteRequestBody(builder, handler, components);
        WriteResponses(builder, handler, components);

        builder.Append('}');
    }

    private static void WriteText(StringBuilder builder, string field, string? value) {
        if (string.IsNullOrEmpty(value)) {
            return;
        }

        builder.Append(",\"").Append(field).Append("\":\"")
            .Append(JsonSchemaWriter.Escape(value!)).Append('"');
    }

    /// <summary>
    /// The <c>servers</c> the entry point declares with <c>[Server]</c>. Omitted entirely when
    /// there are none - an empty <c>servers</c> array is not the same as saying nothing, since a
    /// reader treats the absent case as "the document's own location" and an empty one as an
    /// application served from nowhere.
    /// </summary>
    private static void WriteServers(StringBuilder builder, EntryPointSelector.Model appModel) {
        if (appModel.AttributeModels == null) {
            return;
        }

        var first = true;

        foreach (var attribute in appModel.AttributeModels) {
            if (!attribute.TypeDefinition.Name.StartsWith("Server", System.StringComparison.Ordinal)) {
                continue;
            }

            // "url", "description" - split on the first comma only, because a URL may contain one
            // and the description is whatever remains.
            var arguments = attribute.Arguments;
            var comma = arguments.IndexOf(',');

            var url = (comma < 0 ? arguments : arguments.Substring(0, comma)).Trim().Trim('"');

            if (url.Length == 0) {
                continue;
            }

            builder.Append(first ? ",\"servers\":[" : ",");

            builder.Append("{\"url\":\"").Append(JsonSchemaWriter.Escape(url)).Append('"');

            if (comma >= 0) {
                var description = arguments.Substring(comma + 1).Trim().Trim('"');

                if (description.Length > 0) {
                    builder.Append(",\"description\":\"")
                        .Append(JsonSchemaWriter.Escape(description)).Append('"');
                }
            }

            builder.Append('}');

            first = false;
        }

        if (!first) {
            builder.Append(']');
        }
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
    /// The <c>operationId</c> for every handler, keyed by <see cref="HandlerKey"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>operationId</c> MUST be unique across the document. The previous derivation built one
    /// from the verb and the route's literal segments, skipping tokens - so <c>/verbs/item</c> and
    /// <c>/verbs/item/{id}</c> both produced <c>getVerbsItem</c>. Verified on the WebApp fixture:
    /// 42 operations, with <c>getBindingPath</c> and <c>deleteVerbsItem</c> each emitted twice.
    /// That document is invalid, and a client generator fed it either fails or silently drops an
    /// operation. <c>OpenApiRoundTripTests</c> passed throughout, because Microsoft.OpenApi is a
    /// lenient reader - it proves a parser accepts the output, not that the output is valid.
    /// </para>
    /// <para>
    /// The C# method name cannot collide within a class, and reads far better in a generated
    /// client than a path-derived name does. Where two controllers use the same method name the
    /// tag disambiguates, which is the one piece of information that distinguishes them - and it
    /// costs the original name only in the case where no single name could have served both.
    /// </para>
    /// <para>
    /// camelCase because that is what <c>NamingHelper.ToMethodName</c> reverses: it pascal-cases
    /// the id, so <c>getPet</c> comes back as <c>GetPet</c>. Round-tripping a document through the
    /// build task therefore recovers the method name it started as.
    /// </para>
    /// </remarks>
    private static IReadOnlyDictionary<string, string> OperationIds(
        IReadOnlyList<RequestHandlerModel> handlers) {
        var byName = new Dictionary<string, List<RequestHandlerModel>>(System.StringComparer.Ordinal);

        foreach (var handler in handlers) {
            var name = CamelCase(handler.HandlerMethod);

            if (!byName.TryGetValue(name, out var sharing)) {
                sharing = new List<RequestHandlerModel>();
                byName[name] = sharing;
            }

            sharing.Add(handler);
        }

        var ids = new Dictionary<string, string>(System.StringComparer.Ordinal);

        foreach (var pair in byName) {
            var contested = pair.Value.Count > 1;

            foreach (var handler in pair.Value) {
                ids[HandlerKey(handler)] = contested
                    ? CamelCase(Tag(handler)) + Pascal(handler.HandlerMethod)
                    : pair.Key;
            }
        }

        return ids;
    }

    /// <summary>
    /// Identifies one handler. The generated invoke class is unique per handler by construction -
    /// its name carries the controller, the method and a hash of the parameter names - which makes
    /// it a safer key than the model itself, whose equality is by value.
    /// </summary>
    private static string HandlerKey(RequestHandlerModel handler) =>
        handler.InvokeHandlerType.Namespace + "." + handler.InvokeHandlerType.Name;

    /// <summary>
    /// The group this operation documents under: what the controller declared with <c>[Tag]</c>,
    /// or its class name with a <c>Controller</c> suffix stripped.
    /// </summary>
    /// <remarks>
    /// The emitter wrote no tags at all. Specification-first groups by
    /// <c>operation.Tags?.FirstOrDefault()?.Name ?? "Default"</c> and turns the tag into an
    /// interface name, so round-tripping an attribute-routed application collapsed every operation
    /// into one <c>IDefaultService</c> and destroyed the controller structure. No new grouping
    /// construct was needed for that - the controller already is the group, and the document
    /// simply did not say so.
    /// </remarks>
    private static string Tag(RequestHandlerModel handler) {
        if (!string.IsNullOrEmpty(handler.Tag)) {
            return handler.Tag!;
        }

        var name = handler.ControllerType.Name;

        return name.Length > ControllerSuffix.Length &&
               name.EndsWith(ControllerSuffix, System.StringComparison.Ordinal)
            ? name.Substring(0, name.Length - ControllerSuffix.Length)
            : name;
    }

    private const string ControllerSuffix = "Controller";

    private static string CamelCase(string value) =>
        value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value.Substring(1);

    private static string Pascal(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value.Substring(1);

    /// <summary>
    /// The route as a document writes it.
    ///
    /// <para>
    /// Hardened's own form matches except for the catch-all marker: <c>/files/{*path}</c> is a
    /// Hardened route, and <c>{*path}</c> is not a valid OpenAPI path template — a template
    /// expression is a name, and the name has to match a declared parameter. The marker says how
    /// much of the path the token takes, which is a routing concern the document has no way to
    /// express, so it is dropped and the parameter is written under its own name.
    /// </para>
    ///
    /// <para>
    /// That does lose something: a specification round-tripped back through
    /// <c>Hardened.OpenApi.BuildTask</c> gives a single-segment token where the source route had a
    /// catch-all. Worth knowing, and better than emitting a document no OpenAPI reader accepts.
    /// </para>
    /// </summary>
    private static string ToTemplate(string path) =>
        path.IndexOf("{*", StringComparison.Ordinal) < 0
            ? path
            : path.Replace("{*", "{");

    private static string? Location(ParameterBindType bindType) =>
        bindType switch {
            ParameterBindType.Path => "path",
            ParameterBindType.QueryString => "query",
            ParameterBindType.Header => "header",
            ParameterBindType.Cookie => "cookie",
            _ => null
        };
}
