using System.Collections.Generic;
using System.Linq;
using System.Text;
using CSharpAuthor;
using Hardened.Generation.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web.Routing;

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

        // Before anything reads a handler, because the entry point's rung says something about
        // every one of them. Both front ends arrive here, so a described application publishes what
        // its module declares on the same terms an attribute-routed one does.
        handlers = WithEntryPointRung(appModel, handlers);

        // And [ErrorBodies(Json)] narrows every refusal, before any of them is written. Here rather
        // than inside ErrorContentTypes because the rule is about the service and the handlers are
        // already rewritten once at this point - threading a flag down to two call sites would put
        // a whole-service answer in a per-handler argument.
        handlers = WithJsonErrorBodies(appModel, handlers);

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

        WriteServers(builder, appModel, identity);
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

        // Every vocabulary is a component, whether a body reached it or only a parameter did. A
        // parameter's schema refers to the component by name, and a parameter-only enum has no
        // body schema to carry it in. A body schema that reaches the same enum writes the same
        // component again, byte for byte.
        foreach (var vocabulary in enums.Values) {
            components[vocabulary.Name] = EnumComponent(vocabulary);
        }

        // Grouped by path, because a document keys operations under one path entry rather than
        // repeating the path per verb.
        // Grouped by the template, which is what the document keys on - not by the route, which is
        // what the router keys on. The two differ wherever a token carries a constraint:
        // RouteTemplate strips it, so /pets/{petId:guid} and /pets/{petId} are one path item in a
        // document and two routes in a table. Grouping on the route emitted the same key twice, and
        // every parser keeps the last - so a GET declared beside a constrained DELETE vanished from
        // the document while continuing to serve.
        var byPath = handlers
            .GroupBy(handler => RouteTemplate.NamesOnly(RoutePath.Combine(basePath, handler.Name.Path)))
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

    /// <summary>
    /// <paramref name="handlers"/> with what the entry point declares folded into each one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A filter declared on the entry point installs on every handler in the compilation that it
    /// applies to, so what it answers belongs on those operations. Read once for the application
    /// and narrowed per handler here, because whether a declaration reaches an operation depends on
    /// that operation's verb and on whether it streams - the same
    /// <c>Methods</c>/<c>NotWhenStreaming</c> narrowing a declaration on a controller gets.
    /// </para>
    /// <para>
    /// Copied rather than amended in place. A handler model is a Roslyn cache key and the same
    /// instance is handed to the routing table, so writing the document must not change what the
    /// table reads.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<RequestHandlerModel> WithEntryPointRung(
        EntryPointSelector.Model appModel, IReadOnlyList<RequestHandlerModel> handlers) {
        if (appModel.FilterFacts is not DeclaredOperationFacts facts || facts.IsEmpty) {
            return handlers;
        }

        var merged = new List<RequestHandlerModel>(handlers.Count);

        foreach (var handler in handlers) {
            merged.Add(Merged(
                handler,
                facts.For(handler.Name.Method, handler.ResponseInformation.IsAsyncEnumerable)));
        }

        return merged;
    }

    /// <summary>
    /// Every handler's refusals narrowed to JSON, where the entry point asked for that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The document has to say it or it is not true of the service: an operation declaring
    /// <c>application/x-msgpack</c> under this setting still answers its 404 as JSON, and a client
    /// generated from a document claiming otherwise reads the wrong thing.
    /// </para>
    /// <para>
    /// A description says the same with <c>x-hardened-error-bodies</c> at its root, and that
    /// arrives already applied - <c>SpecHandlerModelBuilder</c> writes it onto each operation's
    /// error content types, where a contract's own per-status media types are written too.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<RequestHandlerModel> WithJsonErrorBodies(
        EntryPointSelector.Model appModel, IReadOnlyList<RequestHandlerModel> handlers) {
        if (!DeclaresJsonErrorBodies(appModel)) {
            return handlers;
        }

        var narrowed = new List<RequestHandlerModel>(handlers.Count);

        foreach (var handler in handlers) {
            // A copy of both, because a handler model is shared with the rest of the pipeline and
            // its response information is a record whose members are settable - mutating either in
            // place would change what every other reader of it sees.
            narrowed.Add(handler.WithFilters(
                handler.Filters,
                handler.ResponseInformation with { ErrorContentTypes = Json }));
        }

        return narrowed;
    }

    /// <summary>Whether the entry point carries <c>[JsonErrorBodies]</c>.</summary>
    private static bool DeclaresJsonErrorBodies(EntryPointSelector.Model appModel) {
        foreach (var attribute in appModel.AttributeModels) {
            if (attribute.TypeDefinition.Name.StartsWith("JsonErrorBodies", StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    private static RequestHandlerModel Merged(
        RequestHandlerModel handler, OperationDeclarations reaching) {
        if (reaching.Refusals.Count == 0 && reaching.ResponseHeaders.Count == 0 &&
            reaching.RequestHeaders.Count == 0) {
            return handler;
        }

        // The rung's responses go last, so a status the handler declared itself keeps the shape the
        // handler gave it - the order Compose puts a declaration's own refusals in.
        var merged = handler.WithFilters(
            handler.Filters,
            responseSchemas: reaching.WithHeaders(
                handler.ResponseSchemas.Concat(reaching.Refusals).ToList()));

        // A refusal names what can be answered instead of the handler and says nothing about what
        // the handler answers when it runs, so the return type is still the only source of the
        // success. The same rule [Throws<T>] follows.
        if (reaching.Refusals.Count > 0 && handler.ResponseInformation.UnionCases == null) {
            merged.DeclaredResponsesAreComplete = false;
        }

        merged.DeclaredHeaderParameters =
            Combined(handler.DeclaredHeaderParameters, reaching.HeaderParameters());

        // Headers() answers null where it merged nothing in, which is every handler the rung
        // declares no header for.
        merged.SingleResponseHeaders =
            reaching.Headers(
                handler.ResponseInformation.DefaultStatusCode ?? 200,
                handler.SingleResponseHeaders ?? System.Array.Empty<ResponseHeaderModel>())
            ?? handler.SingleResponseHeaders;

        return merged;
    }

    /// <summary>
    /// The handler's own header parameters, then the rung's that it does not already name.
    /// </summary>
    private static IReadOnlyList<DeclaredHeaderParameterModel> Combined(
        IReadOnlyList<DeclaredHeaderParameterModel> declared,
        IReadOnlyList<DeclaredHeaderParameterModel> wider) {
        if (wider.Count == 0) {
            return declared;
        }

        var result = new List<DeclaredHeaderParameterModel>(declared);

        foreach (var header in wider) {
            if (!result.Exists(existing =>
                    string.Equals(existing.Name, header.Name, System.StringComparison.OrdinalIgnoreCase))) {
                result.Add(header);
            }
        }

        return result;
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
        WriteTimeout(builder, handler);

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
    /// arguments as source text with their quotes taken off.
    /// </summary>
    /// <remarks>
    /// Split at the commas that separate arguments rather than at every comma. A description is
    /// prose and prose has commas in it, and the truncation was silent: the document published
    /// everything up to the first one.
    /// </remarks>
    private static (string? Title, string? Version, string? Description) InfoAttribute(
        EntryPointSelector.Model appModel) {
        if (appModel.AttributeModels == null) {
            return (null, null, null);
        }

        foreach (var attribute in appModel.AttributeModels) {
            if (!attribute.TypeDefinition.Name.StartsWith("OpenApiInfo", System.StringComparison.Ordinal)) {
                continue;
            }

            var parts = AttributeArguments.Split(attribute.Arguments);

            var title = AttributeArguments.Text(parts, 0, "title");
            var infoVersion = AttributeArguments.Text(parts, 1, "version");
            var description = AttributeArguments.Text(parts, 2, "description");

            return (
                title.Length > 0 ? title : null,
                infoVersion.Length > 0 ? infoVersion : null,
                description.Length > 0 ? description : null);
        }

        return (null, null, null);
    }

    /// <summary>
    /// Where the application is served, from the contract when it says and from <c>[Server]</c>
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// The same precedence <c>info</c> gets: what the contract declares about itself wins, and the
    /// attribute is how a code-first application - or a described one whose contract says nothing -
    /// answers the same question. Either or, rather than a union: an author who wrote both said one
    /// thing twice, and publishing both would put the same host in the list two ways.
    /// </remarks>
    private static void WriteServers(
        StringBuilder builder, EntryPointSelector.Model appModel, DocumentIdentity? identity) {
        if (identity != null && identity.Servers.Count > 0) {
            var firstDeclared = true;

            foreach (var (url, description) in identity.Servers) {
                if (url.Length == 0) {
                    continue;
                }

                WriteServer(builder, url, description ?? "", ref firstDeclared);
            }

            if (!firstDeclared) {
                builder.Append(']');
            }

            return;
        }

        if (appModel.AttributeModels == null) {
            return;
        }

        var first = true;

        foreach (var attribute in appModel.AttributeModels) {
            if (!attribute.TypeDefinition.Name.StartsWith("Server", System.StringComparison.Ordinal)) {
                continue;
            }

            // "url", "description" - split at the commas between arguments, because both may
            // contain one of their own.
            var parts = AttributeArguments.Split(attribute.Arguments);
            var url = AttributeArguments.Text(parts, 0, "url");

            if (url.Length == 0) {
                continue;
            }

            WriteServer(builder, url, AttributeArguments.Text(parts, 1, "description"), ref first);
        }

        if (!first) {
            builder.Append(']');
        }
    }

    private static void WriteServer(
        StringBuilder builder, string url, string description, ref bool first) {
        builder.Append(first ? ",\"servers\":[" : ",");

        builder.Append("{\"url\":\"").Append(JsonSchemaWriter.Escape(url)).Append('"');

        if (description.Length > 0) {
            builder.Append(",\"description\":\"")
                .Append(JsonSchemaWriter.Escape(description)).Append('"');
        }

        builder.Append('}');

        first = false;
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
        var descriptions = new Dictionary<string, string>();

        foreach (var handler in handlers) {
            var tag = Tag(handler);

            if (!seen.Contains(tag)) {
                seen.Add(tag);
            }

            // The contract's prose for the group, where a handler carries it. First writer wins,
            // which cannot disagree with itself: every handler under one tag came from the same
            // service and carries the same description.
            if (!descriptions.ContainsKey(tag) && !string.IsNullOrEmpty(handler.TagDescription)) {
                descriptions[tag] = handler.TagDescription!;
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

            builder.Append("{\"name\":\"").Append(JsonSchemaWriter.Escape(seen[i])).Append('"');

            if (descriptions.TryGetValue(seen[i], out var description)) {
                builder.Append(",\"description\":\"")
                    .Append(JsonSchemaWriter.Escape(description)).Append('"');
            }

            builder.Append('}');
        }

        builder.Append(']');
    }

    private static void WriteParameters(
        StringBuilder builder, RequestHandlerModel handler, OpenApiVersion version,
        IReadOnlyDictionary<string, EnumVocabulary> enums) {
        var bound = handler.RequestParameterInformationList
            .Where(p => Location(p.BindingType) != null)
            .ToList();

        // A header a filter reads is dropped where the handler binds one of that name: the
        // handler's carries a type, a constraint and a description of its own, and two entries
        // under one name is a document no generator can read.
        var declared = handler.DeclaredHeaderParameters
            .Where(header => !bound.Any(parameter => string.Equals(
                BoundName(parameter), header.Name, System.StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (bound.Count == 0 && declared.Count == 0) {
            return;
        }

        builder.Append(",\"parameters\":[");

        for (var i = 0; i < bound.Count; i++) {
            var parameter = bound[i];

            if (i > 0) {
                builder.Append(',');
            }

            var name = BoundName(parameter);

            // A parameter carrying a default is one the caller may omit - the binder answers
            // with the default rather than a 400 - so publishing it required documents a demand
            // the service does not make. Path parameters stay required whatever they carry,
            // because OpenAPI requires it of them and a path segment cannot be absent. A
            // [Required] on a parameter that could be absent is the same demand the validator
            // makes, and is published as one.
            var required = (parameter.Required || parameter.RequiredByConstraint) &&
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

        for (var i = 0; i < declared.Count; i++) {
            if (bound.Count > 0 || i > 0) {
                builder.Append(',');
            }

            builder.Append("{\"name\":\"").Append(JsonSchemaWriter.Escape(declared[i].Name))
                .Append("\",\"in\":\"header\",\"required\":false");

            WriteText(builder, "description", declared[i].Description);

            builder.Append(",\"schema\":{\"type\":\"string\"}}");
        }

        builder.Append(']');
    }

    private static void WriteRequestBody(
        StringBuilder builder, RequestHandlerModel handler, SortedDictionary<string, string> components) {
        if (handler.RequestSchema == null) {
            return;
        }

        Merge(components, handler.RequestSchema);

        // The media type the operation actually reads, which was hardcoded to JSON - so an
        // operation taking a blob published a content map naming the one type its body cannot be,
        // and a generated client sent the wrong Content-Type on the request the operation exists
        // for.
        var contentType = handler.RequestContentType ?? "application/json";

        builder.Append(",\"requestBody\":{\"required\":true,\"content\":{\"")
            .Append(JsonSchemaWriter.Escape(contentType))
            .Append("\":{\"schema\":")
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

        // Keyed and sorted rather than appended in the order the writers run. A synthesized 400 or
        // 401 used to land after every declared response, so an operation answering 200, 404 and
        // 401 published them in that order - which reads as arbitrary next to the declared block,
        // which has always been sorted, and made the two halves of one responses object disagree
        // about what order means.
        var responses = new SortedDictionary<int, string>();

        if (handler.ResponseSchemas.Count == 0) {
            WriteSingleResponse(responses, handler, components, version, successStatus);
        }
        else if (returnTypeDeclaredThem) {
            WriteDeclaredResponses(responses, handler, components, version);
        }
        else {
            WriteSingleResponse(responses, handler, components, version, successStatus);
            WriteDeclaredResponses(responses, handler, components, version);
        }

        WriteValidationResponse(responses, handler, components);
        WriteAuthenticationResponse(responses, handler, components);
        WriteAuthorizationResponse(responses, handler, components);
        WriteTimeoutResponse(responses, handler, components);
        WriteConstrainedPathResponse(responses, handler);

        var first = true;

        foreach (var response in responses) {
            if (!first) {
                builder.Append(',');
            }

            builder.Append('"').Append(response.Key).Append("\":").Append(response.Value);

            first = false;
        }

        builder.Append('}');
    }

    /// <summary>
    /// The 403 an operation whose security requires a scope can answer, declared rather than
    /// implied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A scoped entry becomes <c>Requirement.Grant</c>, which refuses an authenticated caller who
    /// does not hold it - so the status is a fact about the operation the way the 401 beside it
    /// is. An entry with no scopes becomes <c>Requirement.Authenticated</c> and can refuse nobody
    /// who got past the 401, which is why this is keyed on the scopes rather than on there being a
    /// requirement at all. <c>docs/design/described-authorization.md</c> is the table both readings come
    /// from.
    /// </para>
    /// <para>
    /// Every alternative, because the array is an OR. One unscoped entry beside a scoped one is
    /// satisfied by anybody who got past the 401, so the 403 is unreachable and publishing it
    /// would be the second half of the same defect this closes.
    /// </para>
    /// <para>
    /// Only a described operation reaches this. An attribute-routed one gets the same 403 from
    /// <c>IAuthorizeAttribute</c>'s <c>[AnswersStatus]</c>, which lands in <c>ResponseSchemas</c>
    /// and takes <see cref="DeclaresStatus"/>'s branch out.
    /// </para>
    /// </remarks>
    private static void WriteAuthorizationResponse(
        SortedDictionary<int, string> responses, RequestHandlerModel handler,
        SortedDictionary<string, string> components) {
        if (!EveryAlternativeRequiresAScope(handler) || DeclaresStatus(handler, 403)) {
            return;
        }

        components["ErrorModel"] = ErrorModelSchema;

        responses[403] = Envelope(
            handler, "The caller does not hold what this operation requires.", ErrorModelRef);
    }

    /// <summary>
    /// Whether the operation declares security and every alternative names at least one scope.
    /// </summary>
    /// <remarks>
    /// The entries are written as OpenAPI requirement objects - <c>{"oauth2":["pets:read"]}</c> -
    /// so a scope is a non-empty array under some scheme. Read as text rather than reparsed,
    /// because this generator writes the same strings a few lines further down and holds no JSON
    /// reader; a bracket pair with something between it is the whole of the question.
    /// </remarks>
    private static bool EveryAlternativeRequiresAScope(RequestHandlerModel handler) {
        if (handler.SecurityRequirements.Count == 0) {
            return false;
        }

        foreach (var requirement in handler.SecurityRequirements) {
            if (!NamesAScope(requirement)) {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether one requirement object carries a non-empty scope array.</summary>
    private static bool NamesAScope(string requirement) {
        var open = requirement.IndexOf('[');

        while (open >= 0) {
            var close = requirement.IndexOf(']', open);

            if (close < 0) {
                return false;
            }

            if (requirement.Substring(open + 1, close - open - 1).Trim().Length > 0) {
                return true;
            }

            open = requirement.IndexOf('[', close);
        }

        return false;
    }

    /// <summary>
    /// The status a bounded operation answers when its budget runs out, declared rather than
    /// implied.
    /// </summary>
    /// <remarks>
    /// <c>x-hardened-timeout</c> says how long the operation may take and nothing about what the
    /// caller is told, so a described contract that declared a deadline published a budget and no
    /// status while the runtime answered 504 all along. An attribute-routed handler already has
    /// this from <c>TimeoutAttribute</c>'s <c>[AnswersStatus]</c>, which is where the wording is
    /// taken from so the two paths publish one sentence.
    /// </remarks>
    private static void WriteTimeoutResponse(
        SortedDictionary<int, string> responses, RequestHandlerModel handler,
        SortedDictionary<string, string> components) {
        if (handler.DeclaredTimeout is not { } timeout || DeclaresStatus(handler, timeout.Status)) {
            return;
        }

        components["ErrorModel"] = ErrorModelSchema;

        // A string, like every other response header this writes. A header is a string on the
        // wire whatever it carries, which is the rule ResponseHeaderModel states.
        var headers = timeout.RetryAfterSeconds > 0
            ? "\"headers\":{\"Retry-After\":{" +
              "\"description\":\"How long to wait before trying again, in seconds.\"," +
              "\"schema\":{\"type\":\"string\"}}}"
            : null;

        responses[timeout.Status] = Envelope(
            handler, "The operation did not finish inside its budget.", ErrorModelRef, headers);
    }

    /// <summary>
    /// The deadline this operation is bounded by, as the extension the parser reads back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written so the exported document round-trips. A code-first application's budget is part of
    /// how its operations actually behave, and a contract that dropped it described a service that
    /// would wait forever - so a client generated from it, or a service regenerated from it,
    /// disagreed with the one that published it.
    /// </para>
    /// <para>
    /// The scalar form where the status and the retry-after are the defaults, which is almost every
    /// declaration and reads better than an object with one member. The object form only where
    /// there is something else to say.
    /// </para>
    /// <para>
    /// An extension because OpenAPI has no field for this. The specification describes the
    /// exchange; how long a server may take over it is a property of the server, and only its own
    /// vocabulary can carry it.
    /// </para>
    /// </remarks>
    private static void WriteTimeout(StringBuilder builder, RequestHandlerModel handler) {
        if (handler.DeclaredTimeout is not { } timeout) {
            return;
        }

        builder.Append(",\"x-hardened-timeout\":");

        if (timeout.Status == 504 && timeout.RetryAfterSeconds == 0) {
            builder.Append(timeout.Milliseconds);

            return;
        }

        builder.Append("{\"milliseconds\":").Append(timeout.Milliseconds)
            .Append(",\"status\":").Append(timeout.Status);

        if (timeout.RetryAfterSeconds > 0) {
            builder.Append(",\"retryAfterSeconds\":").Append(timeout.RetryAfterSeconds);
        }

        builder.Append('}');
    }

    /// <summary>
    /// A handler that returns one type: the status it succeeds with, and the body it sends.
    /// </summary>
    private static void WriteSingleResponse(
        SortedDictionary<int, string> responses, RequestHandlerModel handler,
        SortedDictionary<string, string> components,
        OpenApiVersion version, int successStatus) {
        var builder = new StringBuilder("{\"description\":\"")
            .Append(JsonSchemaWriter.Escape(HttpResponseDescription.For(successStatus)))
            .Append('"');

        // A handler returning a plain value has no ResponseSchemas entry to hang a declared header
        // on, and this is the writer that describes its success.
        if (handler.SingleResponseHeaders is { Count: > 0 } headers) {
            WriteResponseHeaders(builder, headers);
        }

        if (handler.ResponseInformation.IsAsyncEnumerable) {
            WriteStreamedResponse(builder, handler, components, version);
        }
        else if (handler.ResponseSchema != null) {
            Merge(components, handler.ResponseSchema);

            WriteContentMap(builder, ContentTypes(handler), handler.ResponseSchema.Schema);
        }

        responses[successStatus] = builder.Append('}').ToString();
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
        SortedDictionary<int, string> responses, RequestHandlerModel handler,
        SortedDictionary<string, string> components,
        OpenApiVersion version) {
        var streamedStatus = handler.ResponseInformation.IsAsyncEnumerable
            ? handler.ResponseInformation.DefaultStatusCode ?? 200
            : (int?)null;

        var byStatus = handler.ResponseSchemas
            .GroupBy(response => response.Status)
            .OrderBy(group => group.Key);

        var successContentTypes = ContentTypes(handler);
        var errorContentTypes = ErrorContentTypes(handler);

        foreach (var group in byStatus) {
            var description = group.First().Description;

            if (group.Key == 404 && RouteTemplate.HasConstraint(handler.Name.Path)) {
                description = Sentence(description) + ConstrainedPathNote;
            }

            var builder = new StringBuilder("{\"description\":\"")
                .Append(JsonSchemaWriter.Escape(description))
                .Append('"');

            WriteResponseHeaders(builder, group);

            // The handler's media types describe its success. An error body goes through the same
            // locator under a set of its own, because the exception path serializes JSON only when
            // nothing can write the error model as a declared type - see ErrorContentTypes.
            var contentTypes = group.Key >= 400 ? errorContentTypes : successContentTypes;

            var bodies = group.Where(response => response.Schema != null).ToList();

            // A described operation's streamed success sits in the declared set for its
            // description and headers, and carries no schema of its own: the item is on
            // ResponseSchema, and the streamed path writes it the way it does code-first.
            if (group.Key == streamedStatus) {
                WriteStreamedResponse(builder, handler, components, version);
            }
            else if (bodies.Count > 0) {
                string schema;

                if (bodies.Count == 1) {
                    Merge(components, bodies[0].Schema!);
                    schema = bodies[0].Schema!.Schema;
                }
                else {
                    var oneOf = new StringBuilder("{\"oneOf\":[");

                    for (var i = 0; i < bodies.Count; i++) {
                        if (i > 0) {
                            oneOf.Append(',');
                        }

                        Merge(components, bodies[i].Schema!);
                        oneOf.Append(bodies[i].Schema!.Schema);
                    }

                    schema = oneOf.Append("]}").ToString();
                }

                WriteContentMap(builder, contentTypes, schema);
            }

            responses[group.Key] = builder.Append('}').ToString();
        }
    }

    /// <summary>
    /// The <c>headers</c> a response declares, merged across the status's cases by wire name.
    /// </summary>
    /// <remarks>
    /// The declaration only: the value is the handler's, exactly as the generated case type's
    /// constructor divides them. Schema stays <c>string</c> for the reason
    /// <c>ResponseHeaderModel</c> gives - a header is a string on the wire whatever it carries.
    /// </remarks>
    private static void WriteResponseHeaders(
        StringBuilder builder, IEnumerable<ResponseSchemaModel> responses) =>
        WriteResponseHeaders(builder, responses.SelectMany(response => response.Headers));

    private static void WriteResponseHeaders(
        StringBuilder builder, IEnumerable<Generation.Models.ResponseHeaderModel> headers) {
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var first = true;

        {
            foreach (var header in headers) {
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
        SortedDictionary<int, string> responses, RequestHandlerModel handler,
        SortedDictionary<string, string> components) {
        if (handler.ParametersValidator == null && !handler.HasGeneratedValidation &&
            !HasBindingRefusals(handler)) {
            return;
        }

        // An operation whose contract declares the validation status answers it there: the
        // declared 422 is already in the response set, and a synthesized 400 beside it would
        // describe a status validation no longer produces on this operation.
        if (handler.ResponseInformation.ValidationErrorStatus != null) {
            return;
        }

        if (DeclaresStatus(handler, 400)) {
            return;
        }

        components["RequestValidationError"] = ValidationErrorSchema;

        responses[400] = Envelope(handler, "The request failed validation.", ValidationErrorRef);
    }

    /// <summary>
    /// Whether binding alone can refuse this request with the validation envelope.
    /// </summary>
    /// <remarks>
    /// A bound value that fails to parse as its declared type never reaches the handler:
    /// <c>StringConverterService.Parse</c> answers 400 with the same field-level envelope a
    /// generated validator produces. So the status is a fact about any operation binding a
    /// non-string value, whether or not a validator was generated for it - the gate that required
    /// one is why a documented 400 depended on the operation happening to declare a constraint.
    /// A string binds as itself and cannot fail conversion.
    ///
    /// <para>
    /// A route constraint that guarantees the conversion is the other way a parameter cannot refuse.
    /// The router decides first, so a value the converter would have rejected is a 404 and never
    /// reaches binding at all - which is what <c>{id:int}</c> is written for. Reading the path here
    /// is what stops this writer publishing a 400 the 404 writer in the same file has already
    /// explained away.
    /// </para>
    /// </remarks>
    private static bool HasBindingRefusals(RequestHandlerModel handler) {
        foreach (var parameter in handler.RequestParameterInformationList) {
            if (parameter.BindingType is not (ParameterBindType.Path or ParameterBindType.QueryString
                or ParameterBindType.Header or ParameterBindType.Cookie or ParameterBindType.Form)) {
                continue;
            }

            var name = parameter.ParameterType.Name.TrimEnd('?');

            if (name is "String" or "string" or "Object" or "object") {
                continue;
            }

            if (parameter.BindingType == ParameterBindType.Path &&
                RouteConstraintFacts.GuaranteesConversion(
                    RouteTemplate.ConstraintOn(handler.Name.Path, BoundName(parameter)), name)) {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>The name the caller uses for a parameter, which is the route token's name.</summary>
    private static string BoundName(RequestParameterInformation parameter) =>
        string.IsNullOrEmpty(parameter.BindingName) ? parameter.Name : parameter.BindingName;

    /// <summary>Whether the handler already declares <paramref name="status"/> itself.</summary>
    private static bool DeclaresStatus(RequestHandlerModel handler, int status) {
        foreach (var response in handler.ResponseSchemas) {
            if (response.Status == status) {
                return true;
            }
        }

        return (handler.ResponseInformation.DefaultStatusCode ?? 200) == status;
    }

    /// <summary>The schema of the framework's undeclared error body, written once into components.</summary>
    private const string ErrorModelSchema =
        "{\"type\":\"object\"," +
        "\"description\":\"How a refused or failed request is answered when the contract declared no body for it.\"," +
        "\"required\":[\"type\",\"message\",\"details\"]," +
        "\"properties\":{" +
        "\"type\":{\"type\":\"string\"}," +
        "\"message\":{\"type\":\"string\"}," +
        "\"details\":{\"type\":\"string\"}}}";

    /// <summary>
    /// The 401 an operation with security requirements can answer, declared rather than implied.
    /// </summary>
    /// <remarks>
    /// <c>AuthorizationFilter</c> refuses an unauthenticated caller before the handler runs, with
    /// a <c>WWW-Authenticate</c> challenge and the standard error body. The requirement itself was
    /// already published under <c>security</c>; this is the status enforcing it produces, which
    /// the document promised nothing about. Skipped where the operation declared its own 401,
    /// whose description then wins.
    /// </remarks>
    private static void WriteAuthenticationResponse(
        SortedDictionary<int, string> responses, RequestHandlerModel handler,
        SortedDictionary<string, string> components) {
        if (handler.SecurityRequirements.Count == 0) {
            return;
        }

        if (DeclaresStatus(handler, 401)) {
            return;
        }

        components["ErrorModel"] = ErrorModelSchema;

        responses[401] = Envelope(
            handler, "Authentication required.", ErrorModelRef,
            "\"headers\":{\"WWW-Authenticate\":{" +
            "\"description\":\"The challenge naming the scheme to authenticate with.\"," +
            "\"schema\":{\"type\":\"string\"}}}");
    }

    /// <summary>
    /// The 404 a constrained path token produces, stated rather than implied.
    /// </summary>
    /// <remarks>
    /// A value that violates a route constraint means the route did not match, so the answer is
    /// the router's 404 - deliberately bodyless, before binding and before the handler. The
    /// constraint itself is stripped from the path template, because a template expression is a
    /// name and nothing else, which left the document with no trace of the refusal at all.
    ///
    /// <para>
    /// Where the operation declares a 404 of its own, that description wins and
    /// <see cref="ConstrainedPathNote"/> is added to it by <see cref="WriteDeclaredResponses"/>
    /// instead. Two 404s reach the wire and only one can be written down, so the one that is
    /// written says the other exists: a client author reading a declared body schema is otherwise
    /// told nothing about the empty body they will also be sent.
    /// </para>
    /// </remarks>
    private static void WriteConstrainedPathResponse(
        SortedDictionary<int, string> responses, RequestHandlerModel handler) {
        if (!RouteTemplate.HasConstraint(handler.Name.Path)) {
            return;
        }

        if (DeclaresStatus(handler, 404)) {
            return;
        }

        responses[404] = "{\"description\":" +
                         "\"The path did not name a resource: a token failed its route constraint.\"}";
    }

    /// <summary>
    /// <paramref name="text"/> punctuated as a sentence, so a second one can follow it.
    /// </summary>
    /// <remarks>
    /// A description written in a contract usually has no full stop - <c>No pet with that id</c> -
    /// and appending to it produced one run-on sentence.
    /// </remarks>
    private static string Sentence(string text) =>
        text.Length == 0 || text[text.Length - 1] is '.' or '!' or '?' ? text : text + ".";

    /// <summary>
    /// What a declared 404 gains where a route constraint answers the same status.
    /// </summary>
    private const string ConstrainedPathNote =
        " A token that fails its route constraint answers this status too, before the handler and " +
        "with no body.";

    /// <summary>
    /// The media type every response falls back to, and the one an error body can always be
    /// written as.
    /// </summary>
    private const string Json = "application/json";

    private static readonly string[] JsonOnly = { Json };

    /// <summary>
    /// The media types a success goes out as, in the order the operation prefers them: what it
    /// declared, else the contract's, else JSON.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The whole declared set, one <c>content:</c> key each.</b> This took the first and dropped
    /// the rest, so an operation the runtime negotiates two representations of advertised one -
    /// <c>[Produces("application/json", "application/x-msgpack")]</c> published as JSON alone, and
    /// a generated client had no MessagePack branch for a response the service answers with.
    /// </para>
    /// <para>
    /// The first entry still means what it meant: the representation the operation leads with, and
    /// the one a client expressing no preference is answered with. That is why the set keeps its
    /// order here rather than being sorted - see <see cref="WriteContentMap"/>.
    /// </para>
    /// <para>
    /// Read from <c>ProducedContentTypes</c> ahead of <c>RawResponseContentType</c>, because the
    /// second is only set where the first names exactly one type and the handler writes text or
    /// bytes. Before <c>[Produces]</c> the two could not disagree: the only way to state a media
    /// type code-first was <c>[RawResponse]</c>, which went on nothing else.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> ContentTypes(RequestHandlerModel handler) {
        // The success half where the model carries it apart. A described operation's negotiated set
        // ends with its error representations, which the runtime needs and the success response is
        // not - see OperationModel.SuccessContentTypes.
        var produced = handler.ResponseInformation.SuccessContentTypes ??
                       handler.ResponseInformation.ProducedContentTypes;

        if (!string.IsNullOrEmpty(produced)) {
            var types = Split(produced!);

            if (types.Count > 0) {
                return types;
            }
        }

        if (!string.IsNullOrEmpty(handler.ResponseInformation.RawResponseContentType)) {
            return new[] { handler.ResponseInformation.RawResponseContentType! };
        }

        return !string.IsNullOrEmpty(handler.ResponseInformation.DeclaredContentType)
            ? new[] { handler.ResponseInformation.DeclaredContentType! }
            : JsonOnly;
    }

    /// <summary>
    /// The media types an error body goes out as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not JSON for every operation, which is what this published.</b>
    /// <c>ExceptionResponseSerializer</c> puts the error model through the same locator the success
    /// goes through, so an operation declaring <c>application/x-msgpack</c> answers its refusals as
    /// MessagePack wherever a serializer for it is registered. The document said
    /// <c>application/json</c> and the runtime did not.
    /// </para>
    /// <para>
    /// <b>The declared set and JSON, because the build cannot tell which.</b> The fallback fires
    /// only when nothing registered can write the error model as any declared type, and what a host
    /// registers is not readable from this compilation - the same limit
    /// <c>ContentTypeDiagnostics</c> works under. Describing both is honest where naming one is a
    /// guess. The declared types come first, which is the order the runtime tries them in.
    /// </para>
    /// <para>
    /// <b>A raw or streamed handler keeps JSON alone.</b> An error model is a model, and neither
    /// writer will take one: <c>RawResponseSerializer.CanProduce</c> refuses a response value that
    /// is not already bytes, and <c>StreamingJsonResponseSerializer.CanProduce</c> requires a
    /// committed framing that a refusal before the first item never wrote. Both leave the exception
    /// path with nothing producible, which is the case it commits JSON for.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> ErrorContentTypes(RequestHandlerModel handler) {
        // Stated, where a contract stated it. A described operation says what its refusals look
        // like, so there is nothing to work out and nothing to be generous about: those media types
        // and no others.
        if (handler.ResponseInformation.ErrorContentTypes is { } described) {
            return Split(described);
        }

        if (handler.ResponseInformation.ReturnsBytesOrText ||
            handler.ResponseInformation.IsAsyncEnumerable) {
            return JsonOnly;
        }

        var declared = ContentTypes(handler);

        if (declared.Count == 1 && declared[0] == Json) {
            return JsonOnly;
        }

        var types = new List<string>(declared.Count + 1);

        types.AddRange(declared);

        if (!types.Contains(Json)) {
            types.Add(Json);
        }

        return types;
    }

    private const string ErrorModelRef = "{\"$ref\":\"#/components/schemas/ErrorModel\"}";

    private const string ValidationErrorRef =
        "{\"$ref\":\"#/components/schemas/RequestValidationError\"}";

    /// <summary>
    /// One of the refusals the pipeline answers on its own: a description, optionally some headers,
    /// and the error body under every media type the operation can answer it as.
    /// </summary>
    /// <remarks>
    /// Four writers built this string by concatenation, each with <c>application/json</c> spelled
    /// into it. That was one media type restated in four places, so an operation declaring another
    /// had to be fixed in all four or in none.
    /// </remarks>
    private static string Envelope(
        RequestHandlerModel handler, string description, string schema, string? headers = null) {
        var builder = new StringBuilder("{\"description\":\"")
            .Append(JsonSchemaWriter.Escape(description))
            .Append('"');

        if (headers != null) {
            builder.Append(',').Append(headers);
        }

        WriteContentMap(builder, ErrorContentTypes(handler), schema);

        return builder.Append('}').ToString();
    }

    /// <summary>
    /// A comma-joined media type list as the set it names, in order and without repeats.
    /// </summary>
    private static List<string> Split(string contentTypes) {
        var types = new List<string>();

        foreach (var type in contentTypes.Split(',')) {
            var trimmed = type.Trim();

            if (trimmed.Length > 0 && !types.Contains(trimmed)) {
                types.Add(trimmed);
            }
        }

        return types;
    }

    /// <summary>
    /// A <c>content:</c> map: one key per media type, each naming the same schema.
    /// </summary>
    /// <remarks>
    /// Written in the order given rather than sorted, unlike <c>components</c> beside it. The order
    /// is the operation's own preference - <c>[Produces]</c> is ordered, and
    /// <c>OpenApiSpecParser</c> collects a response's keys in document order into
    /// <c>ProducedContentTypes</c> - so sorting here would make a round trip through the document
    /// change which representation the operation leads with.
    /// </remarks>
    private static void WriteContentMap(
        StringBuilder builder, IReadOnlyList<string> contentTypes, string schema) {
        builder.Append(",\"content\":{");

        for (var i = 0; i < contentTypes.Count; i++) {
            if (i > 0) {
                builder.Append(',');
            }

            builder.Append('"').Append(JsonSchemaWriter.Escape(contentTypes[i]))
                .Append("\":{\"schema\":").Append(schema).Append('}');
        }

        builder.Append('}');
    }

    /// <summary>
    /// A streamed response: the media type it is framed as, the shape of one item, and the whole
    /// body as an array of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two keys, because they answer different questions and OpenAPI 3.2 says both may be written.
    /// <c>itemSchema</c> describes each item as it is read off the stream, and is the only spelling
    /// that says the items arrive one after another. <c>schema</c> describes the complete content,
    /// which the specification defines for a sequential media type as the items treated as an
    /// array in the order they were sent - so it is written as exactly that, an array of the item.
    /// The item alone under <c>schema</c>, which is what this emitted before <c>itemSchema</c>
    /// existed, claims the response is a single one of them, and a generator built from that
    /// document produces a client that reads one and stops.
    /// </para>
    /// <para>
    /// <c>schema</c> is written at every version and is what a reader without 3.2 sees. Refitter
    /// takes a stream's element type from it and reads nothing from <c>itemSchema</c>, and a 3.0 or
    /// 3.1 reader that would otherwise be told nothing is told the item type and that there are
    /// many. What such a reader cannot be told is that they stream, so a client generated below 3.2
    /// reads a list; the handler is named in a build warning by <c>RoutingTableGenerator</c> so the
    /// trade is not silent. Kiota ignores both keys for a streaming media type and hands back the
    /// raw stream either way.
    /// </para>
    /// </remarks>
    private static void WriteStreamedResponse(
        StringBuilder builder, RequestHandlerModel handler, SortedDictionary<string, string> components,
        OpenApiVersion version) {
        var contentType = StreamFramingNames.ContentType(handler.ResponseInformation.StreamFraming);

        builder.Append(",\"content\":{\"").Append(JsonSchemaWriter.Escape(contentType)).Append("\":{");

        if (handler.ResponseSchema != null) {
            Merge(components, handler.ResponseSchema);

            builder.Append("\"schema\":{\"type\":\"array\",\"items\":")
                .Append(handler.ResponseSchema.Schema)
                .Append('}');

            if (OpenApiVersionFacts.SupportsItemSchema(version)) {
                builder.Append(",\"itemSchema\":").Append(handler.ResponseSchema.Schema);
            }
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
    /// <para>
    /// <c>[Operation]</c> on the handler declares the id outright, and it is written as given: the
    /// id is what a generated client names its method after, so a declared one holds the contract
    /// still across a rename. A derived name that collides with a declared one is prefixed with
    /// its tag, the same way two derived names are. Two declared ones colliding is
    /// <c>HRDOA004</c>, reported where the handlers are collected.
    /// </para>
    /// </remarks>
    private static IReadOnlyDictionary<string, string> OperationIds(
        IReadOnlyList<RequestHandlerModel> handlers) {
        var ids = new Dictionary<string, string>(System.StringComparer.Ordinal);
        var declared = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var handler in handlers) {
            if (handler.OperationId != null) {
                ids[HandlerKey(handler)] = handler.OperationId;
                declared.Add(handler.OperationId);
            }
        }

        var byName = new Dictionary<string, List<RequestHandlerModel>>(System.StringComparer.Ordinal);

        foreach (var handler in handlers) {
            if (handler.OperationId != null) {
                continue;
            }

            var name = CamelCase(handler.HandlerMethod);

            if (!byName.TryGetValue(name, out var sharing)) {
                sharing = new List<RequestHandlerModel>();
                byName[name] = sharing;
            }

            sharing.Add(handler);
        }

        foreach (var pair in byName) {
            var contested = pair.Value.Count > 1 || declared.Contains(pair.Key);

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
    /// The vocabulary schema for a code-first enum parameter, or null when the type is not one.
    /// </summary>
    /// <remarks>
    /// An attribute-routed application has no declaration to carry the vocabulary, but it does
    /// have the vocabulary itself: the same collected set the wire converters are generated from.
    /// Consulting it is what keeps a parameter's <c>enum</c> array agreeing with what the binder
    /// accepts - the fourth of the four places <c>EnumWireNaming</c>'s remarks require to agree.
    /// </remarks>
    /// <summary>
    /// A reference to the component an enum parameter's vocabulary is written as, or null for a
    /// type that is not one of the application's enums.
    /// </summary>
    private static string? EnumSchema(
        ITypeDefinition type, IReadOnlyDictionary<string, EnumVocabulary> enums) {
        var qualified = "global::" + type.Namespace + "." + type.Name.TrimEnd('?');

        if (!enums.TryGetValue(qualified, out var vocabulary)) {
            return null;
        }

        return "{\"$ref\":\"#/components/schemas/" + JsonSchemaWriter.Escape(vocabulary.Name) + "\"}";
    }

    /// <summary>The component an enum is written as: its values, in the vocabulary the wire carries.</summary>
    private static string EnumComponent(EnumVocabulary vocabulary) {
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
    /// A nullable scalar - <c>int?</c> on a query value or header - described as a string until
    /// <see cref="ScalarSchema"/> learned the keyword spellings. The earlier account of this gap
    /// blamed the model, claiming the type arrived as a <c>Nullable</c> with no argument to read.
    /// It never did: the unwrap resolves the underlying type fine and hands on a definition named
    /// with the C# keyword - <c>int</c>, by way of <c>TypeDefinition.Get(typeof(int))</c>, whose
    /// whole point is the keyword - while the schema switch matched only the CLR names a bare
    /// parameter's symbol produces.
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
            // A hand-written handler's constraints, read off the parameter where its symbol was
            // in hand and spliced in here, so a bound the validator enforces is a bound the
            // document states - the same statement written twice, as it is for a property.
            return SchemaConstraintWriter.Merge(
                CodeFirstSchema(parameter.ParameterType, enums), parameter.SchemaFacets);
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

    /// <summary>
    /// The schema for a code-first parameter, read off its C# type.
    /// </summary>
    /// <remarks>
    /// A collection is an array here for the same reason it binds as one: the binder fills it from
    /// every value the request carried, so a document calling it a string describes a parameter the
    /// application does not have.
    /// </remarks>
    private static string CodeFirstSchema(
        ITypeDefinition type, IReadOnlyDictionary<string, EnumVocabulary> enums) {
        var itemType = CollectionParameter.ItemType(type);

        if (itemType == null) {
            return EnumSchema(type, enums) ?? ScalarSchema(type);
        }

        return "{\"type\":\"array\",\"items\":" +
               (EnumSchema(itemType, enums) ?? ScalarSchema(itemType)) + "}";
    }

    private static string ScalarSchema(ITypeDefinition type) {
        // Two spellings of every predefined type reach here, and both must match. A bare `int`
        // parameter is built from its symbol and named "Int32"; `int?` is unwrapped through
        // TypeDefinition.Get(typeof(int)), whose whole point is the C# keyword name, so it
        // arrives as "int". Matching only the CLR names is how int? and long? published as
        // strings while int and long published as integers - the same type disagreeing with
        // itself about what it is. The trailing '?' is the third spelling, trimmed.
        var name = type.Name.TrimEnd('?');

        return name switch {
            "String" or "string" or "Char" or "char" => "{\"type\":\"string\"}",
            "Boolean" or "bool" => "{\"type\":\"boolean\"}",
            "Byte" or "byte" or "SByte" or "sbyte" or "Int16" or "short" or "UInt16" or "ushort"
                or "Int32" or "int" or "UInt32" or "uint" =>
                "{\"type\":\"integer\",\"format\":\"int32\"}",
            "Int64" or "long" or "UInt64" or "ulong" => "{\"type\":\"integer\",\"format\":\"int64\"}",
            "Single" or "float" => "{\"type\":\"number\",\"format\":\"float\"}",
            "Double" or "double" => "{\"type\":\"number\",\"format\":\"double\"}",
            "Decimal" or "decimal" => "{\"type\":\"number\"}",
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
