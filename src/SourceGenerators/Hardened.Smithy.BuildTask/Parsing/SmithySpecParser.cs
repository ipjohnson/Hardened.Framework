using System.Text.Json;
using Hardened.Idl;
using Hardened.Generation;
using Hardened.Generation.Models;

namespace Hardened.Smithy.BuildTask.Parsing;

/// <summary>
/// Turns a Smithy JSON AST into the neutral model.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately smaller than its OpenAPI counterpart, and for structural reasons rather than because
/// it does less. The AST resolves every reference, so there is no resolver. <c>smithy ast --flatten</c>
/// removes mixins, so there is no <c>allOf</c> merge. Inline input and output structures are hoisted
/// and named by the CLI, so nothing has to be synthesised. And HTTP binding lives in traits on an
/// operation's own input members, so the reconciliation between path-level and operation-level
/// parameters that OpenAPI needs does not arise.
/// </para>
/// <para>
/// What it returns is normalised <em>and named</em> - see <c>ExtractSpecTask.Parse</c> for why the
/// naming pass belongs to a front end rather than to the shell.
/// </para>
/// </remarks>
internal static class SmithySpecParser {

    /// <summary>
    /// Reads one AST. Returns null when nothing usable could be built, having said why.
    /// </summary>
    /// <param name="serviceShapeId">
    /// The service to generate, or null for every service the model declares. A Smithy model may
    /// carry several, which is cleaner than OpenAPI's informal tags but means the choice has to be
    /// expressible.
    /// </param>
    internal static ServiceSpecModel? Parse(
        string json,
        string fileName,
        ICollection<string> diagnostics,
        string? serviceShapeId = null) {
        var ast = SmithyAst.Load(json, diagnostics);

        if (ast == null) {
            return null;
        }

        var services = new List<KeyValuePair<string, JsonElement>>();

        foreach (var shape in ast.Shapes) {
            if (SmithyAst.Kind(shape.Value) == "service" &&
                !SmithyAst.IsTraitDefinition(shape.Value)) {
                services.Add(shape);
            }
        }

        if (services.Count == 0) {
            diagnostics.Add("the model declares no service shape, so there is nothing to generate.");

            return null;
        }

        if (serviceShapeId != null) {
            services.RemoveAll(s => !string.Equals(s.Key, serviceShapeId, StringComparison.Ordinal));

            if (services.Count == 0) {
                diagnostics.Add($"the model declares no service shape named '{serviceShapeId}'.");

                return null;
            }
        }

        // Sorted so the emitted model does not depend on the order the AST happened to list shapes
        // in. The build task compares written content to decide whether to rewrite, and an unstable
        // order would make every build look dirty.
        services.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));

        var model = new ServiceSpecModel { FileName = fileName };
        var context = new ParseContext(ast, model, diagnostics);

        foreach (var service in services) {
            if (!TryReadProtocol(service.Key, service.Value, diagnostics, out var protocol)) {
                return null;
            }

            ParseService(context, service.Key, service.Value, protocol);
        }

        if (model.Services.Count == 0 || model.Services.TrueForAll(s => s.Operations.Count == 0)) {
            diagnostics.Add("the model's services bind no operations, so there is nothing to generate.");

            return null;
        }

        ReportUnhandledTraits(context);

        // Sorted for the same reason the services are, and before naming because the allocator
        // resolves collisions by sorting - which only makes the outcome independent of the
        // document's order if what it sorts is already stable.
        model.Schemas.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));

        NameAllocator.Apply(model, fileName);

        return model;
    }

    /// <summary>What one parse needs to carry, so nothing threads six parameters.</summary>
    private sealed class ParseContext {
        internal ParseContext(SmithyAst ast, ServiceSpecModel model, ICollection<string> diagnostics) {
            Ast = ast;
            Model = model;
            Diagnostics = diagnostics;
        }

        internal SmithyAst Ast { get; }

        internal ServiceSpecModel Model { get; }

        internal ICollection<string> Diagnostics { get; }

        /// <summary>Shapes already turned into schemas, so a shape reached twice is built once.</summary>
        internal HashSet<string> Built { get; } = new(StringComparer.Ordinal);

        /// <summary>Traits seen anywhere, for the unknown-trait report.</summary>
        internal HashSet<string> SeenTraits { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// How a service's operations are selected, and what they exchange.
    /// </summary>
    /// <remarks>
    /// Two shapes only: routed, where the request's path and verb name the operation, and
    /// dispatched, where every request goes to one route and a header names it. Nothing else about
    /// a protocol reaches the model, which is what keeps a third one a table entry.
    /// </remarks>
    private readonly struct ProtocolBinding {
        internal string? DispatchHeader { get; init; }

        internal string? ContentType { get; init; }

        internal bool Dispatches => DispatchHeader != null;
    }

    /// <summary>
    /// Reads the service's protocol, or refuses one whose wire format this does not serve.
    /// </summary>
    /// <remarks>
    /// Absence is fine - a model using only <c>@http</c> declares no protocol and is exactly what a
    /// hand-written service looks like. A named protocol that is neither restJson1 nor an awsJson
    /// version is refused rather than ignored, because the generated server would be confidently
    /// wrong rather than merely incomplete.
    /// </remarks>
    private static bool TryReadProtocol(
        string serviceId,
        JsonElement service,
        ICollection<string> diagnostics,
        out ProtocolBinding protocol) {
        protocol = default;

        foreach (var trait in SmithyAst.Traits(service)) {
            if (SmithyTraits.RefusedProtocols.TryGetValue(trait.Key, out var reason)) {
                diagnostics.Add(
                    $"service '{SmithyPrelude.LocalName(serviceId)}' declares protocol " +
                    $"'{trait.Key}', which this generator does not serve: {reason}.");

                return false;
            }

            if (SmithyTraits.DispatchProtocols.TryGetValue(trait.Key, out var header)) {
                protocol = new ProtocolBinding {
                    DispatchHeader = header,
                    ContentType = SmithyTraits.DispatchContentTypes[trait.Key]
                };
            }
        }

        return true;
    }

    private static void ParseService(
        ParseContext context, string serviceId, JsonElement service, ProtocolBinding protocol) {
        var tag = SmithyPrelude.LocalName(serviceId);
        var operations = new List<OperationModel>();

        Note(context, service);

        foreach (var operationId in Operations(context, service)) {
            if (!context.Ast.TryGetShape(operationId, out var operation)) {
                context.Diagnostics.Add(
                    $"service '{tag}' binds operation '{operationId}', which the model does not declare.");
                continue;
            }

            var parsed = ParseOperation(
                context, operationId, operation, tag, protocol, RequiresAuth(service));

            if (parsed != null) {
                operations.Add(parsed);
            }
        }

        operations.Sort((left, right) =>
            string.CompareOrdinal(left.OperationId, right.OperationId));

        context.Model.Services.Add(new ServiceModel {
            Tag = tag,
            DispatchHeader = protocol.DispatchHeader,
            Operations = operations
        });
    }

    /// <summary>
    /// Every operation a service reaches, directly or through a resource.
    /// </summary>
    /// <remarks>
    /// Resources are flattened into the service that owns them. A resource's lifecycle operations
    /// are ordinary operations with ordinary HTTP bindings, and the resource itself has no C#
    /// equivalent worth inventing - it groups operations, which is what the service already does.
    /// </remarks>
    private static IEnumerable<string> Operations(ParseContext context, JsonElement service) {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<JsonElement>();

        queue.Enqueue(service);

        while (queue.Count > 0) {
            var shape = queue.Dequeue();

            foreach (var operationId in SmithyAst.TargetList(shape, "operations")) {
                if (seen.Add(operationId)) {
                    yield return operationId;
                }
            }

            foreach (var operationId in SmithyAst.TargetList(shape, "collectionOperations")) {
                if (seen.Add(operationId)) {
                    yield return operationId;
                }
            }

            // A resource's lifecycle bindings are single targets rather than a list.
            foreach (var lifecycle in new[] { "create", "put", "read", "update", "delete", "list" }) {
                if (shape.TryGetProperty(lifecycle, out var bound)) {
                    var target = SmithyAst.Target(bound);

                    if (target != null && seen.Add(target)) {
                        yield return target;
                    }
                }
            }

            foreach (var resourceId in SmithyAst.TargetList(shape, "resources")) {
                if (context.Ast.TryGetShape(resourceId, out var resource)) {
                    queue.Enqueue(resource);
                }
            }
        }
    }

    /// <summary>
    /// Whether a shape's own traits say a caller must be authenticated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two ways to say no, and they mean different things to a reader of the model even though they
    /// mean the same thing here. <c>@auth([])</c> narrows the supported schemes to none, which is how
    /// an operation on an authenticated service is made public. <c>@optionalAuth</c> says a caller
    /// may authenticate and need not - which, for a server deciding whether to refuse, is the same
    /// answer.
    /// </para>
    /// <para>
    /// A service that declares a scheme and an operation that neither opts out is the only shape
    /// that requires anything. Smithy cannot say more than that: it has no scopes, so it can never
    /// produce a grant.
    /// </para>
    /// </remarks>
    /// <summary>Whether the shape narrows its supported schemes to none - <c>@auth([])</c>.</summary>
    private static bool DeclaresNoAuth(JsonElement shape) =>
        SmithyAst.TryGetTrait(shape, SmithyTraits.Auth, out var auth) &&
        auth.ValueKind == JsonValueKind.Array &&
        auth.GetArrayLength() == 0;

    private static bool RequiresAuth(JsonElement service) {
        // @auth narrows the schemes a shape supports, so it answers on its own when present - and
        // an empty list is how a service supports none.
        //
        // No @optionalAuth check here. Smithy defines that trait on operations, so a service
        // carrying one is not a model this has to answer for, and the operation is asked directly.
        if (SmithyAst.TryGetTrait(service, SmithyTraits.Auth, out var auth)) {
            return auth.ValueKind == JsonValueKind.Array && auth.GetArrayLength() > 0;
        }

        foreach (var scheme in SmithyTraits.AuthSchemes) {
            if (SmithyAst.HasTrait(service, scheme)) {
                return true;
            }
        }

        return false;
    }

    private static OperationModel? ParseOperation(
        ParseContext context,
        string operationId,
        JsonElement operation,
        string tag,
        ProtocolBinding protocol,
        bool serviceRequiresAuth) {
        Note(context, operation);

        var name = SmithyPrelude.LocalName(operationId);

        if (SmithyAst.HasTrait(operation, SmithyTraits.Streaming)) {
            context.Diagnostics.Add(
                $"operation '{name}' is @streaming, which this generator cannot serve, so it was skipped.");

            return null;
        }

        OperationModel model;

        if (protocol.Dispatches) {
            // The protocol decides all of this, and the operation has no say. An @http trait here is
            // not an error and not a conflict: the specification says HTTP binding traits "MUST be
            // ignored if they are present", so a model that carries both is well formed and this is
            // what ignoring them means.
            model = new OperationModel {
                OperationId = name,
                Tag = tag,
                Path = "/",
                HttpMethod = "POST",
                DispatchKey = tag + "." + name,
                RequestBodyContentType = protocol.ContentType,
                ResponseContentType = protocol.ContentType
            };
        } else {
            if (!SmithyAst.TryGetTrait(operation, SmithyTraits.Http, out var http)) {
                context.Diagnostics.Add(
                    $"operation '{name}' has no @http trait, so it has no route and was skipped.");

                return null;
            }

            model = new OperationModel {
                OperationId = name,
                Tag = tag,
                Path = String(http, "uri") ?? "/",
                HttpMethod = (String(http, "method") ?? "GET").ToUpperInvariant(),
                SuccessStatusCode = Int(http, "code") ?? 200
            };
        }

        model.Description = Text(operation, SmithyTraits.Documentation);
        model.IsDeprecated = SmithyAst.HasTrait(operation, SmithyTraits.Deprecated);

        // Authentication only, because that is all the language carries. An operation may opt out of
        // an authenticated service; it cannot opt in to one that declares no scheme, because there
        // would be nothing to authenticate against.
        if (serviceRequiresAuth &&
            !SmithyAst.HasTrait(operation, SmithyTraits.OptionalAuth) &&
            !DeclaresNoAuth(operation)) {
            model.AuthorizationBranches.Add(
                new AuthorizationBranchModel { RequiresAuthentication = true });
        }

        ParseInput(context, operation, model, name, protocol);
        ParseOutput(context, operation, model, name, protocol);

        // The success as a declared response, mirroring what the OpenAPI parser records. Smithy
        // models one output per operation, so there is always exactly one and never the multiple
        // 2xx a description can declare - but the emitters read this list rather than the flat
        // fields when they build a response set, and an operation missing from it gets a set
        // carrying only its errors and no case a handler can return to say it succeeded. That is
        // what a Smithy operation answering 204 did: RemoveTodoNoContent was never emitted.
        model.SuccessResponses.Add(new SuccessResponseModel {
            StatusCode = model.SuccessStatusCode,
            Ref = model.ResponseRef,
            Type = model.ResponseType,
            Format = model.ResponseFormat,
            IsArray = model.ResponseIsArray,
            ArrayItemsRef = model.ResponseArrayItemsRef,
            ArrayItemsType = model.ResponseArrayItemsType,
            ContentType = model.ResponseContentType
        });

        ParseErrors(context, operation, model, protocol);

        return model;
    }

    /// <summary>
    /// Splits an operation's input structure into route bindings and a body.
    /// </summary>
    /// <remarks>
    /// This is the part Smithy makes simpler than OpenAPI does. Binding lives on the member, so
    /// each one lands in exactly one place and nothing has to be reconciled: a member carrying
    /// <c>@httpLabel</c> is a path parameter, one carrying <c>@httpQuery</c> is a query parameter,
    /// and a member carrying no binding trait at all is part of the JSON body.
    /// </remarks>
    private static void ParseInput(
        ParseContext context,
        JsonElement operation,
        OperationModel model,
        string operationName,
        ProtocolBinding protocol) {
        if (!operation.TryGetProperty("input", out var inputRef)) {
            return;
        }

        var inputId = SmithyAst.Target(inputRef);

        if (inputId == null || inputId == SmithyPrelude.Unit) {
            return;
        }

        if (!context.Ast.TryGetShape(inputId, out var input)) {
            context.Diagnostics.Add(
                $"operation '{operationName}' takes '{inputId}', which the model does not declare.");

            return;
        }

        Note(context, input);

        var bodyMembers = new List<KeyValuePair<string, JsonElement>>();

        foreach (var member in SmithyAst.Members(input)) {
            Note(context, member.Value);

            // Under a dispatch protocol there is nowhere for a binding to put anything: the request
            // is POST / with the input structure as its body, and the specification requires the
            // binding traits to be ignored rather than honoured.
            if (protocol.Dispatches) {
                bodyMembers.Add(member);
            } else if (SmithyAst.TryGetTrait(member.Value, SmithyTraits.HttpLabel, out _)) {
                AddParameter(context, model, member, "path", member.Key);
            } else if (SmithyAst.TryGetTrait(member.Value, SmithyTraits.HttpQuery, out var query)) {
                AddParameter(context, model, member, "query",
                    query.ValueKind == JsonValueKind.String ? query.GetString() ?? member.Key : member.Key);
            } else if (SmithyAst.TryGetTrait(member.Value, SmithyTraits.HttpHeader, out var header)) {
                AddParameter(context, model, member, "header",
                    header.ValueKind == JsonValueKind.String ? header.GetString() ?? member.Key : member.Key);
            } else if (SmithyAst.TryGetTrait(member.Value, SmithyTraits.HttpPayload, out _)) {
                var target = SmithyAst.Target(member.Value);

                if (target != null) {
                    model.RequestBodyContentType ??= "application/json";
                    model.RequestBodyRef = ReferenceTo(context, target);
                }
            } else {
                bodyMembers.Add(member);
            }
        }

        if (bodyMembers.Count == 0 || model.RequestBodyRef != null) {
            return;
        }

        // Members with no binding trait are the JSON body. The input structure already names them
        // as a group, so the body is that shape - there is nothing to synthesise, which is the case
        // OpenAPI needs SynthesizeSchema for.
        // Left alone when the protocol already named one - awsJson sends
        // application/x-amz-json-1.0, not application/json.
        model.RequestBodyContentType ??= "application/json";
        model.RequestBodyRef = ReferenceTo(context, inputId);

        foreach (var member in bodyMembers) {
            var property = BuildProperty(context, member.Key, member.Value);

            model.RequestBodyProperties.Add(property);

            if (property.IsRequired) {
                model.RequestBodyRequired.Add(member.Key);
            }
        }
    }

    private static void ParseOutput(
        ParseContext context,
        JsonElement operation,
        OperationModel model,
        string operationName,
        ProtocolBinding protocol) {
        if (!operation.TryGetProperty("output", out var outputRef)) {
            return;
        }

        var outputId = SmithyAst.Target(outputRef);

        if (outputId == null || outputId == SmithyPrelude.Unit) {
            return;
        }

        if (!context.Ast.TryGetShape(outputId, out var output)) {
            context.Diagnostics.Add(
                $"operation '{operationName}' returns '{outputId}', which the model does not declare.");

            return;
        }

        Note(context, output);

        model.ResponseContentType ??= "application/json";

        // An @httpPayload member is the whole response body; otherwise the output structure is.
        // Under a dispatch protocol the trait is ignored, so the structure always is.
        foreach (var member in SmithyAst.Members(output)) {
            if (protocol.Dispatches ||
                !SmithyAst.TryGetTrait(member.Value, SmithyTraits.HttpPayload, out _)) {
                continue;
            }

            var target = SmithyAst.Target(member.Value);

            if (target != null) {
                model.ResponseRef = ReferenceTo(context, target);

                return;
            }
        }

        model.ResponseRef = ReferenceTo(context, outputId);
    }

    private static void ParseErrors(
        ParseContext context, JsonElement operation, OperationModel model, ProtocolBinding protocol) {
        foreach (var errorId in SmithyAst.TargetList(operation, "errors")) {
            if (!context.Ast.TryGetShape(errorId, out var error)) {
                continue;
            }

            Note(context, error);

            var status = 500;

            // @httpError is an HTTP binding trait, which a dispatch protocol requires be ignored -
            // so under one, @error's client/server is the only thing that decides the status.
            if (!protocol.Dispatches &&
                SmithyAst.TryGetTrait(error, SmithyTraits.HttpError, out var httpError) &&
                httpError.ValueKind == JsonValueKind.Number) {
                status = httpError.GetInt32();
            } else if (SmithyAst.TryGetTrait(error, SmithyTraits.Error, out var kind) &&
                       kind.ValueKind == JsonValueKind.String) {
                // @error says client or server; @httpError says which code. With only the former,
                // the conventional default for each is the honest answer.
                status = kind.GetString() == "client" ? 400 : 500;
            }

            var reference = ReferenceTo(context, errorId);

            if (protocol.Dispatches) {
                AddTypeDiscriminator(context, errorId);
            }

            model.ErrorResponses.Add(new ErrorResponseModel {
                StatusCode = status,
                Ref = reference,
                Description = Text(error, SmithyTraits.Documentation)
            });
        }

        model.ErrorResponses.Sort((left, right) => left.StatusCode.CompareTo(right.StatusCode));
    }

    /// <summary>
    /// The <c>__type</c> field that tells a caller which error a dispatch protocol's response is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// awsJson serializes an error exactly like a success and distinguishes it by one extra body
    /// field, so there is no status code to read it from and no envelope wrapping it. The
    /// specification says a server should send the error shape's <em>full</em> shape id, which is
    /// why this carries <c>com.example#NotFound</c> rather than <c>NotFound</c> - a client is
    /// specified to strip everything before a <c>#</c>, so the qualified form is what both halves
    /// agree on.
    /// </para>
    /// <para>
    /// <b>Added to the schema rather than emitted specially.</b> A property with a constant default
    /// is what this is, and expressing it that way means the record, the constructor's positional
    /// order and the JSON type info all follow from <c>SchemaShape.Constructor</c> exactly as they
    /// do for a declared property - which is the coupling that file exists to keep. An emitter that
    /// wrote <c>__type</c> itself would have to be taught to the resolver a second time, and the two
    /// would drift by index.
    /// </para>
    /// <para>
    /// Idempotent, because one error shape is routinely thrown by several operations and each of
    /// them reaches this.
    /// </para>
    /// </remarks>
    private static void AddTypeDiscriminator(ParseContext context, string errorId) {
        var name = SmithyPrelude.LocalName(errorId);
        var schema = context.Model.Schemas.Find(s => s.Name == name);

        if (schema == null || schema.Properties.Exists(p => p.Name == TypeDiscriminator)) {
            return;
        }

        schema.Properties.Add(new PropertyModel {
            Name = TypeDiscriminator,
            Type = "string",

            // Optional, which is what gives the parameter its default - and the default is the
            // whole point: nothing constructs this, it is a constant the wire needs.
            IsRequired = false,
            Default = errorId,
            Description = "The shape id identifying this error, as the protocol requires."
        });
    }

    /// <summary>The body field an awsJson error is recognised by.</summary>
    private const string TypeDiscriminator = "__type";

    private static void AddParameter(
        ParseContext context,
        OperationModel model,
        KeyValuePair<string, JsonElement> member,
        string location,
        string wireName) {
        var target = SmithyAst.Target(member.Value);
        var parameter = new ParameterModel {
            Name = wireName,
            In = location,
            IsRequired = location == "path" ||
                         SmithyAst.HasTrait(member.Value, SmithyTraits.Required),
            Description = Text(member.Value, SmithyTraits.Documentation)
        };

        // MemberNameOverride is deliberately not set here. It is NameAllocator's output slot, not an
        // input - the allocator assigns every C# name in one pass, from the wire name, and anything
        // written here is overwritten. So a member called `detailed` bound to @httpQuery("verbose")
        // reaches C# as `verbose`: the wire name is the contract, and the alternative would be a
        // second naming authority, which is the exact defect NameAllocator was built to remove.
        if (target != null) {
            Describe(context, target, out var type, out var format, out var reference, out var array);

            parameter.Type = type;
            parameter.Format = format;
            parameter.Ref = reference;
            parameter.IsArray = array.IsArray;
            parameter.ArrayItemsType = array.ItemType;
            parameter.ArrayItemsRef = array.ItemRef;
        }

        ApplyTo(ReadConstraints(context, member.Value, target), parameter);

        model.Parameters.Add(parameter);
    }

    private static PropertyModel BuildProperty(
        ParseContext context, string name, JsonElement member) {
        var property = new PropertyModel {
            Name = JsonName(member) ?? name,
            IsRequired = SmithyAst.HasTrait(member, SmithyTraits.Required),
            Description = Text(member, SmithyTraits.Documentation)
        };

        // As with parameters, the C# name is NameAllocator's to assign - from Name, which @jsonName
        // has already set to the wire spelling where the two differ.
        var target = SmithyAst.Target(member);

        if (target != null) {
            Describe(context, target, out var type, out var format, out var reference, out var shape);

            property.Type = type;
            property.Format = format;
            property.Ref = reference;
            property.IsArray = shape.IsArray;
            property.ArrayItemsType = shape.ItemType;
            property.ArrayItemsRef = shape.ItemRef;
            property.IsDictionary = shape.IsDictionary;
            property.DictionaryValueType = shape.ValueType;
            property.DictionaryValueRef = shape.ValueRef;
        }

        // Smithy's nullability rules in one line, and this is the whole of them for a server:
        // a member is non-null when it is @required or carries a @default, and nullable otherwise.
        // @clientOptional makes a required member optional for clients, which a server implementing
        // the contract still has to accept.
        property.IsNullable = !property.IsRequired &&
                              !SmithyAst.HasTrait(member, SmithyTraits.Default);

        if (SmithyAst.HasTrait(member, SmithyTraits.ClientOptional)) {
            property.IsNullable = true;
        }

        ApplyTo(ReadConstraints(context, member, target), property);
        NoteUnmappedTraits(context, member, name);

        return property;
    }

    /// <summary>
    /// Traits this parser knows about and does not map, noted where they are met.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Smithy half of the same rule the OpenAPI parser applies: a constraint the model declares
    /// and the application does not enforce is a promise to a caller that nothing keeps, and the
    /// build should say so rather than read past it.
    /// </para>
    /// <para>
    /// Both of these are already named in <c>SmithyTraits.Mapped</c>, which is what makes them worth
    /// reporting rather than leaving to the unknown-trait path - the parser declares them as traits
    /// it handles, and then <c>ReadConstraints</c> asks for <c>@length</c>, <c>@range</c> and
    /// <c>@pattern</c> and nothing else. Being on that list and unread is the gap.
    /// </para>
    /// <para>
    /// <c>@default</c> is deliberately absent. It is read - it decides nullability a few lines above
    /// - so it is not this class; that its <i>value</i> never reaches the model is a collapse, and
    /// belongs to the audit that finds those.
    /// </para>
    /// </remarks>
    private static void NoteUnmappedTraits(ParseContext context, JsonElement member, string name) {
        if (SmithyAst.HasTrait(member, SmithyTraits.UniqueItems)) {
            context.Model.UnmappedKeywords.Add(new UnmappedKeywordModel("@uniqueItems", name));
        }

        if (SmithyAst.HasTrait(member, SmithyTraits.Sparse)) {
            context.Model.UnmappedKeywords.Add(new UnmappedKeywordModel("@sparse", name));
        }
    }

    /// <summary>Where a shape id lands in the IR: an inlined primitive, a reference, or a collection.</summary>
    private readonly struct ShapeFacts {
        internal ShapeFacts(
            bool isArray, string? itemType, string? itemRef,
            bool isDictionary, string? valueType, string? valueRef) {
            IsArray = isArray;
            ItemType = itemType;
            ItemRef = itemRef;
            IsDictionary = isDictionary;
            ValueType = valueType;
            ValueRef = valueRef;
        }

        internal bool IsArray { get; }

        internal string? ItemType { get; }

        internal string? ItemRef { get; }

        internal bool IsDictionary { get; }

        internal string? ValueType { get; }

        internal string? ValueRef { get; }
    }

    /// <summary>
    /// Resolves what a target means, inlining everything that is not worth a C# type of its own.
    /// </summary>
    /// <remarks>
    /// Structures, unions and enums become references, because each generates a type. Everything
    /// else is inlined at the use site - a named <c>string</c> with a <c>@pattern</c> is a
    /// <c>string</c> carrying a constraint, not a wrapper type, and a named <c>list</c> is a
    /// <c>List&lt;T&gt;</c>. That mirrors what <c>InlineNonObjectRefs</c> does on the OpenAPI side,
    /// and keeps the generated surface to the shapes a caller actually names.
    /// </remarks>
    private static void Describe(
        ParseContext context,
        string target,
        out string? type,
        out string? format,
        out string? reference,
        out ShapeFacts facts) {
        type = null;
        format = null;
        reference = null;
        facts = default;

        if (SmithyPrelude.TryMap(target, out var preludeType, out var preludeFormat)) {
            type = preludeType;
            format = preludeFormat;

            if (SmithyPrelude.IsLossy(target)) {
                context.Diagnostics.Add(
                    $"'{SmithyPrelude.LocalName(target)}' has no exact C# type; " +
                    "values outside the mapped range will not round-trip.");
            }

            return;
        }

        if (!context.Ast.TryGetShape(target, out var shape)) {
            context.Diagnostics.Add($"'{target}' is referenced but not declared; it becomes JsonElement.");

            return;
        }

        switch (SmithyAst.Kind(shape)) {
            case "structure":
            case "union":
            case "enum":
                reference = ReferenceTo(context, target);

                return;

            case "list":
            case "set":
                facts = ListFacts(context, shape);

                return;

            case "map":
                facts = MapFacts(context, shape);

                return;

            case "intEnum":
                // An int-valued enum is an integer on the wire. Generating a C# enum for it would
                // need member names the model does not supply in the way a string enum does.
                type = "integer";

                return;

            default:
                // A named simple shape - string, integer, timestamp - inlined to what it targets.
                var member = SmithyAst.Kind(shape);

                type = member switch {
                    "string" => "string",
                    "boolean" => "boolean",
                    "byte" or "short" or "integer" => "integer",
                    "long" => "integer",
                    "float" or "double" or "bigDecimal" => "number",
                    "bigInteger" => "integer",
                    "blob" => "string",
                    "timestamp" => "string",
                    _ => null
                };

                format = member switch {
                    "long" or "bigInteger" => "int64",
                    "float" => "float",
                    "double" or "bigDecimal" => "double",
                    "blob" => "byte",
                    "timestamp" => "date-time",
                    _ => null
                };

                return;
        }
    }

    private static ShapeFacts ListFacts(ParseContext context, JsonElement shape) {
        if (!shape.TryGetProperty("member", out var member)) {
            return new ShapeFacts(true, null, null, false, null, null);
        }

        var target = SmithyAst.Target(member);

        if (target == null) {
            return new ShapeFacts(true, null, null, false, null, null);
        }

        Describe(context, target, out var type, out _, out var reference, out _);

        return new ShapeFacts(true, type, reference, false, null, null);
    }

    private static ShapeFacts MapFacts(ParseContext context, JsonElement shape) {
        if (!shape.TryGetProperty("value", out var value)) {
            return new ShapeFacts(false, null, null, true, null, null);
        }

        var target = SmithyAst.Target(value);

        if (target == null) {
            return new ShapeFacts(false, null, null, true, null, null);
        }

        Describe(context, target, out var type, out _, out var reference, out _);

        return new ShapeFacts(false, null, null, true, type, reference);
    }

    /// <summary>
    /// Names a shape, building its schema the first time it is reached.
    /// </summary>
    /// <remarks>
    /// References are written with <c>TypeMapper.MakeRef</c> rather than spelled inline, because the
    /// passes that rewrite references - the name allocator renaming a type, the slicer walking to a
    /// derived one - read them with <c>GetRefName</c>. The form is the spine's to define; this front
    /// end only has to agree.
    /// </remarks>
    private static string ReferenceTo(ParseContext context, string shapeId) {
        var name = SmithyPrelude.LocalName(shapeId);

        if (context.Built.Add(shapeId)) {
            BuildSchema(context, shapeId, name);
        }

        return TypeMapper.MakeRef(name);
    }

    private static void BuildSchema(ParseContext context, string shapeId, string name) {
        if (!context.Ast.TryGetShape(shapeId, out var shape)) {
            return;
        }

        Note(context, shape);

        var schema = new SchemaModel {
            Name = name,
            Description = Text(shape, SmithyTraits.Documentation),
            IsDeprecated = SmithyAst.HasTrait(shape, SmithyTraits.Deprecated)
        };

        switch (SmithyAst.Kind(shape)) {
            case "enum":
                schema.Kind = SchemaKind.Enum;

                foreach (var member in SmithyAst.Members(shape)) {
                    // Smithy supplies both halves: the member name is the C# identifier and
                    // @enumValue is the wire value. OpenAPI has only the latter and the allocator
                    // has to invent the former.
                    schema.EnumMemberNames.Add(NamingHelper.ToPascalCase(member.Key));
                    schema.EnumValues.Add(
                        SmithyAst.TryGetTrait(member.Value, SmithyTraits.EnumValue, out var value) &&
                        value.ValueKind == JsonValueKind.String
                            ? value.GetString() ?? member.Key
                            : member.Key);
                }

                break;

            case "union":
                schema.Kind = SchemaKind.OneOf;

                foreach (var member in SmithyAst.Members(shape)) {
                    var target = SmithyAst.Target(member.Value);

                    if (target == null) {
                        continue;
                    }

                    Describe(context, target, out var type, out var format, out var reference, out _);

                    schema.OneOf.Add(reference != null
                        ? new ChoiceBranchModel { Ref = reference }
                        : new ChoiceBranchModel { Type = type, Format = format });
                }

                break;

            default:
                schema.Kind = SchemaKind.Object;

                foreach (var member in SmithyAst.Members(shape)) {
                    Note(context, member.Value);

                    var property = BuildProperty(context, member.Key, member.Value);

                    schema.Properties.Add(property);

                    if (property.IsRequired) {
                        schema.Required.Add(property.Name);
                    }
                }

                break;
        }

        context.Model.Schemas.Add(schema);
    }

    /// <summary>
    /// The constraint traits, read once, before anything knows which model will carry them.
    /// </summary>
    /// <remarks>
    /// <see cref="PropertyModel"/> and <see cref="ParameterModel"/> hold an identical set of these
    /// and share nothing else - which is what <see cref="IConstraintFacets"/> exists to say. The
    /// facets are get-only there, so reading into this and applying it twice is what keeps the two
    /// call sites from drifting: a constraint added here reaches both or neither.
    /// </remarks>
    private readonly struct Constraints {
        internal int? MinLength { get; init; }

        internal int? MaxLength { get; init; }

        internal int? MinItems { get; init; }

        internal int? MaxItems { get; init; }

        internal decimal? Minimum { get; init; }

        internal decimal? Maximum { get; init; }

        internal string? Pattern { get; init; }

        internal Constraints Merge(Constraints other) => new() {
            MinLength = MinLength ?? other.MinLength,
            MaxLength = MaxLength ?? other.MaxLength,
            MinItems = MinItems ?? other.MinItems,
            MaxItems = MaxItems ?? other.MaxItems,
            Minimum = Minimum ?? other.Minimum,
            Maximum = Maximum ?? other.Maximum,
            Pattern = Pattern ?? other.Pattern
        };
    }

    /// <summary>
    /// Reads the constraint traits from a member and from the shape it targets.
    /// </summary>
    /// <remarks>
    /// A constraint may sit on either and means the same thing. The member wins where both declare
    /// one, which is what Smithy says, and is why the member is merged over the shape rather than
    /// under it.
    /// </remarks>
    private static Constraints ReadConstraints(
        ParseContext context, JsonElement member, string? target) {
        var collection = IsCollection(context, target);
        var fromMember = ReadFrom(member, collection);

        if (target == null || !context.Ast.TryGetShape(target, out var shape)) {
            return fromMember;
        }

        return fromMember.Merge(ReadFrom(shape, collection));
    }

    private static Constraints ReadFrom(JsonElement source, bool collection) {
        if (source.ValueKind != JsonValueKind.Object) {
            return default;
        }

        int? minLength = null, maxLength = null, minItems = null, maxItems = null;

        if (SmithyAst.TryGetTrait(source, SmithyTraits.Length, out var length)) {
            // @length bounds a string's characters and a list's or map's entries. Which pair of IR
            // fields that is depends on what the member is, and getting it wrong emits a validator
            // that reads .Count off a string - which is the case TypeMapper.HasItemCount guards.
            if (collection) {
                minItems = Int(length, "min");
                maxItems = Int(length, "max");
            } else {
                minLength = Int(length, "min");
                maxLength = Int(length, "max");
            }
        }

        decimal? minimum = null, maximum = null;

        if (SmithyAst.TryGetTrait(source, SmithyTraits.Range, out var range)) {
            minimum = Decimal(range, "min");
            maximum = Decimal(range, "max");
        }

        return new Constraints {
            MinLength = minLength,
            MaxLength = maxLength,
            MinItems = minItems,
            MaxItems = maxItems,
            Minimum = minimum,
            Maximum = maximum,
            Pattern = SmithyAst.TryGetTrait(source, SmithyTraits.Pattern, out var pattern) &&
                      pattern.ValueKind == JsonValueKind.String
                ? pattern.GetString()
                : null
        };
    }

    private static void ApplyTo(Constraints constraints, PropertyModel model) {
        model.MinLength = constraints.MinLength;
        model.MaxLength = constraints.MaxLength;
        model.MinItems = constraints.MinItems;
        model.MaxItems = constraints.MaxItems;
        model.Minimum = constraints.Minimum;
        model.Maximum = constraints.Maximum;
        model.Pattern = constraints.Pattern;
    }

    private static void ApplyTo(Constraints constraints, ParameterModel model) {
        model.MinLength = constraints.MinLength;
        model.MaxLength = constraints.MaxLength;
        model.MinItems = constraints.MinItems;
        model.MaxItems = constraints.MaxItems;
        model.Minimum = constraints.Minimum;
        model.Maximum = constraints.Maximum;
        model.Pattern = constraints.Pattern;
    }

    private static bool IsCollection(ParseContext context, string? target) {
        if (target == null || !context.Ast.TryGetShape(target, out var shape)) {
            return false;
        }

        return SmithyAst.Kind(shape) is "list" or "set" or "map";
    }

    /// <summary>Records every trait a shape carries, for the report at the end.</summary>
    private static void Note(ParseContext context, JsonElement shape) {
        foreach (var trait in SmithyAst.Traits(shape)) {
            context.SeenTraits.Add(trait.Key);
        }
    }

    /// <summary>
    /// Says what the model asked for and did not get.
    /// </summary>
    /// <remarks>
    /// The point of an allowlist rather than a blanket ignore. A trait nobody has classified is
    /// reported once, by name, so a model using a feature this does not implement says so at build
    /// time instead of producing a server that quietly disagrees with its own description.
    /// </remarks>
    private static void ReportUnhandledTraits(ParseContext context) {
        var unhandled = new List<string>();

        foreach (var trait in context.SeenTraits) {
            if (SmithyTraits.IsAccountedFor(trait)) {
                continue;
            }

            // A custom trait is the model's own extension point rather than something missing, and
            // is reported separately by the filter pass when one is wired to it.
            if (!SmithyPrelude.IsPrelude(trait)) {
                continue;
            }

            unhandled.Add(trait);
        }

        unhandled.Sort(StringComparer.Ordinal);

        foreach (var trait in unhandled) {
            context.Diagnostics.Add(
                $"the model applies '{trait}', which this generator does not model; it was ignored.");
        }

        foreach (var trait in context.SeenTraits) {
            if (SmithyTraits.Degrades.Contains(trait)) {
                context.Diagnostics.Add(
                    $"the model applies '{trait}', which has no equivalent in the generated code.");
            }
        }
    }

    private static string? String(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Int(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static decimal? Decimal(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal()
            : null;

    /// <summary>A trait whose value is a bare string - @documentation, @title.</summary>
    private static string? Text(JsonElement shape, string traitId) =>
        SmithyAst.TryGetTrait(shape, traitId, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? JsonName(JsonElement member) =>
        Text(member, SmithyTraits.JsonName);
}
