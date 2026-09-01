using System.Collections.Generic;
using System.Linq;
using System.Text;
using CSharpAuthor;
using Hardened.Generation.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
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
        EntryPointSelector.Model appModel, IReadOnlyList<RequestHandlerModel> handlers, string basePath,
        OpenApiVersion version = OpenApiVersionFacts.Default, DocumentIdentity? identity = null) {
        var builder = new StringBuilder();

        // Identity, in preference order: the contract's own (specification-first), an
        // [OpenApiInfo] on the entry point (code-first), then the fallbacks every application got
        // before either existed - the entry point's class name and "1.0.0". The fallbacks renamed
        // an API its author had titled, which is why the first two exist.
        var (declaredTitle, declaredVersion, declaredDescription) = InfoAttribute(appModel);

        var title = identity?.Title ?? declaredTitle ?? appModel.EntryPointType.Name;
        var infoVersion = identity?.Version ?? declaredVersion ?? "1.0.0";
        var description = identity?.Description ?? declaredDescription;

        builder.Append("{\"openapi\":\"")
            .Append(OpenApiVersionFacts.VersionString(version))
            .Append("\",\"info\":{\"title\":\"")
            .Append(JsonSchemaWriter.Escape(title))
            .Append("\",\"version\":\"")
            .Append(JsonSchemaWriter.Escape(infoVersion))
            .Append('"');

        WriteText(builder, "description", description);

        builder.Append('}');

        WriteServers(builder, appModel);
        WriteTags(builder, handlers);

        builder.Append(",\"paths\":{");

        var components = new SortedDictionary<string, string>(System.StringComparer.Ordinal);
        var operationIds = OperationIds(handlers);

        // The wire vocabulary of every enum the application serializes, keyed as emitted code
        // names the type. Collected once: a parameter whose C# type is one of these is an enum
        // the name switch below cannot recognise, and its vocabulary belongs in the document
        // exactly as it does when the same enum sits in a body schema.
        var enums = new Dictionary<string, EnumVocabulary>(System.StringComparer.Ordinal);

        foreach (var vocabulary in EnumVocabularies.Collect(handlers)) {
            enums[vocabulary.QualifiedName] = vocabulary;
        }

        // Grouped by path, because a document keys operations under one path entry rather than
        // repeating the path per verb.
        // Grouped by the template, which is what the document keys on - not by the route, which is
        // what the router keys on. The two differ wherever a token carries a constraint: ToTemplate
        // strips it, so /pets/{petId:guid} and /pets/{petId} are one path item in a document and two
        // routes in a table. Grouping on the route emitted the same key twice, and every parser
        // keeps the last - so a GET declared beside a constrained DELETE vanished from the document
        // while continuing to serve.
        var byPath = handlers
            .GroupBy(handler => ToTemplate(RoutePath.Combine(basePath, handler.Name.Path)))
            .OrderBy(group => group.Key, System.StringComparer.Ordinal);

        var firstPath = true;

        foreach (var group in byPath) {
            if (!firstPath) {
                builder.Append(',');
            }

            builder.Append('"').Append(JsonSchemaWriter.Escape(group.Key)).Append("\":{");

            var firstOperation = true;

            foreach (var handler in group.OrderBy(h => h.Name.Method, System.StringComparer.Ordinal)) {
                if (!firstOperation) {
                    builder.Append(',');
                }

                WriteOperation(builder, handler, components, operationIds, version, enums);

                firstOperation = false;
            }

            builder.Append('}');

            firstPath = false;
        }

        builder.Append('}');

        // The identity's schemes (a contract's declarations) unioned with the ones handlers
        // declare by naming them - [Authorize<TAuth>]'s whole premise is that usage is
        // declaration. Identity wins a name collision, because a contract's spelling is the one
        // reviewed.
        var securitySchemes = new List<(string Name, string Json)>(
            identity?.SecuritySchemes ?? (IReadOnlyList<(string, string)>)System.Array.Empty<(string, string)>());

        foreach (var handler in handlers) {
            foreach (var declared in handler.DeclaredSecuritySchemes) {
                if (!securitySchemes.Exists(existing => existing.Name == declared.Name)) {
                    securitySchemes.Add((declared.Name, declared.Json));
                }
            }
        }

        securitySchemes.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));

        if (components.Count > 0 || securitySchemes.Count > 0) {
            builder.Append(",\"components\":{");

            if (components.Count > 0) {
                builder.Append("\"schemas\":{");

                var firstComponent = true;

                foreach (var component in components) {
                    if (!firstComponent) {
                        builder.Append(',');
                    }

                    builder.Append('"').Append(JsonSchemaWriter.Escape(component.Key)).Append("\":")
                        .Append(component.Value);

                    firstComponent = false;
                }

                builder.Append('}');
            }

            if (securitySchemes.Count > 0) {
                if (components.Count > 0) {
                    builder.Append(',');
                }

                builder.Append("\"securitySchemes\":{");

                for (var i = 0; i < securitySchemes.Count; i++) {
                    if (i > 0) {
                        builder.Append(',');
                    }

                    builder.Append('"').Append(JsonSchemaWriter.Escape(securitySchemes[i].Name))
                        .Append("\":").Append(securitySchemes[i].Json);
                }

                builder.Append('}');
            }

            builder.Append('}');
        }

        return builder.Append('}').ToString();
    }

    private static void WriteOperation(
        StringBuilder builder,
        RequestHandlerModel handler,
        SortedDictionary<string, string> components,
        IReadOnlyDictionary<string, string> operationIds,
        OpenApiVersion version,
        IReadOnlyDictionary<string, EnumVocabulary> enums) {
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

        if (handler.SecurityRequirements.Count > 0) {
            builder.Append(",\"security\":[");

            for (var i = 0; i < handler.SecurityRequirements.Count; i++) {
                if (i > 0) {
                    builder.Append(',');
                }

                builder.Append(handler.SecurityRequirements[i]);
            }

            builder.Append(']');
        }

        WriteParameters(builder, handler, version, enums);
        WriteRequestBody(builder, handler, components);
        WriteResponses(builder, handler, components, version);

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
    /// <summary>
    /// The <c>[OpenApiInfo("title", "version")]</c> an entry point declares, read the way
    /// <see cref="WriteServers"/> reads <c>[Server]</c>: off the attribute list, by name prefix,
    /// arguments as source text with their quotes trimmed.
    /// </summary>
    private static (string? Title, string? Version, string? Description) InfoAttribute(
        EntryPointSelector.Model appModel) {
        if (appModel.AttributeModels == null) {
            return (null, null, null);
        }

        foreach (var attribute in appModel.AttributeModels) {
            if (!attribute.TypeDefinition.Name.StartsWith("OpenApiInfo", System.StringComparison.Ordinal)) {
                continue;
            }

            var parts = attribute.Arguments.Split(',');

            var title = parts.Length > 0 ? parts[0].Trim().Trim('"') : "";
            var infoVersion = parts.Length > 1 ? parts[1].Trim().Trim('"') : "";
            var description = parts.Length > 2 ? parts[2].Trim().Trim('"') : "";

            return (
                title.Length > 0 ? title : null,
                infoVersion.Length > 0 ? infoVersion : null,
                description.Length > 0 ? description : null);
        }

        return (null, null, null);
    }

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

    /// <summary>
    /// The document's own <c>tags</c> list, declaring every group its operations reference.
    /// </summary>
    /// <remarks>
    /// Each operation already carried a tag; nothing declared them. That is legal and it is lossy:
    /// the top-level list is where a tag gets a description and, more practically, where its order
    /// is set — a reader that finds tags only on operations shows them alphabetically, so the
    /// grouping a client's documentation and generated SDK present is whatever the names sort to
    /// rather than what the application declared. Emitted in the order the handlers do, which is
    /// the order the routing table was built in.
    /// </remarks>
    private static void WriteTags(StringBuilder builder, IReadOnlyList<RequestHandlerModel> handlers) {
        var seen = new List<string>();

        foreach (var handler in handlers) {
            var tag = Tag(handler);

            if (!seen.Contains(tag)) {
                seen.Add(tag);
            }
        }

        if (seen.Count == 0) {
            return;
        }

        builder.Append(",\"tags\":[");

        for (var i = 0; i < seen.Count; i++) {
            if (i > 0) {
                builder.Append(',');
            }

            builder.Append("{\"name\":\"").Append(JsonSchemaWriter.Escape(seen[i])).Append("\"}");
        }

        builder.Append(']');
    }

    private static void WriteParameters(
        StringBuilder builder, RequestHandlerModel handler, OpenApiVersion version,
        IReadOnlyDictionary<string, EnumVocabulary> enums) {
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

            // A parameter carrying a default is one the caller may omit - the binder answers
            // with the default rather than a 400 - so publishing it required documents a demand
            // the service does not make. Path parameters stay required whatever they carry,
            // because OpenAPI requires it of them and a path segment cannot be absent.
            var required = parameter.Required &&
                           (parameter.DefaultValue == null ||
                            parameter.BindingType == ParameterBindType.Path);

            builder.Append("{\"name\":\"").Append(JsonSchemaWriter.Escape(name))
                .Append("\",\"in\":\"").Append(Location(parameter.BindingType))
                .Append("\",\"required\":").Append(required ? "true" : "false");

            WriteText(builder, "description", parameter.Description);

            builder.Append("")
                .Append(",\"schema\":").Append(ParameterSchema(parameter, version, enums))
                .Append('}');
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

    /// <summary>
    /// The operation's <c>responses</c>, which is every status it can answer with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This wrote a single hardcoded <c>"200"</c> for every operation until response sets existed,
    /// and both halves of that were wrong. Any other status the handler could answer with was
    /// absent, so a client generated from the document had no branch for a 404 the handler returns
    /// on every miss. And the success status itself was not read from the model, so a handler
    /// declaring <c>[Post(SuccessStatus = 201)]</c> published a contract promising 200 - a
    /// mismatch a conditional-request or a create-then-poll client acts on.
    /// </para>
    /// <para>
    /// The success status now comes from <c>DefaultStatusCode</c>, which is where a description's
    /// <c>responses:</c> key and <c>[Post(SuccessStatus = 201)]</c> both already land. One field, so
    /// the two front ends state the same thing.
    /// </para>
    /// </remarks>
    private static void WriteResponses(
        StringBuilder builder, RequestHandlerModel handler, SortedDictionary<string, string> components,
        OpenApiVersion version) {
        var successStatus = handler.ResponseInformation.DefaultStatusCode ?? 200;

        builder.Append(",\"responses\":{");

        // Which declaration produced the set decides whether it is the whole of it. A Response or
        // union return type names every status the handler answers, success included - and that
        // success need not be 200: Response<NoContent, NotFound> declares 204 and 404 and nothing
        // else. [Throws<T>] names only failures, and the success still comes from the return type.
        //
        // Asking instead whether the declared set happens to contain the default success status
        // gets the union case wrong, and writes a 200 beside a 204 for a handler that answers one
        // of them.
        var returnTypeDeclaredThem = handler.DeclaredResponsesAreComplete;

        if (handler.ResponseSchemas.Count == 0) {
            WriteSingleResponse(builder, handler, components, version, successStatus);
        }
        else if (returnTypeDeclaredThem) {
            WriteDeclaredResponses(builder, handler, components);
        }
        else {
            WriteSingleResponse(builder, handler, components, version, successStatus);
            builder.Append(',');
            WriteDeclaredResponses(builder, handler, components);
        }

        WriteValidationResponse(builder, handler, components);

        builder.Append('}');
    }

    /// <summary>
    /// A handler that returns one type: the status it succeeds with, and the body it sends.
    /// </summary>
    private static void WriteSingleResponse(
        StringBuilder builder, RequestHandlerModel handler, SortedDictionary<string, string> components,
        OpenApiVersion version, int successStatus) {
        builder.Append('"').Append(successStatus).Append("\":{\"description\":\"")
            .Append(JsonSchemaWriter.Escape(HttpResponseDescription.For(successStatus)))
            .Append('"');

        if (handler.ResponseInformation.IsAsyncEnumerable) {
            WriteStreamedResponse(builder, handler, components, version);
        }
        else if (handler.ResponseSchema != null) {
            Merge(components, handler.ResponseSchema);

            builder.Append(",\"content\":{\"").Append(JsonSchemaWriter.Escape(ContentType(handler)))
                .Append("\":{\"schema\":").Append(handler.ResponseSchema.Schema).Append("}}");
        }

        builder.Append('}');
    }

    /// <summary>
    /// A handler whose return type declares a set of responses: one entry per status it can answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Grouped by status and written in status order, ordinally, because a document is diffed
    /// against the last one as often as it is read - and an operation whose responses moved for no
    /// reason is a diff a reviewer has to work out is empty.
    /// </para>
    /// <para>
    /// Two cases sharing one status become a <c>oneOf</c> rather than the last one silently winning.
    /// That is a real declaration and not a mistake: it is two shapes under one status, which is
    /// what a caller writing <c>Response&lt;Todo, Archived&gt;</c> for a 200 means.
    /// </para>
    /// </remarks>
    private static void WriteDeclaredResponses(
        StringBuilder builder, RequestHandlerModel handler, SortedDictionary<string, string> components) {
        var byStatus = handler.ResponseSchemas
            .GroupBy(response => response.Status)
            .OrderBy(group => group.Key);

        var successContentType = ContentType(handler);
        var first = true;

        foreach (var group in byStatus) {
            if (!first) {
                builder.Append(',');
            }

            builder.Append('"').Append(group.Key).Append("\":{\"description\":\"")
                .Append(JsonSchemaWriter.Escape(group.First().Description))
                .Append('"');

            WriteResponseHeaders(builder, group);

            // The handler's media type describes its success. An error body is written by the
            // exception path, which serializes JSON whatever the success was - publishing the
            // error under the raw media type described a body that path cannot produce.
            var contentType = group.Key >= 400 ? "application/json" : successContentType;

            var bodies = group.Where(response => response.Schema != null).ToList();

            if (bodies.Count > 0) {
                builder.Append(",\"content\":{\"").Append(JsonSchemaWriter.Escape(contentType))
                    .Append("\":{\"schema\":");

                if (bodies.Count == 1) {
                    Merge(components, bodies[0].Schema!);
                    builder.Append(bodies[0].Schema!.Schema);
                }
                else {
                    builder.Append("{\"oneOf\":[");

                    for (var i = 0; i < bodies.Count; i++) {
                        if (i > 0) {
                            builder.Append(',');
                        }

                        Merge(components, bodies[i].Schema!);
                        builder.Append(bodies[i].Schema!.Schema);
                    }

                    builder.Append("]}");
                }

                builder.Append("}}");
            }

            builder.Append('}');

            first = false;
        }
    }

    /// <summary>
    /// The media type the operation answers with, which is JSON unless it committed to another.
    /// </summary>
    /// <summary>
    /// The <c>headers</c> a response declares, merged across the status's cases by wire name.
    /// </summary>
    /// <remarks>
    /// The declaration only: the value is the handler's, exactly as the generated case type's
    /// constructor divides them. Schema stays <c>string</c> for the reason
    /// <c>ResponseHeaderModel</c> gives - a header is a string on the wire whatever it carries.
    /// </remarks>
    private static void WriteResponseHeaders(
        StringBuilder builder, IEnumerable<ResponseSchemaModel> responses) {
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var first = true;

        foreach (var response in responses) {
            foreach (var header in response.Headers) {
                if (!seen.Add(header.Name)) {
                    continue;
                }

                builder.Append(first ? ",\"headers\":{" : ",");

                builder.Append('"').Append(JsonSchemaWriter.Escape(header.Name)).Append("\":{");

                if (!string.IsNullOrEmpty(header.Description)) {
                    builder.Append("\"description\":\"")
                        .Append(JsonSchemaWriter.Escape(header.Description!)).Append("\",");
                }

                builder.Append("\"schema\":{\"type\":\"string\"}}");

                first = false;
            }
        }

        if (!first) {
            builder.Append('}');
        }
    }

    /// <summary>The schema of the framework's validation 400, written once into components.</summary>
    private const string ValidationErrorSchema =
        "{\"type\":\"object\"," +
        "\"description\":\"How a request that failed validation is answered.\"," +
        "\"required\":[\"type\",\"message\",\"errors\"]," +
        "\"properties\":{" +
        "\"type\":{\"type\":\"string\"}," +
        "\"message\":{\"type\":\"string\"}," +
        "\"errors\":{\"type\":\"array\",\"items\":{" +
        "\"type\":\"object\"," +
        "\"required\":[\"field\",\"code\",\"message\"]," +
        "\"properties\":{" +
        "\"field\":{\"type\":\"string\"}," +
        "\"code\":{\"type\":\"string\"}," +
        "\"message\":{\"type\":\"string\"}}}}}}";

    /// <summary>
    /// The 400 every operation with a generated validator can answer, declared rather than
    /// implied.
    /// </summary>
    /// <remarks>
    /// A constraint failure never reaches the handler - the generated filter answers 400 with
    /// <c>RequestValidationError</c> - so the status is a fact about the operation the contract
    /// nowhere states and the document never carried. Skipped where the operation declared its own
    /// 400, whose description then wins.
    /// </remarks>
    private static void WriteValidationResponse(
        StringBuilder builder, RequestHandlerModel handler,
        SortedDictionary<string, string> components) {
        if (handler.ParametersValidator == null && !handler.HasGeneratedValidation) {
            return;
        }

        foreach (var response in handler.ResponseSchemas) {
            if (response.Status == 400) {
                return;
            }
        }

        if ((handler.ResponseInformation.DefaultStatusCode ?? 200) == 400) {
            return;
        }

        components["RequestValidationError"] = ValidationErrorSchema;

        builder.Append(",\"400\":{\"description\":\"The request failed validation.\"," +
                       "\"content\":{\"application/json\":{\"schema\":" +
                       "{\"$ref\":\"#/components/schemas/RequestValidationError\"}}}}");
    }

    /// <summary>
    /// The media type a success goes out as: <c>[RawResponse]</c>'s, else the contract's declared
    /// one, else JSON. The declared type never reached here, so a <c>text/plain</c> contract
    /// published its success under a JSON key or under nothing.
    /// </summary>
    private static string ContentType(RequestHandlerModel handler) =>
        !string.IsNullOrEmpty(handler.ResponseInformation.RawResponseContentType)
            ? handler.ResponseInformation.RawResponseContentType!
            : !string.IsNullOrEmpty(handler.ResponseInformation.DeclaredContentType)
                ? handler.ResponseInformation.DeclaredContentType!
                : "application/json";

    /// <summary>
    /// A streamed response: the media type it is framed as, and the shape of one item.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>itemSchema</c> rather than <c>schema</c>, which is the distinction OpenAPI 3.2 added for
    /// exactly this: <c>schema</c> describes the whole body, and the body here is many documents one
    /// after another. Putting the item under <c>schema</c> - which is what this emitted before -
    /// tells a client the response is a single one of them, and a generator built from that document
    /// produces a client that reads one and stops.
    /// </para>
    /// <para>
    /// Below 3.2 the media type is written and the schema is not. That says "a body of this type,
    /// contents unspecified", which is true where the alternative is false; the handler is named in
    /// a build warning by <c>RoutingTableGenerator</c> so the omission is not silent.
    /// </para>
    /// </remarks>
    private static void WriteStreamedResponse(
        StringBuilder builder, RequestHandlerModel handler, SortedDictionary<string, string> components,
        OpenApiVersion version) {
        var contentType = StreamFramingNames.ContentType(handler.ResponseInformation.StreamFraming);

        builder.Append(",\"content\":{\"").Append(JsonSchemaWriter.Escape(contentType)).Append("\":{");

        if (handler.ResponseSchema != null && OpenApiVersionFacts.SupportsItemSchema(version)) {
            Merge(components, handler.ResponseSchema);

            builder.Append("\"itemSchema\":").Append(handler.ResponseSchema.Schema);
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
    /// <para>
    /// The emitter wrote no tags at all. Specification-first groups by
    /// <c>operation.Tags?.FirstOrDefault()?.Name ?? "Default"</c> and turns the tag into an
    /// interface name, so round-tripping an attribute-routed application collapsed every operation
    /// into one <c>IDefaultService</c> and destroyed the controller structure. No new grouping
    /// construct was needed for that - the controller already is the group, and the document simply
    /// did not say so.
    /// </para>
    /// <para>
    /// Shared with the links generator, which has to name the same group the same way or a route
    /// name would change meaning when the document round-trips.
    /// </para>
    /// </remarks>
    private static string Tag(RequestHandlerModel handler) => HandlerGroup.Name(handler);

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
    private static string ToTemplate(string path) {
        if (path.IndexOf('{') < 0) {
            return path;
        }

        var builder = new StringBuilder(path.Length);
        var index = 0;

        while (index < path.Length) {
            var open = path.IndexOf('{', index);

            if (open < 0) {
                builder.Append(path, index, path.Length - index);
                break;
            }

            var close = path.IndexOf('}', open);

            if (close < 0) {
                builder.Append(path, index, path.Length - index);
                break;
            }

            builder.Append(path, index, open - index).Append('{');

            var start = open + 1;

            // The catch-all marker: how much of the path the token takes, which a document cannot
            // express.
            if (start < close && path[start] == '*') {
                start++;
            }

            // The constraint: what the token has to look like to match. A template expression is a
            // parameter name and nothing else, so ":int" is not a shorter spelling of a schema - it
            // is a syntax error that happens to parse. Left in, it made the name in the template
            // disagree with the name in "parameters", which Spectral reports as path-params and a
            // generated client turns into a request for /boards/%7BboardId:guid%7D.
            var name = path.IndexOf(':', start);
            var end = name >= 0 && name < close ? name : close;

            builder.Append(path, start, end - start).Append('}');

            index = close + 1;
        }

        return builder.ToString();
    }

    /// <summary>
    /// The vocabulary schema for a code-first enum parameter, or null when the type is not one.
    /// </summary>
    /// <remarks>
    /// An attribute-routed application has no declaration to carry the vocabulary, but it does
    /// have the vocabulary itself: the same collected set the wire converters are generated from.
    /// Consulting it is what keeps a parameter's <c>enum</c> array agreeing with what the binder
    /// accepts - the fourth of the four places <c>EnumWireNaming</c>'s remarks require to agree.
    /// </remarks>
    private static string? EnumSchema(
        ITypeDefinition type, IReadOnlyDictionary<string, EnumVocabulary> enums) {
        var unwrapped = type.Name == "Nullable" && type.TypeArguments.Count == 1
            ? type.TypeArguments[0]
            : type;

        var qualified = "global::" + unwrapped.Namespace + "." + unwrapped.Name.TrimEnd('?');

        if (!enums.TryGetValue(qualified, out var vocabulary)) {
            return null;
        }

        var builder = new StringBuilder("{\"type\":\"string\",\"enum\":[");

        for (var i = 0; i < vocabulary.Values.Count; i++) {
            if (i > 0) {
                builder.Append(',');
            }

            builder.Append('"').Append(JsonSchemaWriter.Escape(vocabulary.Values[i].Wire)).Append('"');
        }

        return builder.Append("]}").ToString();
    }

    /// <summary>
    /// The schema for a value that arrived as text — a path token, a query value, a header, a cookie.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every one of these was written as <c>{"type":"string"}</c> whatever the handler declared, so
    /// a document described <c>Double(int count)</c> as taking a string. A generated client then has
    /// no reason to reject <c>/double/abc</c> before sending it, and no way to know the value is
    /// numeric — which is most of what a typed client is for.
    /// </para>
    /// <para>
    /// Matched by name rather than by symbol because there is no symbol left. Schemas that need one
    /// are captured during the syntax transform and carried on the model; these do not, since a
    /// value parsed from a string is a scalar by construction. Anything unrecognised stays a string,
    /// which is what it arrived as.
    /// </para>
    /// <para>
    /// <b>Known gap.</b> A nullable scalar - <c>int?</c> on a query value or header - still
    /// describes as a string. The type reaches here as a <c>Nullable</c> definition carrying no
    /// type argument, so the underlying type is not recoverable from the model as it stands;
    /// recovering it means changing what the syntax transform records, which is a wider change than
    /// this. Not a regression - every parameter described as a string before - and the value does
    /// arrive as text, so the schema is unspecific rather than wrong.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The schema for a bound value: the contract's declaration when the handler came from one,
    /// <see cref="ScalarSchema"/>'s reading of the C# type when it did not.
    /// </summary>
    /// <remarks>
    /// The declaration wins because it is the only one of the two that knows anything. A described
    /// parameter's wire type, format, enum vocabulary, bounds and default all survive to the
    /// handler model now; deriving the schema from the C# type instead is how every parameter came
    /// to be published as <c>{"type":"string"}</c> - the enum fell through the name switch, and a
    /// nullable scalar arrives as a <c>Nullable</c> with no argument to read.
    /// </remarks>
    private static string ParameterSchema(
        RequestParameterInformation parameter, OpenApiVersion version,
        IReadOnlyDictionary<string, EnumVocabulary> enums) {
        var spec = parameter.SpecParameter;

        if (spec == null || (string.IsNullOrEmpty(spec.Type) &&
                             spec.EnumValues is not { Count: > 0 } &&
                             !spec.IsArray)) {
            return EnumSchema(parameter.ParameterType, enums) ?? ScalarSchema(parameter.ParameterType);
        }

        var builder = new StringBuilder();

        builder.Append('{');

        if (spec.IsArray) {
            builder.Append("\"type\":\"array\"");

            if (spec.MinItems.HasValue) {
                builder.Append(",\"minItems\":").Append(spec.MinItems.Value);
            }

            if (spec.MaxItems.HasValue) {
                builder.Append(",\"maxItems\":").Append(spec.MaxItems.Value);
            }

            builder.Append(",\"items\":");
            AppendScalarFacets(
                builder, spec.ArrayItemsType ?? "string", spec.Format, spec, version, openObject: true);
        } else {
            AppendScalarFacets(
                builder,
                string.IsNullOrEmpty(spec.Type) ? "string" : spec.Type!,
                spec.Format, spec, version, openObject: false);
        }

        builder.Append('}');

        return builder.ToString();
    }

    /// <summary>
    /// The scalar half of a declared schema: type, format, enum, bounds, pattern and default.
    /// </summary>
    /// <remarks>
    /// The exclusive bounds change spelling with the document version: 3.0 writes
    /// <c>"minimum": n, "exclusiveMinimum": true</c> and 3.1 aligned with JSON Schema 2020-12,
    /// where <c>exclusiveMinimum</c> is itself the number. Writing the boolean form into a 3.2
    /// document is the defect this replaces.
    /// </remarks>
    private static void AppendScalarFacets(
        StringBuilder builder, string type, string? format, IConstraintFacets spec,
        OpenApiVersion version, bool openObject) {
        if (openObject) {
            builder.Append('{');
        }

        builder.Append("\"type\":\"").Append(JsonSchemaWriter.Escape(type)).Append('"');

        if (!string.IsNullOrEmpty(format)) {
            builder.Append(",\"format\":\"").Append(JsonSchemaWriter.Escape(format!)).Append('"');
        }

        AppendConstraintFacets(builder, type, spec, version);

        if (openObject) {
            builder.Append('}');
        }
    }

    /// <summary>
    /// The constraint keywords alone, appended to a schema object someone else opened.
    /// </summary>
    /// <remarks>
    /// Shared with <c>SpecSchemaWriter</c>, which writes body schemas from the normalised model.
    /// Parameters travelled through here from the start, which is why the trial found every
    /// parameter constraint published and every body constraint dropped - two writers, one of
    /// which never learned these keywords.
    /// </remarks>
    internal static void AppendConstraintFacets(
        StringBuilder builder, string type, IConstraintFacets spec, OpenApiVersion version) {
        if (spec.EnumValues is { Count: > 0 }) {
            builder.Append(",\"enum\":[");

            for (var i = 0; i < spec.EnumValues.Count; i++) {
                if (i > 0) {
                    builder.Append(',');
                }

                builder.Append('"').Append(JsonSchemaWriter.Escape(spec.EnumValues[i])).Append('"');
            }

            builder.Append(']');
        }

        if (spec.Minimum.HasValue) {
            builder.Append(version == OpenApiVersion.V3_0
                    ? ",\"minimum\":"
                    : spec.ExclusiveMinimum ? ",\"exclusiveMinimum\":" : ",\"minimum\":")
                .Append(Number(spec.Minimum.Value));

            if (version == OpenApiVersion.V3_0 && spec.ExclusiveMinimum) {
                builder.Append(",\"exclusiveMinimum\":true");
            }
        }

        if (spec.Maximum.HasValue) {
            builder.Append(version == OpenApiVersion.V3_0
                    ? ",\"maximum\":"
                    : spec.ExclusiveMaximum ? ",\"exclusiveMaximum\":" : ",\"maximum\":")
                .Append(Number(spec.Maximum.Value));

            if (version == OpenApiVersion.V3_0 && spec.ExclusiveMaximum) {
                builder.Append(",\"exclusiveMaximum\":true");
            }
        }

        if (spec.MinLength.HasValue) {
            builder.Append(",\"minLength\":").Append(spec.MinLength.Value);
        }

        if (spec.MaxLength.HasValue) {
            builder.Append(",\"maxLength\":").Append(spec.MaxLength.Value);
        }

        if (!string.IsNullOrEmpty(spec.Pattern)) {
            builder.Append(",\"pattern\":\"").Append(JsonSchemaWriter.Escape(spec.Pattern!)).Append('"');
        }

        if (!string.IsNullOrEmpty(spec.Default)) {
            builder.Append(",\"default\":").Append(DefaultLiteralJson(type, spec.Default!));
        }
    }

    /// <summary>A decimal as JSON, which never means the culture's decimal separator.</summary>
    private static string Number(decimal value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The declared default as a JSON literal of the schema's type, quoted only when the type is
    /// textual. A default that does not parse as its declared type is written as a string rather
    /// than invalidating the document over it.
    /// </summary>
    private static string DefaultLiteralJson(string type, string value) {
        switch (type) {
            case "integer":
            case "number":
                return decimal.TryParse(
                    value, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var number)
                    ? Number(number)
                    : "\"" + JsonSchemaWriter.Escape(value) + "\"";
            case "boolean" when value is "true" or "false":
                return value;
            default:
                return "\"" + JsonSchemaWriter.Escape(value) + "\"";
        }
    }

    private static string ScalarSchema(ITypeDefinition type) {
        // int? and friends: the schema describes the value, and "required" already says whether one
        // has to be there. Two spellings reach here - Nullable<T> with a type argument, and the
        // underlying name with a '?' on it, depending on how the parameter was written - so both
        // are unwrapped rather than guessing which the generator produced.
        var name = (type.Name == "Nullable" && type.TypeArguments.Count == 1
            ? type.TypeArguments[0].Name
            : type.Name).TrimEnd('?');

        return name switch {
            "String" or "Char" => "{\"type\":\"string\"}",
            "Boolean" => "{\"type\":\"boolean\"}",
            "Byte" or "SByte" or "Int16" or "UInt16" or "Int32" or "UInt32" =>
                "{\"type\":\"integer\",\"format\":\"int32\"}",
            "Int64" or "UInt64" => "{\"type\":\"integer\",\"format\":\"int64\"}",
            "Single" => "{\"type\":\"number\",\"format\":\"float\"}",
            "Double" => "{\"type\":\"number\",\"format\":\"double\"}",
            "Decimal" => "{\"type\":\"number\"}",
            "DateTime" or "DateTimeOffset" => "{\"type\":\"string\",\"format\":\"date-time\"}",
            "DateOnly" => "{\"type\":\"string\",\"format\":\"date\"}",
            "Guid" => "{\"type\":\"string\",\"format\":\"uuid\"}",
            "Uri" => "{\"type\":\"string\",\"format\":\"uri\"}",
            _ => "{\"type\":\"string\"}"
        };
    }

    private static string? Location(ParameterBindType bindType) =>
        bindType switch {
            ParameterBindType.Path => "path",
            ParameterBindType.QueryString => "query",
            ParameterBindType.Header => "header",
            ParameterBindType.Cookie => "cookie",
            _ => null
        };
}
