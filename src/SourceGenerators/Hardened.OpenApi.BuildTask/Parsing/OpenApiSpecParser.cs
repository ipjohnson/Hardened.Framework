using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;
using Hardened.Idl.Models;
using Hardened.Idl;

namespace Hardened.OpenApi.SourceGenerator;

internal static class OpenApiSpecParser {
    /// <summary>
    /// Readers for both formats a specification is written in.
    /// </summary>
    /// <remarks>
    /// From 2.0 the core package reads JSON and nothing else; YAML ships separately and has to be
    /// added before a YAML document will parse at all. Both are first-class here - the Petstore and
    /// Slack descriptions are JSON, everything else tested is YAML.
    /// </remarks>
    private static OpenApiReaderSettings ReaderSettings(string? externalRefRoot) {
        var settings = new OpenApiReaderSettings();

        settings.AddYamlReader();

        if (externalRefRoot != null) {
            settings.LoadExternalRefs = true;

            // A file URI, so relative references resolve beside the specification rather than
            // against the process's working directory or, worse, over the network.
            settings.BaseUrl = new Uri(
                externalRefRoot.EndsWith("/", StringComparison.Ordinal)
                    ? externalRefRoot
                    : externalRefRoot + "/");
        }

        return settings;
    }

    /// <summary>
    /// JSON or YAML, decided from the document rather than from its file name.
    /// </summary>
    /// <remarks>
    /// Both are first-class: the Petstore and Slack descriptions are JSON, everything else tested is
    /// YAML. Named explicitly rather than left to the reader to infer, so a document that opens with
    /// a comment or a byte order mark cannot be mistaken for the other format.
    /// </remarks>
    private static string DetectFormat(string text) {
        foreach (var character in text) {
            if (char.IsWhiteSpace(character) || character == '﻿') {
                continue;
            }

            // JSON is the only one of the two that can begin with a brace, and YAML documents that
            // begin with '{' are flow-style JSON anyway - which this reader also accepts.
            return character == '{' ? OpenApiConstants.Json : OpenApiConstants.Yaml;
        }

        return OpenApiConstants.Yaml;
    }
    /// <param name="diagnostics">
    /// Reasons, when there are any. The reader knows exactly what is wrong - an unsupported
    /// specification version, an unresolved reference - and its message was previously the only
    /// description of the failure anyone would have got, discarded along with the exception. A
    /// caller that passes nothing still behaves as before.
    /// </param>
    /// <param name="groupUntaggedByPath">
    /// Whether operations with no tag are grouped by first path segment rather than onto one
    /// service. See <see cref="UntaggedGroup"/>.
    /// </param>
    /// <param name="externalRefRoot">
    /// The directory external <c>$ref</c>s resolve against, or null to leave them unresolved.
    /// Opt in: a document may reference any URL, and a build that reaches the network is neither
    /// reproducible nor safe to run against a description someone else controls. Passing a
    /// directory restricts resolution to files beside the specification.
    /// </param>
    public static ServiceSpecModel? Parse(
        string text, string fileName, CancellationToken cancellationToken,
        bool applyServerBasePath = false, ICollection<string>? diagnostics = null,
        bool groupUntaggedByPath = false, string? externalRefRoot = null) {
        cancellationToken.ThrowIfCancellationRequested();

        OpenApiDocument? document;
        try {
            var settings = ReaderSettings(externalRefRoot);
            var format = DetectFormat(text);

            // External references are only followed by the asynchronous reader - the synchronous
            // one refuses outright. Blocking on it is safe here and nowhere near a hot path: an
            // MSBuild task's Execute is synchronous by contract, the task runs in its own host
            // process with no synchronisation context to deadlock against, and it runs once.
            var result = externalRefRoot == null
                ? OpenApiDocument.Parse(text, format, settings)
                : OpenApiDocument
                    .LoadAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)), format, settings,
                        cancellationToken)
                    .GetAwaiter().GetResult();

            document = result.Document;

            if (result.Diagnostic?.Errors != null) {
                foreach (var error in result.Diagnostic.Errors) {
                    diagnostics?.Add(
                        string.IsNullOrEmpty(error.Pointer)
                            ? error.Message
                            : error.Pointer + ": " + error.Message);
                }
            }
        } catch (Exception exception) {
            diagnostics?.Add(exception.Message);
            return null;
        }

        if (document == null) {
            return null;
        }

        var model = new ServiceSpecModel { FileName = fileName };

        // Schemas the document does not name, lifted out of the places they were written inline.
        // Collected separately and appended, so a synthesized name colliding with a declared one is
        // visible to SpecDiagnostics as a duplicate rather than quietly overwriting it.
        var synthesized = new SchemaCollector(document.Components?.Schemas?.Keys);

        if (document.Components?.Schemas != null) {
            foreach (var kvp in document.Components.Schemas) {
                cancellationToken.ThrowIfCancellationRequested();
                var schema = ParseSchema(kvp.Key, kvp.Value, synthesized);
                if (schema != null) {
                    model.Schemas.Add(schema);
                }
            }
        }

        // Parse x-filter-types extension
        // Whole-service, and at the root because that is the only place a document addresses the
        // service rather than an operation. What an operation produces is per operation; what
        // happens when a client asks for something outside that set is one answer for all of them.
        if (document.Extensions != null &&
            document.Extensions.TryGetValue("x-hardened-content-negotiation", out var negotiationExt) &&
            negotiationExt is JsonNodeExtension { Node: JsonValue negotiationValue } &&
            negotiationValue.GetValueKind() == JsonValueKind.String) {
            model.ContentNegotiation = negotiationValue.GetValue<string>().Trim().ToLowerInvariant();
        }

        if (document.Extensions != null &&
            document.Extensions.TryGetValue("x-filter-types", out var filterTypesExt) &&
            filterTypesExt is JsonNodeExtension { Node: JsonObject filterTypesObj }) {
            foreach (var kvp in filterTypesObj) {
                cancellationToken.ThrowIfCancellationRequested();
                var filterType = ParseFilterType(kvp.Key, kvp.Value);
                if (filterType != null) {
                    model.FilterTypes.Add(filterType);
                }
            }
        }

        var operationsByTag = new Dictionary<string, List<OperationModel>>();

        var basePath = applyServerBasePath ? ServerBasePath(document) : "";

        if (document.Paths != null) {
            foreach (var pathKvp in document.Paths) {
                cancellationToken.ThrowIfCancellationRequested();
                ParsePath(basePath + pathKvp.Key, pathKvp.Value, operationsByTag, synthesized,
                    groupUntaggedByPath, document, diagnostics);
            }
        }

        foreach (var schema in synthesized.Synthesized) {
            model.Schemas.Add(schema);
        }

        foreach (var kvp in operationsByTag) {
            model.Services.Add(new ServiceModel {
                Tag = kvp.Key,
                Operations = kvp.Value
            });
        }

        // Order matters, and only here. References are cleared before names are allocated so the
        // allocator never names a type nothing will emit; base types are dropped before that
        // because a dropped base changes which members a type declares. Choices go first of all,
        // because dropping one is another way a reference stops naming something.
        DropUndecidableChoices(model);
        InlineNonObjectRefs(model);
        DropIncompatibleBaseTypes(model);
        NameAllocator.Apply(model, fileName);

        return model;
    }

    /// <summary>
    /// Inheritance a record cannot express, dropped in favour of the derived type's own shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A derived schema may narrow a property the base left loose - Jira's
    /// <c>ProjectIdAssociationContext</c> declares <c>identifier</c> as an integer where its base
    /// declares a <c>oneOf</c>, which reaches C# as <c>JsonElement</c>. That is ordinary in a
    /// document and impossible in a record: a positional parameter must match the inherited member
    /// exactly, so the narrowed one is CS8866.
    /// </para>
    /// <para>
    /// The base relationship goes rather than the narrowing. <c>allOf</c> has already merged every
    /// branch's properties into the derived schema, so it carries the whole shape either way - what
    /// is lost is the type relationship, not any of the data.
    /// </para>
    /// </remarks>
    private static void DropIncompatibleBaseTypes(ServiceSpecModel model) {
        var byName = new Dictionary<string, SchemaModel>(StringComparer.Ordinal);

        foreach (var schema in model.Schemas) {
            byName[schema.Name] = schema;
        }

        foreach (var schema in model.Schemas) {
            if (schema.BaseRef == null ||
                !byName.TryGetValue(TypeMapper.GetRefName(schema.BaseRef), out var baseSchema)) {
                continue;
            }

            foreach (var property in schema.Properties) {
                var inherited = baseSchema.Properties.Find(p => p.MemberName == property.MemberName);

                if (inherited == null) {
                    continue;
                }

                if (TypeMapper.MapPropertyToCSharpType(inherited) !=
                        TypeMapper.MapPropertyToCSharpType(property) ||
                    inherited.IsCSharpNullable != property.IsCSharpNullable) {
                    schema.BaseRef = null;
                    break;
                }
            }
        }
    }





    /// <summary>Points every reference at a renamed schema's new name.</summary>

    /// <summary>
    /// A short, stable qualifier for a name that has to differ from another.
    /// </summary>
    /// <remarks>
    /// FNV-1a, for the reason <c>PatternRegistry</c> gives: <see cref="string.GetHashCode"/> is
    /// randomised per process in .NET Core, and a generated type that renames itself on every build
    /// churns the file and recompiles every consumer. Derived from provenance rather than from the
    /// order things were parsed in, so reordering a document cannot rename a type.
    /// </remarks>

    /// <summary>
    /// Rewrites references to top-level array schemas into the array they stand for.
    /// </summary>
    /// <remarks>
    /// A schema declared at the top level as <c>type: array</c> is an alias for a list, not a type
    /// of its own, and no record is emitted for it. Anything referencing it was still typed by the
    /// reference's name, so Slack's <c>blocks</c> produced a property of type <c>Blocks</c> that
    /// nothing declared - CS0246 in a generated file.
    ///
    /// <para>
    /// Done as a pass over the finished model rather than at the point each property is parsed,
    /// because a reference can be read before the schema it names has been.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Removes a <c>oneOf</c> whose branches cannot be told apart, leaving the property loose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pass rather than a check at the point of synthesis, because deciding this needs every
    /// branch schema and a branch may be declared after the property referring to it.
    /// </para>
    /// <para>
    /// The alternative to dropping it is to generate a type that guesses - try each branch and keep
    /// the first that reads - and a guess is exactly what a generated type should not contain. Of
    /// the published corpus's 338 undiscriminated choices, 84% are decided by a test on the value's
    /// kind or its properties; the rest are pairs nothing separates, and for those a
    /// <c>JsonElement</c> the caller inspects is more honest than a type that picks one.
    /// </para>
    /// </remarks>
    private static void DropUndecidableChoices(ServiceSpecModel model) {
        var dropped = new HashSet<string>(StringComparer.Ordinal);

        // The schemas that will emit a type, which is not every schema in the model: an array
        // alias or a primitive one is a name for something else and produces no record, so a branch
        // naming one names nothing the converter can read into.
        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var schema in model.Schemas) {
            if (schema.Kind is SchemaKind.Object or SchemaKind.Enum or SchemaKind.OneOf) {
                declared.Add(schema.Name);
            }
        }

        foreach (var schema in model.Schemas) {
            if (schema.Kind != SchemaKind.OneOf) {
                continue;
            }

            // A branch naming a schema nothing declares cannot be read into anything, and the
            // converter would name a type that does not exist - CS0234 in a generated file.
            // Checked for every choice, discriminated or not: a discriminator says which branch a
            // payload is, not that the branch was generated.
            schema.OneOf.RemoveAll(
                branch => branch.Ref != null && !declared.Contains(TypeMapper.GetRefName(branch.Ref)));
            schema.DiscriminatorMapping.RemoveAll(
                mapping => !declared.Contains(TypeMapper.GetRefName(mapping.Ref)));

            if (schema.OneOf.Count < 2 ||
                (schema.DiscriminatorPropertyName == null &&
                 !ChoiceResolution.Resolve(schema.OneOf, model.Schemas).Usable)) {
                dropped.Add(schema.Name);
            }
        }

        if (dropped.Count == 0) {
            return;
        }

        model.Schemas.RemoveAll(schema => dropped.Contains(schema.Name));

        // The property keeps its OneOfRefs, so the branches stay reachable and the diagnostic can
        // still say which schemas it was choosing between.
        foreach (var reference in ModelRefs.All(model)) {
            if (reference.Value != null &&
                dropped.Contains(TypeMapper.GetRefName(reference.Value))) {
                reference.Set(null);
            }
        }
    }

    private static void InlineNonObjectRefs(ServiceSpecModel model) {
        // Only objects, enums and choice types become types. A reference to anything else - a
        // top-level array alias, an anyOf with no shape of its own, a schema this parser could not
        // read - was still typed by the reference's name, naming something nothing declares.
        // Slack's `blocks`, GitHub's `code-frequency-stat` and Stripe's `external_account` were all
        // CS0246.
        var emittable = new HashSet<string>();
        var arrays = new Dictionary<string, SchemaModel>();

        foreach (var schema in model.Schemas) {
            if (schema.Kind == SchemaKind.Object || schema.Kind == SchemaKind.Enum ||
                schema.Kind == SchemaKind.OneOf) {
                emittable.Add(schema.Name);
            }

            if (schema.Kind == SchemaKind.Array) {
                arrays[schema.Name] = schema;
            }
        }

        bool Missing(string? reference) =>
            reference != null && !emittable.Contains(TypeMapper.GetRefName(reference));

        SchemaModel? Array(string? reference) =>
            reference != null && arrays.TryGetValue(TypeMapper.GetRefName(reference), out var found)
                ? found
                : null;

        foreach (var schema in model.Schemas) {
            foreach (var property in schema.Properties) {
                var target = Array(property.Ref);

                if (target != null) {
                    // An alias for a list, so the property becomes that list.
                    property.Ref = null;
                    property.IsArray = true;
                    property.ArrayItemsRef = target.ArrayItemsRef;
                    property.ArrayItemsType = target.ArrayItemsType;
                    property.ArrayItemsFormat = target.ArrayItemsFormat;
                } else if (Missing(property.Ref)) {
                    property.Ref = null;
                }

                // Nested arrays and unreadable element types both fall back to JsonElement, which
                // is what the mapper already does for an element it cannot name.
                if (Missing(property.ArrayItemsRef)) property.ArrayItemsRef = null;
                if (Missing(property.DictionaryValueRef)) property.DictionaryValueRef = null;
            }
        }

        // Every schema that carries a reference, not only properties. A base type, a discriminator
        // branch, a parameter and a declared error response all name a type in generated code, and
        // Cloudflare and PagerDuty reference hundreds of schemas that produce none - each one a
        // CS0234 naming something nothing declares.
        foreach (var schema in model.Schemas) {
            if (Missing(schema.BaseRef)) schema.BaseRef = null;

            schema.DiscriminatorMapping.RemoveAll(mapping => Missing(mapping.Ref));
        }

        foreach (var service in model.Services) {
            foreach (var operation in service.Operations) {
                if (Missing(operation.RequestBodyRef)) operation.RequestBodyRef = null;

                foreach (var parameter in operation.Parameters) {
                    if (Missing(parameter.Ref)) parameter.Ref = null;
                    if (Missing(parameter.ArrayItemsRef)) parameter.ArrayItemsRef = null;
                }

                // A success whose schema does not exist loses its body rather than its status: the
                // status is declared and still has to be answerable, so the case becomes a bodyless
                // one. Errors are removed outright because an error case with no body and no status
                // of its own carries nothing at all.
                foreach (var success in operation.SuccessResponses) {
                    if (Missing(success.Ref)) success.Ref = null;
                    if (Missing(success.ArrayItemsRef)) {
                        success.ArrayItemsRef = null;
                        success.IsArray = false;
                    }
                }

                operation.ErrorResponses.RemoveAll(error => Missing(error.Ref));

                var response = Array(operation.ResponseRef);

                if (response != null) {
                    operation.ResponseRef = null;
                    operation.ResponseIsArray = true;
                    operation.ResponseArrayItemsRef = response.ArrayItemsRef;
                    operation.ResponseArrayItemsType = response.ArrayItemsType;
                    operation.ResponseArrayItemsFormat = response.ArrayItemsFormat;
                } else if (Missing(operation.ResponseRef)) {
                    operation.ResponseRef = null;
                }

                if (Missing(operation.ResponseArrayItemsRef)) {
                    operation.ResponseArrayItemsRef = null;
                    operation.ResponseIsArray = false;
                }
            }
        }
    }

    /// <summary>
    /// The path component of the first <c>servers</c> entry, or the empty string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Opt-in, and off by default. A specification's paths are relative to its server URL, so a
    /// <c>/v1</c> there means every route is under <c>/v1</c> - but plenty of deployments strip that
    /// prefix at the gateway, and applying it unasked would silently double it. Being wrong in that
    /// direction is the same class of failure this work exists to remove, so the caller says.
    /// </para>
    /// <para>
    /// An unresolved <c>{variable}</c> is dropped rather than emitted: the route tree compiles a
    /// path into character comparisons and cannot match a brace it was never told about. Variables
    /// with declared defaults are substituted first.
    /// </para>
    /// </remarks>
    private static string ServerBasePath(OpenApiDocument document) {
        var server = document.Servers?.FirstOrDefault();

        // Pattern-matched rather than a null check on Url: .NET Framework's reference assemblies
        // do not carry the NotNullWhen annotation on string.IsNullOrWhiteSpace, so guarding with it
        // alone leaves url nullable on that leg and the build warns where the other does not.
        if (server?.Url is not { } url || string.IsNullOrWhiteSpace(url)) {
            return "";
        }

        if (server.Variables != null) {
            foreach (var variable in server.Variables) {
                var value = variable.Value?.Default;

                if (!string.IsNullOrEmpty(value)) {
                    url = url.Replace("{" + variable.Key + "}", value);
                }
            }
        }

        // An absolute URL contributes only its path.
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);

        if (schemeEnd >= 0) {
            var afterAuthority = url.IndexOf('/', schemeEnd + 3);

            url = afterAuthority < 0 ? "" : url.Substring(afterAuthority);
        }

        url = url.TrimEnd('/');

        // A server that is only a host contributes nothing, and prefixing "/" would produce "//".
        // A variable nobody gave a default to would reach the route tree as a literal brace.
        if (url.Length == 0 || url.IndexOf('{') >= 0) {
            return "";
        }

        return url.StartsWith("/") ? url : "/" + url;
    }

    private static SchemaModel? ParseSchema(
        string name, IOpenApiSchema schema, SchemaCollector collector) {
        var model = ParseSchemaKind(name, schema, collector);

        if (model != null) {
            model.Description = FirstNonEmpty(schema.Description);
            model.IsDeprecated = schema.Deprecated;

            ParsePolymorphism(schema, model);
        }

        return model;
    }

    /// <summary>
    /// The discriminator, and the base an <c>allOf</c> points at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OpenAPI writes inheritance two ways and this reads both. A schema carrying a
    /// <c>discriminator</c> is the base of a hierarchy: its <c>oneOf</c> branches, or its
    /// <c>mapping</c>, name the types that belong to it. A schema whose <c>allOf</c> references such
    /// a base is one of those types.
    /// </para>
    /// <para>
    /// Both were dropped entirely before this. A <c>oneOf</c> schema matched none of the parser's
    /// shapes, so it became a <c>Primitive</c> with a null type and every property referencing it
    /// mapped to <c>JsonElement</c> - the spec described a closed set of shapes and the generated
    /// code took an untyped blob.
    /// </para>
    /// </remarks>
    private static void ParsePolymorphism(IOpenApiSchema schema, SchemaModel model) {
        if (schema.Discriminator != null &&
            !string.IsNullOrEmpty(schema.Discriminator.PropertyName)) {
            model.DiscriminatorPropertyName = schema.Discriminator.PropertyName;

            if (schema.Discriminator.Mapping != null) {
                foreach (var mapping in schema.Discriminator.Mapping) {
                    model.DiscriminatorMapping.Add(new DiscriminatorMappingModel {
                        Value = mapping.Key,
                        Ref = mapping.Value?.Reference?.ReferenceV3 ?? ""
                    });
                }
            }

            // No explicit mapping means the branches are the derived types, keyed by their own
            // names - which is what the specification says a bare discriminator implies.
            if (model.DiscriminatorMapping.Count == 0 && schema.OneOf is { Count: > 0 }) {
                foreach (var branch in schema.OneOf) {
                    var reference = SchemaRef(branch);

                    if (reference != null) {
                        model.DiscriminatorMapping.Add(new DiscriminatorMappingModel {
                            Value = TypeMapper.GetRefName(reference),
                            Ref = reference
                        });
                    }
                }
            }
        }

        if (schema.AllOf is { Count: > 0 }) {
            foreach (var branch in schema.AllOf) {
                if (SchemaRef(branch) != null && branch.Discriminator != null) {
                    model.BaseRef = SchemaRef(branch);
                    break;
                }
            }
        }
    }

    private static SchemaModel? ParseSchemaKind(
        string name, IOpenApiSchema schema, SchemaCollector collector) {
        if (schema.Enum is { Count: > 0 }) {
            var enumModel = new SchemaModel {
                Name = name,
                Kind = SchemaKind.Enum,

                // The wire type travels with the values, because the emitters cannot tell a
                // quoted member from an unquoted one without it - a string enum writes
                // "science-fiction" and an integer enum writes 3.
                Type = EnumMemberType(schema.Enum),
                EnumValues = schema.Enum
                    .Select(EnumMember)
                        .Where(value => value != null)
                        .Select(value => value!)
                    .ToList()
            };

            var declaredNames = EnumMemberNames(schema);

            if (declaredNames != null && declaredNames.Count == enumModel.EnumValues.Count) {
                enumModel.EnumMemberNames.AddRange(declaredNames);
                enumModel.EnumMemberNamesAreDeclared = true;
            }

            return enumModel;
        }

        if (schema.AllOf is { Count: > 0 }) {
            return ParseAllOf(name, schema, collector);
        }

        // A component whose whole definition is a choice becomes the type that holds one of its
        // branches - the same type a property declaring one inline gets, and for the same reason.
        //
        // Both spellings used to end somewhere unusable. Without a discriminator it matched none of
        // the shapes below and fell through to Primitive with a null type, so every property naming
        // it read as JsonElement and an operation returning it lost its response type altogether.
        // With one it came here and became an object, which is the worse of the two: a bare oneOf
        // declares no properties, so the type generated empty and a payload deserialized into
        // nothing at all - silently, behind a build that stayed green.
        //
        // Properties of its own are the case that is still an object. A oneOf beside properties is
        // a base carrying shared members, which is a hierarchy rather than a choice.
        if (schema.OneOf is { Count: > 0 } &&
            (schema.Properties == null || schema.Properties.Count == 0)) {
            var choice = ParseComponentChoice(name, schema);

            if (choice != null) {
                return choice;
            }
        }

        // A oneOf naming its branches, with a discriminator to choose between them, is the base of
        // a hierarchy. Reached now only when the schema declares properties of its own, or when the
        // branches left no choice to make.
        if (schema.OneOf is { Count: > 0 } && schema.Discriminator != null) {
            return ParseObjectSchema(name, schema, collector);
        }

        if (SchemaType(schema) == "object" || schema.Properties is { Count: > 0 }) {
            if (schema.AdditionalProperties != null && (schema.Properties == null || schema.Properties.Count == 0)) {
                return ParseDictionarySchema(name, schema);
            }
            return ParseObjectSchema(name, schema, collector);
        }

        if (SchemaType(schema) == "array") {
            return ParseArraySchema(name, schema);
        }

        return new SchemaModel {
            Name = name,
            Kind = SchemaKind.Primitive,
            Type = SchemaType(schema),
            Format = schema.Format
        };
    }

    /// <summary>
    /// A component that is a choice between named schemas, or null when there is no choice left to
    /// make.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fewer than two branches is not a choice: one means the schema is an alias for that branch,
    /// and none means every branch typed to nothing - <c>{}</c> or 3.1's <c>{type: "null"}</c>,
    /// which <see cref="CollectBranches"/> drops. Both return null and fall through to the ordinary
    /// shapes rather than producing a type that holds one thing or nothing, which is the same rule
    /// a property declaring the same <c>oneOf</c> follows.
    /// </para>
    /// <para>
    /// The discriminator is deliberately not read here. <c>ParseSchema</c> runs
    /// <c>ParsePolymorphism</c> over every schema this returns, and that already reads
    /// <c>propertyName</c>, an explicit <c>mapping</c>, and the bare form the specification says
    /// keys each branch by its own name. Reading it twice would be two places to disagree.
    /// </para>
    /// <para>
    /// Named by the component rather than by its branches, so it needs no allocation here: the name
    /// is the one the document gave it. <c>NameAllocator</c> reserves the converter beside it, the
    /// same as for a synthesized choice.
    /// </para>
    /// </remarks>
    private static SchemaModel? ParseComponentChoice(string name, IOpenApiSchema schema) {
        var branches = new List<ChoiceBranchModel>();

        CollectBranches(schema.OneOf, branches);

        if (branches.Count < 2) {
            return null;
        }

        return new SchemaModel {
            Name = name,
            Kind = SchemaKind.OneOf,
            OneOf = branches
        };
    }

    private static SchemaModel ParseObjectSchema(
        string name, IOpenApiSchema schema, SchemaCollector collector) {
        var model = new SchemaModel {
            Name = name,
            Kind = SchemaKind.Object,
            Required = schema.Required?.ToList() ?? new List<string>()
        };

        if (schema.Properties != null) {
            foreach (var propKvp in schema.Properties) {
                model.Properties.Add(ParseProperty(propKvp.Key, propKvp.Value,
                    model.Required.Contains(propKvp.Key), name, collector));
            }
        }

        return model;
    }

    private static SchemaModel ParseAllOf(
        string name, IOpenApiSchema schema, SchemaCollector collector) {
        var model = new SchemaModel {
            Name = name,
            Kind = SchemaKind.Object,
            Required = schema.Required?.ToList() ?? new List<string>()
        };

        MergeBranches(schema, model, name, collector, new HashSet<string>(StringComparer.Ordinal));

        return model;
    }

    /// <summary>
    /// Folds every branch of an <c>allOf</c> into the derived schema, following branches that are
    /// themselves compositions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The recursion is the point. A branch that is itself an <c>allOf</c> declares no properties of
    /// its own - they are in <em>its</em> branches - so reading only the direct ones merges nothing
    /// from it and loses everything above it. Box builds every resource that way:
    /// <c>File--Full</c> extends <c>File</c>, which extends <c>File--Mini</c>, which extends
    /// <c>File--Base</c>, and <c>id</c>, <c>type</c> and <c>etag</c> are declared at the bottom. They
    /// were absent from the generated type entirely, which replaying Box's own published examples is
    /// what surfaced.
    /// </para>
    /// <para>
    /// Depth first, so the deepest ancestor is merged before anything that narrows it and
    /// <see cref="MergeProperty"/> sees them in the order the document layers them.
    /// </para>
    /// </remarks>
    private static void MergeBranches(
        IOpenApiSchema schema, SchemaModel model, string name, SchemaCollector collector,
        HashSet<string> visited) {
        foreach (var branch in schema.AllOf ?? Enumerable.Empty<IOpenApiSchema>()) {
            // A composition may name itself somewhere up its own chain, and a document is not
            // required to be acyclic just because a type system is.
            var reference = SchemaRef(branch);

            if (reference != null && !visited.Add(reference)) {
                continue;
            }

            MergeBranches(branch, model, name, collector, visited);

            if (branch.Required != null) {
                foreach (var required in branch.Required) {
                    if (!model.Required.Contains(required)) {
                        model.Required.Add(required);
                    }
                }
            }

            if (branch.Properties == null) {
                continue;
            }

            foreach (var propKvp in branch.Properties) {
                var isRequired = model.Required.Contains(propKvp.Key) ||
                                 (branch.Required?.Contains(propKvp.Key) ?? false);
                var parsed = ParseProperty(propKvp.Key, propKvp.Value, isRequired, name, collector);

                // allOf is an intersection, so a property named by more than one branch is one
                // property described twice - most often a base declaring it and a later branch
                // narrowing a constraint on it. Appending both produced two record parameters of
                // the same name: CS0100 and CS0102, from a document that is entirely legal.
                var existing = model.Properties.FindIndex(p => p.Name == parsed.Name);

                if (existing >= 0) {
                    model.Properties[existing] = MergeProperty(model.Properties[existing], parsed);
                } else {
                    model.Properties.Add(parsed);
                }
            }
        }
    }

    /// <summary>
    /// One property described by two <c>allOf</c> branches, folded into one.
    /// </summary>
    /// <remarks>
    /// Later branches win on anything they state, which is what narrowing means: a branch that
    /// repeats a property to add <c>maxLength</c> is refining the earlier declaration, not replacing
    /// it. Anything the later branch is silent about keeps the earlier value, so the type and
    /// description a base declared survive a branch that only tightens a bound.
    /// </remarks>
    private static PropertyModel MergeProperty(PropertyModel first, PropertyModel second) {
        return new PropertyModel {
            Name = first.Name,
            Type = second.Type ?? first.Type,
            Format = second.Format ?? first.Format,
            Ref = second.Ref ?? first.Ref,
            IsArray = second.IsArray || first.IsArray,
            ArrayItemsRef = second.ArrayItemsRef ?? first.ArrayItemsRef,
            ArrayItemsType = second.ArrayItemsType ?? first.ArrayItemsType,
            ArrayItemsFormat = second.ArrayItemsFormat ?? first.ArrayItemsFormat,

            // Required anywhere is required: a branch cannot loosen what another branch demands.
            IsRequired = first.IsRequired || second.IsRequired,
            IsNullable = first.IsNullable && second.IsNullable,

            Description = second.Description ?? first.Description,
            Default = second.Default ?? first.Default,
            IsReadOnly = second.IsReadOnly || first.IsReadOnly,
            IsWriteOnly = second.IsWriteOnly || first.IsWriteOnly,

            IsDictionary = second.IsDictionary || first.IsDictionary,
            DictionaryValueType = second.DictionaryValueType ?? first.DictionaryValueType,
            DictionaryValueRef = second.DictionaryValueRef ?? first.DictionaryValueRef,

            MinLength = second.MinLength ?? first.MinLength,
            MaxLength = second.MaxLength ?? first.MaxLength,
            Minimum = second.Minimum ?? first.Minimum,
            Maximum = second.Maximum ?? first.Maximum,
            ExclusiveMinimum = second.ExclusiveMinimum || first.ExclusiveMinimum,
            ExclusiveMaximum = second.ExclusiveMaximum || first.ExclusiveMaximum,
            Pattern = second.Pattern ?? first.Pattern,
            MinItems = second.MinItems ?? first.MinItems,
            MaxItems = second.MaxItems ?? first.MaxItems,
            EnumValues = second.EnumValues is { Count: > 0 } ? second.EnumValues : first.EnumValues
        };
    }

    private static SchemaModel ParseDictionarySchema(string name, IOpenApiSchema schema) {
        var addlProps = schema.AdditionalProperties;
        return new SchemaModel {
            Name = name,
            Kind = SchemaKind.Dictionary,
            DictionaryValueType = SchemaType(addlProps),
            DictionaryValueRef = GetNonPrimitiveRef(addlProps)
        };
    }

    private static SchemaModel ParseArraySchema(string name, IOpenApiSchema schema) {
        return new SchemaModel {
            Name = name,
            Kind = SchemaKind.Array,
            ArrayItemsRef = GetNonPrimitiveRef(schema.Items),
            ArrayItemsType = SchemaType(schema.Items),
            ArrayItemsFormat = schema.Items?.Format
        };
    }

    /// <summary>
    /// A schema declared inline, given a name so it can have a type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An object written inline on a property was discarded before any emitter saw it: the property
    /// mapped to <c>JsonElement</c> and every constraint on the nested properties went with it. It
    /// has no name of its own, so one is made from where it sits - <c>Pet</c> plus <c>address</c>
    /// gives <c>PetAddress</c>.
    /// </para>
    /// <para>
    /// A synthesized name colliding with one the document declares is reported rather than renamed.
    /// Silently picking a different name would give the author a public type they did not write and
    /// cannot find in their specification.
    /// </para>
    /// </remarks>
    /// <summary>A name nothing has taken, numbered where the plain one is gone.</summary>
    /// <remarks>
    /// Numbered rather than qualified, and that is the whole of what can be said: two provenances
    /// that collide here differ only in where the boundary between parent and property falls, so
    /// they spell the same words in the same order and nothing readable separates them. Same rule
    /// as NameAllocator - a number appears only where the scope distinguishes nothing.
    /// </remarks>
    private static string Unique(string name, SchemaCollector collector) {
        if (!collector.IsTaken(name)) {
            return name;
        }

        for (var suffix = 2; ; suffix++) {
            var candidate =
                name + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (!collector.IsTaken(candidate)) {
                return candidate;
            }
        }
    }

    private static string SynthesizeSchema(
        string parentName, string propertyName, IOpenApiSchema schema, SchemaCollector collector) {
        // `title` is OpenAPI's own way to name a schema and would give far better names than this
        // concatenation - Stripe carries 4,700 of them, GitHub 2,175. It is not usable yet: titles
        // are not unique, and checking one against every name already taken needs the reserved set
        // for the whole document, which only the declared schemas' own pass currently holds.
        // Wiring that set through is the change that makes titles safe.
        var name = NamingHelper.ToPascalCase(parentName) + NamingHelper.ToPascalCase(propertyName);

        // Joining an operation to a property path with no separator lets two different pairs land
        // on one name: Stripe's POST .../financial_account with `features.card_issuing` collides
        // with POST .../financial_account/features with `card_issuing`, 28 times over. Nothing in
        // the document is wrong and neither name is declared, so there is nothing an author could
        // rename.
        //
        // Numbered rather than qualified, and that is the whole of what can be said: the two
        // provenances differ only in where the boundary between parent and property falls, so they
        // spell the same words in the same order and nothing human-readable separates them. This
        // used to carry a hash of the provenance, which was stable and unreadable - and stable was
        // never the hard part. Same rule as NameAllocator: a number appears only where the scope
        // distinguishes nothing.
        name = Unique(name, collector);

        // Claimed before the children are read, since they are lifted during that read.
        collector.Reserve(name);

        var model = ParseObjectSchema(name, schema, collector);

        model.Description = FirstNonEmpty(schema.Description);
        model.IsDeprecated = schema.Deprecated;

        collector.Add(model);

        return "#/components/schemas/" + name;
    }

    /// <summary>
    /// Every branch of a <c>oneOf</c> or <c>anyOf</c>, named or written in place.
    /// </summary>
    /// <remarks>
    /// Inline branches are the majority and were the reason this could not work as a list of
    /// references: of the corpus's 338 undiscriminated choices, 200 name nothing at all -
    /// <c>oneOf: [string, boolean]</c> - and 45 more mix a reference with an inline type. Keeping
    /// only the references read those as a choice with one branch, or none, and produced no type.
    /// </remarks>
    /// <summary>
    /// A choice the reader has already folded into a union of types.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Microsoft.OpenApi 3.x normalises <c>oneOf: [{type: string}, {type: boolean}]</c> into one
    /// schema whose <c>Type</c> carries both flags, with <c>OneOf</c> left empty. That is the
    /// majority spelling in the wild - 200 of the corpus's 338 undiscriminated choices name no
    /// schema at all - so a parser that only reads <c>OneOf</c> sees nothing there and types the
    /// property as <c>JsonElement</c>. This is the same choice arriving under a different name.
    /// </para>
    /// <para>
    /// <c>Null</c> is stripped first: <c>type: [string, "null"]</c> is 3.1 for a nullable string,
    /// which is one type and a flag, not a choice between two.
    /// </para>
    /// </remarks>
    private static void CollectTypeUnion(IOpenApiSchema prop, PropertyModel model) {
        if (prop.Type is not { } declared) {
            return;
        }

        var types = declared & ~JsonSchemaType.Null;

        foreach (var candidate in new[] {
                     JsonSchemaType.String, JsonSchemaType.Integer, JsonSchemaType.Number,
                     JsonSchemaType.Boolean, JsonSchemaType.Array, JsonSchemaType.Object
                 }) {
            if ((types & candidate) != candidate) {
                continue;
            }

            // One bit is an ordinary type, not a choice - left alone so nothing changes for the
            // documents that were always read correctly.
            if (types == candidate) {
                return;
            }

            var branch = new ChoiceBranchModel {
                Type = candidate switch {
                    JsonSchemaType.String => "string",
                    JsonSchemaType.Integer => "integer",
                    JsonSchemaType.Number => "number",
                    JsonSchemaType.Boolean => "boolean",
                    JsonSchemaType.Array => "array",
                    _ => "object"
                }
            };

            if (!model.OneOf.Contains(branch)) {
                model.OneOf.Add(branch);
            }
        }
    }

    /// <summary>
    /// The branches of a choice, as the model describes them.
    /// </summary>
    /// <remarks>
    /// Takes the list rather than the owner, because a choice is declared in two places and they do
    /// not share a model type: on a property, where it becomes a synthesized type named for where it
    /// sits, and as a component of its own, where it is already named. Reading both through one
    /// method is what keeps them from disagreeing about which branches count.
    /// </remarks>
    private static void CollectBranches(
        IList<IOpenApiSchema>? branches, List<ChoiceBranchModel> into) {
        if (branches == null) {
            return;
        }

        foreach (var branch in branches) {
            var reference = GetNonPrimitiveRef(branch);

            if (reference == null && SchemaType(branch) == null) {
                // A branch that types to nothing. Two spellings reach here and neither is a choice
                // between anything: `{type: "null"}`, which is 3.1 for "and it may be null" and is
                // how OpenAI writes every optional string, and `{}`, which permits any value at all
                // and so cannot be told from the branch beside it. Dropping both leaves the real
                // branches, and a property left with one of those is an ordinary property again.
                continue;
            }

            var described = reference != null
                ? new ChoiceBranchModel { Ref = reference }
                : new ChoiceBranchModel { Type = SchemaType(branch), Format = branch.Format };

            if (!into.Contains(described)) {
                into.Add(described);
            }
        }
    }

    /// <summary>
    /// A type holding exactly one of a <c>oneOf</c>'s branches, or null to leave the property loose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Synthesized only when the payload can be resolved to a branch on the way in, because the
    /// whole value of the type is that <c>Value</c> is already a <c>Cat</c> when the caller switches
    /// on it. A discriminator says which branch a payload is and the document is the one asserting
    /// it; without one, the only way to decide is to look at the shape, which is a guess this does
    /// not make unless asked.
    /// </para>
    /// <para>
    /// Named for where it is declared rather than for what it holds - <c>OneOfHolderPayload</c>,
    /// not <c>CatOrDog</c> - so the name stays put when a branch is added, and stays short when a
    /// document declares fifteen of them.
    /// </para>
    /// </remarks>
    private static string? SynthesizeOneOf(
        string parentName, string propertyName, IOpenApiSchema prop, PropertyModel property,
        SchemaCollector collector) {
        var discriminator = prop.Discriminator;

        var name = Unique(
            NamingHelper.ToPascalCase(parentName) + NamingHelper.ToPascalCase(propertyName),
            collector);

        collector.Reserve(name);

        var model = new SchemaModel {
            Name = name,
            Kind = SchemaKind.OneOf,
            OneOf = new List<ChoiceBranchModel>(property.OneOf),
            Description = FirstNonEmpty(prop.Description),
            DiscriminatorPropertyName = discriminator?.PropertyName
        };

        // Explicit mapping if the document gives one; otherwise the specification says a value maps
        // to the schema it names, which is what most descriptions rely on.
        if (discriminator?.Mapping is { Count: > 0 }) {
            foreach (var mapping in discriminator.Mapping) {
                model.DiscriminatorMapping.Add(new DiscriminatorMappingModel {
                    Value = mapping.Key,
                    Ref = mapping.Value?.Reference?.ReferenceV3 ?? ""
                });
            }
        } else if (model.DiscriminatorPropertyName != null) {
            foreach (var branch in model.OneOf) {
                if (branch.Ref == null) {
                    continue;
                }

                model.DiscriminatorMapping.Add(new DiscriminatorMappingModel {
                    Value = TypeMapper.GetRefName(branch.Ref),
                    Ref = branch.Ref
                });
            }
        }

        collector.Add(model);

        return TypeMapper.MakeRef(name);
    }

    private static PropertyModel ParseProperty(
        string name, IOpenApiSchema prop, bool isRequired, string parentName, SchemaCollector collector) {
        var model = new PropertyModel {
            Name = name,
            IsRequired = isRequired,
            IsNullable = IsNullable(prop),
            Default = GetOpenApiPrimitiveValue(prop.Default),
            Description = FirstNonEmpty(prop.Description)
        };

        // Extract validation constraints
        ExtractValidationConstraints(prop, model);

        // What the payload is allowed to be. Recorded whether or not it becomes a type of its own,
        // because the branches would otherwise leave no trace in the model - and a schema nothing
        // in the model points at is not generated.
        CollectBranches(prop.OneOf, model.OneOf);
        CollectBranches(prop.AnyOf, model.OneOf);
        CollectTypeUnion(prop, model);

        // A choice between named schemas becomes a type that holds exactly one of them, rather than
        // a JsonElement the caller has to take apart. Only where the document says which branch a
        // payload is - see SynthesizeOneOf.
        if (model.OneOf.Count > 1 && SchemaRef(prop) == null) {
            var choice = SynthesizeOneOf(parentName, name, prop, model, collector);

            if (choice != null) {
                model.Ref = choice;
                return model;
            }
        }

        // Only keep $ref when it points to an object or enum that gets a generated C# type.
        // Primitive refs (e.g. CustomId → string) are inlined to their underlying type.
        var nonPrimitiveRef = GetNonPrimitiveRef(prop);
        if (nonPrimitiveRef != null) {
            model.Ref = nonPrimitiveRef;
            return model;
        }

        // JSON Schema's const is an enum of one, and 3.1 descriptions use it where 3.0 wrote a
        // single-member enum. Read as neither, a property pinned to one value looked unconstrained -
        // and the choice resolution that uses a pinned value to tell branches apart could never
        // fire, because nothing ever put one in the model.
        if (prop.Const is { } constant && prop.Enum is not { Count: > 0 }) {
            var value = EnumMember(constant);

            if (value != null) {
                model.Type = SchemaType(prop) ?? "string";
                model.EnumValues = new List<string> { value };

                return model;
            }
        }

        if (prop.Enum is { Count: > 0 }) {
            // The members' own type, not "string" unconditionally. An inline enum on a property
            // generates no C# enum - it stays a primitive constrained by [AllowedValues] - so this
            // is the type that member is emitted as. Forcing "string" made an integer enum a string
            // property holding "1", "2", which is the mismatch that kept numeric members filtered
            // out of the parser in the first place.
            var memberType = EnumMemberType(prop.Enum);

            model.Type = memberType == "integer" ? SchemaType(prop) ?? "integer" : "string";
            model.EnumValues = prop.Enum
                .Select(EnumMember)
                    .Where(value => value != null)
                    .Select(value => value!)
                .ToList();
            return model;
        }

        if (SchemaType(prop) == "array") {
            model.IsArray = true;
            model.ArrayItemsRef = GetNonPrimitiveRef(prop.Items);
            model.ArrayItemsType = SchemaType(prop.Items);
            model.ArrayItemsFormat = prop.Items?.Format;
            return model;
        }

        if (SchemaType(prop) == "object" && prop.AdditionalProperties != null) {
            model.IsDictionary = true;
            model.DictionaryValueType = SchemaType(prop.AdditionalProperties);
            model.DictionaryValueRef = GetNonPrimitiveRef(prop.AdditionalProperties);
            return model;
        }

        // An object with properties but no name of its own. Lifted into one rather than left to
        // fall through to JsonElement, which discarded the nested shape entirely.
        if (prop.Properties is { Count: > 0 }) {
            model.Ref = SynthesizeSchema(parentName, name, prop, collector);
            return model;
        }

        model.Type = SchemaType(prop);
        model.Format = prop.Format;
        return model;
    }

    private static void ExtractValidationConstraints(IOpenApiSchema schema, PropertyModel model) {
        // Not constraints, but read here because this is the one place a property's own schema is in
        // hand. They shape the generated type rather than validating it - see PropertyModel.
        model.IsReadOnly = schema.ReadOnly;
        model.IsWriteOnly = schema.WriteOnly;

        if (schema.MinLength.HasValue) model.MinLength = schema.MinLength;
        if (schema.MaxLength.HasValue) model.MaxLength = schema.MaxLength;
        if (Bound(schema.Minimum) is { } minimum) model.Minimum = minimum;
        if (Bound(schema.Maximum) is { } maximum) model.Maximum = maximum;
        // 3.1 states the bound on the keyword itself - exclusiveMinimum: 5 - where 3.0 set a
        // flag beside `minimum`. Either way the model keeps a bound plus a flag.
        if (Bound(schema.ExclusiveMinimum) is { } exclusiveMinimum) {
            model.Minimum = exclusiveMinimum;
            model.ExclusiveMinimum = true;
        }
        if (Bound(schema.ExclusiveMaximum) is { } exclusiveMaximum) {
            model.Maximum = exclusiveMaximum;
            model.ExclusiveMaximum = true;
        }
        if (!string.IsNullOrEmpty(schema.Pattern)) model.Pattern = schema.Pattern;
        if (schema.MinItems.HasValue) model.MinItems = schema.MinItems;
        if (schema.MaxItems.HasValue) model.MaxItems = schema.MaxItems;

        // minProperties and maxProperties bound an object's entry count, which for a schema that
        // becomes a Dictionary<string, T> is the same thing MinItems bounds for a List<T> - and
        // [ItemCount] emits `.Count` either way. Collapsed onto the same fields rather than carried
        // separately because a schema is an object or an array, never both, so the two pairs cannot
        // both apply to one property.
        if (schema.MinProperties.HasValue) model.MinItems = (int?)schema.MinProperties;
        if (schema.MaxProperties.HasValue) model.MaxItems = (int?)schema.MaxProperties;
    }

    /// <summary>
    /// Returns the ReferenceV3 string only if the schema references an object or enum
    /// (types that get generated C# classes). Returns null for primitive type refs
    /// so that the caller inlines the underlying type instead.
    /// </summary>
    private static string? GetNonPrimitiveRef(IOpenApiSchema? schema) {
        if (schema is null || SchemaRef(schema) == null) return null;

        // Enums and objects get generated C# types — keep the ref.
        if (schema.Enum is { Count: > 0 }) return SchemaRef(schema);
        if (SchemaType(schema) == "object" || schema.Properties is { Count: > 0 }) return SchemaRef(schema);
        if (SchemaType(schema) == "array") return SchemaRef(schema);
        if (schema.AllOf is { Count: > 0 }) return SchemaRef(schema);

        // The base of a polymorphic hierarchy gets a generated type like any other object, and so
        // does a component that is a choice between branches - see ParseComponentChoice. Both are
        // covered by the same test: a oneOf declares a type either way, and neither has a `type` of
        // its own for the primitive check below to see. Without this the reference was dropped and
        // the property inlined to JsonElement, which is the type it was trying not to be.
        if (schema.OneOf is { Count: > 0 }) return SchemaRef(schema);

        // Primitive types (string, integer, number, boolean) — inline them.
        return null;
    }

    /// <summary>
    /// Follows $ref to resolve the actual schema with properties.
    /// SchemaRef(OpenApiSchema) is non-null for $ref entries; the reader
    /// already resolves these into the same schema object graph, so
    /// properties/required are on the same object.
    /// </summary>
    private static IOpenApiSchema? ResolveSchema(IOpenApiSchema? schema) {
        if (schema == null) return null;

        // If the schema has properties directly, use it
        if (schema.Properties is { Count: > 0 }) return schema;

        // For allOf, merge properties
        if (schema.AllOf is { Count: > 0 }) {
            var merged = new OpenApiSchema {
                Required = new HashSet<string>(schema.Required ?? Enumerable.Empty<string>())
            };
            foreach (var allOfSchema in schema.AllOf ?? Enumerable.Empty<IOpenApiSchema>()) {
                if (allOfSchema.Required != null) {
                    foreach (var req in allOfSchema.Required) {
                        merged.Required.Add(req);
                    }
                }
                if (allOfSchema.Properties != null) {
                    merged.Properties ??= new Dictionary<string, IOpenApiSchema>();
                    foreach (var kvp in allOfSchema.Properties) {
                        merged.Properties[kvp.Key] = kvp.Value;
                    }
                }
            }
            return merged;
        }

        return schema;
    }

    /// <summary>
    /// A path item's own parameters, overlaid with the operation's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OpenAPI lets a path item declare parameters shared by every operation on it - the
    /// <c>{petId}</c> of <c>/pets/{petId}</c>, written once rather than on each of GET, PUT, PATCH
    /// and DELETE - and an operation may override one by redeclaring the same name and location.
    /// Only <c>operation.Parameters</c> was read, so a spec written the shared way generated
    /// handlers that never received the value: the route still matched on its wildcard node, and
    /// what it captured went nowhere.
    /// </para>
    /// <para>
    /// An override keeps the position of the parameter it replaces rather than moving to the end.
    /// This order is the generated method's signature order, so it is a source-breaking change
    /// whenever it shifts.
    /// </para>
    /// </remarks>
    private static IEnumerable<IOpenApiParameter> MergeParameters(
        IList<IOpenApiParameter>? pathItemParameters, IList<IOpenApiParameter>? operationParameters) {
        if (pathItemParameters == null || pathItemParameters.Count == 0) {
            return operationParameters ?? (IList<IOpenApiParameter>)new List<IOpenApiParameter>();
        }

        if (operationParameters == null || operationParameters.Count == 0) {
            return pathItemParameters;
        }

        var merged = new List<IOpenApiParameter>();
        var overridden = new List<IOpenApiParameter>();

        foreach (var shared in pathItemParameters) {
            var operationVersion = operationParameters.FirstOrDefault(candidate => SameParameter(candidate, shared));

            if (operationVersion != null) {
                overridden.Add(operationVersion);
            }

            merged.Add(operationVersion ?? shared);
        }

        foreach (var own in operationParameters) {
            if (!overridden.Contains(own)) {
                merged.Add(own);
            }
        }

        return merged;
    }

    /// <summary>
    /// The first candidate that carries text, or null.
    /// </summary>
    /// <remarks>
    /// An omitted key and an empty one are different things to this model - see
    /// <c>SpecModelSerializer</c> - so a description present but blank has to arrive as null rather
    /// than as "", or it round-trips into a doc comment with nothing in it.
    /// </remarks>
    private static string? FirstNonEmpty(params string?[] candidates) {
        foreach (var candidate in candidates) {
            if (!string.IsNullOrWhiteSpace(candidate)) {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether two declarations name the same parameter. Identity is name plus location, so a
    /// <c>limit</c> in the query and a <c>limit</c> in a header are two parameters, not one.
    /// </summary>
    private static bool SameParameter(IOpenApiParameter left, IOpenApiParameter right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) && left.In == right.In;

    private static ParameterModel? ParseParameter(IOpenApiParameter param) {
        // Skip parameters marked with x-codegen-exclude
        if (param.Extensions != null &&
            param.Extensions.TryGetValue("x-codegen-exclude", out var excludeExt) &&
            excludeExt is JsonNodeExtension { Node: JsonValue excludeValue } &&
            excludeValue.GetValueKind() == JsonValueKind.True) {
            return null;
        }

        var paramModel = new ParameterModel {
            Name = param.Name ?? "",
            In = param.In?.ToString()?.ToLowerInvariant() ?? "query",
            IsNullable = IsNullable(param.Schema),
            Default = GetOpenApiPrimitiveValue(param.Schema?.Default),
            Description = FirstNonEmpty(param.Description),
            IsRequired = param.Required,
            Type = SchemaType(param.Schema),
            Format = param.Schema?.Format,
            Ref = GetNonPrimitiveRef(param.Schema),
            IsArray = SchemaType(param.Schema) == "array",
            ArrayItemsType = SchemaType(param.Schema?.Items),
            ArrayItemsRef = GetNonPrimitiveRef(param.Schema?.Items)
        };

        // Extract validation constraints from parameter schema
        if (param.Schema != null) {
            if (param.Schema.MinLength.HasValue) paramModel.MinLength = param.Schema.MinLength;
            if (param.Schema.MaxLength.HasValue) paramModel.MaxLength = param.Schema.MaxLength;
            if (Bound(param.Schema.Minimum) is { } min) paramModel.Minimum = min;
            if (Bound(param.Schema.Maximum) is { } max) paramModel.Maximum = max;

            // As with schemas: 3.1 puts the bound on the exclusive keyword itself.
            if (Bound(param.Schema.ExclusiveMinimum) is { } exclusiveMin) {
                paramModel.Minimum = exclusiveMin;
                paramModel.ExclusiveMinimum = true;
            }

            if (Bound(param.Schema.ExclusiveMaximum) is { } exclusiveMax) {
                paramModel.Maximum = exclusiveMax;
                paramModel.ExclusiveMaximum = true;
            }
            if (!string.IsNullOrEmpty(param.Schema.Pattern)) paramModel.Pattern = param.Schema.Pattern;
            if (param.Schema.MinItems.HasValue) paramModel.MinItems = param.Schema.MinItems;
            if (param.Schema.MaxItems.HasValue) paramModel.MaxItems = param.Schema.MaxItems;
            if (param.Schema.Enum is { Count: > 0 }) {
                paramModel.EnumValues = param.Schema.Enum
                    .Select(EnumMember)
                        .Where(value => value != null)
                        .Select(value => value!)
                    .ToList();
            }
        }

        return paramModel;
    }

    /// <summary>
    /// What the description says a caller must hold to invoke this operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Scopes, not schemes.</b> A scheme names how a caller proves who they are - which issuer,
    /// which token format - and that is configuration this application already owns; a description
    /// cannot know it and should not try to. What a description <em>does</em> know is which
    /// permissions an operation needs, and that maps onto <c>Requirement</c> without an intermediary.
    /// So the scheme decides only whether the entry carries scopes at all.
    /// </para>
    /// <para>
    /// Only <c>oauth2</c> and <c>openIdConnect</c> may carry them; the specification requires every
    /// other type to declare an empty array. An entry with no scopes therefore says "be
    /// authenticated" - which is a requirement, not the absence of one. Reading it as the absence of
    /// one would be actively unsafe: <c>[{oauth2: [write]}, {apiKey: []}]</c> would become
    /// "needs write, OR needs nothing", and the OR is then satisfied by everybody.
    /// </para>
    /// <para>
    /// <b>An operation's own <c>security</c> replaces the document's, it does not merge with it.</b>
    /// That includes an empty one: <c>security: []</c> is how a description opts a single operation
    /// out of a document-level default, and it is <em>not</em> the same as an empty scope array one
    /// level down. It produces no branches, which produces no requirement - deliberately not
    /// <c>[AllowAnonymous]</c>, because a requirement derived from a description is conjoined with
    /// what the handler declared and must not be able to remove one.
    /// </para>
    /// </remarks>
    private static List<AuthorizationBranchModel> ParseSecurity(
        OpenApiOperation operation, OpenApiDocument document, string operationId,
        ICollection<string>? diagnostics) {
        // Null is "declared nothing, inherit the default"; empty is "declared none, and means it".
        var declared = operation.Security ?? document.Security;

        if (declared == null || declared.Count == 0) {
            return new List<AuthorizationBranchModel>();
        }

        var schemes = document.Components?.SecuritySchemes;
        var branches = new List<AuthorizationBranchModel>();

        foreach (var requirement in declared) {
            var branch = new AuthorizationBranchModel();

            foreach (var entry in requirement) {
                var name = entry.Key?.Reference?.Id;
                var scopes = entry.Value;

                // A name that resolves to nothing is the document's own error, and a silent
                // fallback to "be authenticated" is a downgrade nobody asked for: the operation
                // stops requiring the permission it named. Reported so the misspelling is a build
                // message rather than a discovery in production.
                if (!string.IsNullOrEmpty(name) && !Declares(schemes, name!)) {
                    diagnostics?.Add(
                        $"operation '{operationId}' requires security scheme '{name}', which " +
                        "'components.securitySchemes' does not declare. Its scopes were not read, " +
                        "so the operation requires an authenticated caller and none of the " +
                        "permissions it names.");
                }

                if (!string.IsNullOrEmpty(name) &&
                    scopes is { Count: > 0 } &&
                    CarriesScopes(schemes, name!)) {
                    foreach (var scope in scopes) {
                        if (!string.IsNullOrEmpty(scope) && !branch.Grants.Contains(scope)) {
                            branch.Grants.Add(scope);
                        }
                    }
                } else {
                    // Either a scheme that cannot carry scopes, or one that can and declared none.
                    // Both say the same thing about the caller.
                    branch.RequiresAuthentication = true;
                }
            }

            // An entry naming no schemes at all is not a way in. Dropping it rather than admitting
            // an empty AND, which would be satisfied by anyone and would take the whole OR with it.
            if (branch.Grants.Count > 0 || branch.RequiresAuthentication) {
                branches.Add(branch);
            }
        }

        return branches;
    }

    /// <summary>
    /// Whether a named scheme is one the specification lets carry scopes.
    /// </summary>
    /// <remarks>
    /// A scheme the document never declared is treated as not carrying them. The reference is
    /// dangling, which is the document's own error, and reading its scope list would invent
    /// authorization out of a name that resolves to nothing.
    /// </remarks>
    private static bool CarriesScopes(
        IDictionary<string, IOpenApiSecurityScheme>? schemes, string name) {
        if (schemes == null || !schemes.TryGetValue(name, out var scheme)) {
            return false;
        }

        return scheme.Type is SecuritySchemeType.OAuth2 or SecuritySchemeType.OpenIdConnect;
    }

    /// <summary>Whether the document declares the named scheme at all.</summary>
    private static bool Declares(
        IDictionary<string, IOpenApiSecurityScheme>? schemes, string name) =>
        schemes != null && schemes.ContainsKey(name);

    private static void ParsePath(string path, IOpenApiPathItem pathItem,
        Dictionary<string, List<OperationModel>> operationsByTag, SchemaCollector collector,
        bool groupUntaggedByPath, OpenApiDocument document, ICollection<string>? diagnostics) {
        if (pathItem.Operations == null) {
            return;
        }

        // Guarded rather than coalesced to an empty sequence: naming the element type would mean
        // naming System.Net.Http.HttpMethod, which .NET Framework does not have in scope here.
        foreach (var opKvp in pathItem.Operations) {
            var operation = opKvp.Value;
            var httpMethod = opKvp.Key.ToString().ToUpperInvariant();

            var tag = operation.Tags?.FirstOrDefault()?.Name
                      ?? UntaggedGroup(path, groupUntaggedByPath);
            var operationId = operation.OperationId ?? GenerateOperationId(httpMethod, path);

            var opModel = new OperationModel {
                OperationId = operationId,
                Path = path,
                HttpMethod = httpMethod,
                Tag = tag,
                // Summary first: it is the one-line form, and a doc comment is one line.
                Description = FirstNonEmpty(operation.Summary, operation.Description),
                IsDeprecated = operation.Deprecated,
                AuthorizationBranches = ParseSecurity(operation, document, operationId, diagnostics)
            };

            foreach (var param in MergeParameters(pathItem.Parameters, operation.Parameters)) {
                var paramModel = ParseParameter(param);

                if (paramModel != null) {
                    opModel.Parameters.Add(paramModel);
                }
            }

            if (operation.RequestBody?.Content != null) {
                var bodyContent = SelectMediaType(operation.RequestBody.Content);

                if (bodyContent.Value?.Schema != null) {
                    var bodySchema = bodyContent.Value.Schema;
                    opModel.RequestBodyContentType = bodyContent.Key;
                    opModel.RequestBodyRef = SchemaRef(bodySchema);
                    opModel.RequestBodyType = SchemaType(bodySchema);

                    // Resolve body schema properties for validation
                    var resolvedSchema = ResolveSchema(bodySchema);
                    if (resolvedSchema != null) {
                        opModel.RequestBodyRequired = resolvedSchema.Required?.ToList() ?? new List<string>();
                        if (resolvedSchema.Properties != null) {
                            foreach (var propKvp in resolvedSchema.Properties) {
                                var isRequired = opModel.RequestBodyRequired.Contains(propKvp.Key);
                                opModel.RequestBodyProperties.Add(
                                    ParseProperty(propKvp.Key, propKvp.Value, isRequired,
                                        opModel.OperationId, collector));
                            }
                        }
                    }
                }
            }

            if (operation.Responses != null) {
                // Everything the specification says the operation can answer with, other than the
                // success case. All of it used to be discarded, so a document could describe a 404
                // and its payload in detail and generate no trace of either.
                foreach (var respKvp in operation.Responses
                             .Where(r => !r.Key.StartsWith("2") && r.Key != "default")
                             .OrderBy(r => r.Key, StringComparer.Ordinal)) {
                    if (!int.TryParse(respKvp.Key, out var errorStatus)) {
                        continue;
                    }

                    var errorContent = respKvp.Value?.Content != null
                        ? SelectMediaType(respKvp.Value.Content)
                        : default;

                    opModel.ErrorResponses.Add(new ErrorResponseModel {
                        StatusCode = errorStatus,
                        Ref = SchemaRef(errorContent.Value?.Schema),
                        Description = FirstNonEmpty(respKvp.Value?.Description)
                    });
                }

                // Every 2xx, not the first one.
                //
                // This loop ended in an unconditional break, so an operation declaring 200 and 202
                // parsed as its 200 and the 202 left no trace - not in the interface, not in the
                // routing, not in the document the build emits back out. The set was already in
                // hand and one of it was kept.
                //
                // The flat fields still describe the primary success, which is the lowest declared
                // 2xx and therefore the first here, so every consumer that reads them individually
                // is untouched. The list beside them is what an operation with more than one
                // success needs, because its extra statuses cannot be thrown.
                var isPrimarySuccess = true;

                foreach (var respKvp in operation.Responses.Where(r => r.Key.StartsWith("2")).OrderBy(r => r.Key, StringComparer.Ordinal)) {
                    var response = respKvp.Value;

                    // A non-numeric 2xx key - the "2XX" range form - names no single status, so it
                    // cannot become a case. Skipped rather than guessed at, and skipped before the
                    // flat fields are touched so it cannot claim the primary slot either.
                    if (!int.TryParse(respKvp.Key, out var statusCode)) {
                        continue;
                    }

                    var success = new SuccessResponseModel {
                        StatusCode = statusCode,
                        Description = FirstNonEmpty(response?.Description)
                    };

                    if (isPrimarySuccess) {
                        opModel.SuccessStatusCode = statusCode;
                    }

                    if (response?.Content != null) {
                        // Every media type the response declares, in document order - the set the
                        // response is negotiated against. SelectMediaType below picks one of these
                        // to read the schema from, which is a different question: that one decides
                        // the C# return type, this one decides what may go on the wire.
                        foreach (var declared in response.Content.Keys) {
                            if (!opModel.ProducedContentTypes.Contains(declared)) {
                                opModel.ProducedContentTypes.Add(declared);
                            }
                        }

                        var responseContent = SelectMediaType(response.Content);

                        // itemSchema first, because it and schema answer different questions and a
                        // media type carrying itemSchema is a stream whatever else it says. OpenAPI
                        // 3.2 added it for exactly this; Microsoft.OpenApi surfaces it directly, so
                        // there is nothing to hand-parse.
                        if (responseContent.Value?.ItemSchema != null) {
                            if (isPrimarySuccess) {
                                opModel.ResponseContentType = responseContent.Key;
                                opModel.ItemSchemaRef = SchemaRef(responseContent.Value.ItemSchema);
                            }
                        }
                        else if (responseContent.Value?.Schema != null) {
                            var responseSchema = responseContent.Value.Schema;

                            success.ContentType = responseContent.Key;
                            success.Ref = SchemaRef(responseSchema);
                            success.Type = SchemaType(responseSchema);
                            success.Format = responseSchema.Format;
                            success.IsArray = SchemaType(responseSchema) == "array";
                            success.ArrayItemsRef = SchemaRef(responseSchema.Items);
                            success.ArrayItemsType = SchemaType(responseSchema.Items);

                            if (isPrimarySuccess) {
                                opModel.ResponseContentType = responseContent.Key;
                                opModel.ResponseRef = SchemaRef(responseSchema);
                                opModel.ResponseType = SchemaType(responseSchema);
                                opModel.ResponseFormat = responseSchema.Format;
                                opModel.ResponseIsArray = SchemaType(responseSchema) == "array";
                                opModel.ResponseArrayItemsRef = SchemaRef(responseSchema.Items);

                                // The element's own type, for an array of primitives. Only the $ref
                                // was read, so `items: {type: string}` named nothing and the
                                // response became JsonElement - while array-of-$ref worked, which is
                                // what hid it.
                                opModel.ResponseArrayItemsType = SchemaType(responseSchema.Items);
                                opModel.ResponseArrayItemsFormat = responseSchema.Items?.Format;
                            }
                        }
                    }

                    opModel.SuccessResponses.Add(success);
                    isPrimarySuccess = false;
                }
            }

            // Parse x-filters extension on the operation
            if (operation.Extensions != null &&
                operation.Extensions.TryGetValue("x-filters", out var filtersExt) &&
                filtersExt is JsonNodeExtension { Node: JsonObject filtersObj }) {
                opModel.FilterInstances = ParseFilterInstances(filtersObj);
            }

            // x-hardened-raw-bytes opts the signature into byte[] for a response the spec types as
            // a string. Also not something the content map can say: text/plain describes the wire,
            // not whether the application holds the payload already encoded.
            if (operation.Extensions != null &&
                operation.Extensions.TryGetValue("x-hardened-raw-bytes", out var rawBytesExt) &&
                rawBytesExt is JsonNodeExtension { Node: JsonValue rawBytesValue } &&
                rawBytesValue.GetValueKind() == JsonValueKind.True) {
                opModel.RawBytesResponse = true;
            }

            if (!operationsByTag.TryGetValue(tag, out var list)) {
                list = new List<OperationModel>();
                operationsByTag[tag] = list;
            }

            list.Add(opModel);
        }
    }

    /// <summary>
    /// Which entry of an OpenAPI content map the generated signature is built from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// JSON wins when the operation offers it, because that is the shape the pipeline serializes by
    /// default and choosing anything else for a JSON-and-something operation would change what
    /// already-generated code returns.
    /// </para>
    /// <para>
    /// Otherwise the first entry, which is the change. This previously read
    /// <c>FirstOrDefault(c =&gt; c.Key.Contains("json"))</c> and discarded everything else, so a
    /// <c>text/plain</c> operation parsed to no schema at all: <c>ResponseType</c> stayed null, the
    /// generated interface method came back as bare <c>Task</c>, and nothing ever reached
    /// <c>RawResponseContentType</c>. A spec could describe a plain-text endpoint and the generator
    /// would silently emit one that returned nothing.
    /// </para>
    /// <para>
    /// Ordering is the document's. OpenAPI content maps are unordered in principle, but the reader
    /// preserves document order and a spec listing one media type first means it.
    /// </para>
    /// </remarks>
    private static KeyValuePair<string, IOpenApiMediaType> SelectMediaType(
        IDictionary<string, IOpenApiMediaType> content) {
        foreach (var entry in content) {
            if (entry.Key.Contains("json")) {
                return entry;
            }
        }

        foreach (var entry in content) {
            return entry;
        }

        return default;
    }

    /// <summary>
    /// A name for an operation the document did not name.
    /// </summary>
    /// <remarks>
    /// Path parameters are folded in as <c>By…</c> rather than dropped. Dropping them made sibling
    /// routes indistinguishable - DigitalOcean declares no operation ids at all, and
    /// <c>DELETE /v2/droplets</c> and <c>DELETE /v2/droplets/{droplet_id}</c> both became
    /// <c>deleteV2Droplets</c>, which is one interface method declared twice.
    /// </remarks>
    /// <remarks>
    /// Internal because the allocator uses it too: an operation whose declared id collides falls
    /// back to the name it would have had with no id at all, so both paths produce one convention.
    /// </remarks>
    private static string GenerateOperationId(string method, string path) =>
        NamingHelper.OperationIdFromRoute(method, path);

    /// <summary>
    /// The service an untagged operation belongs to.
    /// </summary>
    /// <remarks>
    /// One <c>Default</c> service by default, which is what a document with no tags asks for. Some
    /// have none at all and hundreds of operations - DigitalOcean is one - and a single interface
    /// with every method on it is not what anyone implements, so
    /// <c>HardenedOpenApiGroupUntaggedByPath</c> groups them by first path segment instead.
    /// </remarks>
    private static string UntaggedGroup(string path, bool byPath) {
        if (!byPath) {
            return "Default";
        }

        foreach (var segment in path.Split('/')) {
            if (segment.Length > 0 && segment[0] != '{') {
                return segment;
            }
        }

        return "Default";
    }

    // ── x-filter-types parsing ─────────────────────────────────────────

    private static FilterTypeModel? ParseFilterType(string name, JsonNode? value) {
        if (value is not JsonObject obj) return null;

        var model = new FilterTypeModel { Name = name };

        if (StringValue(obj, "namespace") is { } ns) {
            model.Namespace = ns;
        }

        if (obj.TryGetPropertyValue("generate", out var genValue) &&
            genValue is JsonValue gen && gen.GetValueKind() is JsonValueKind.True or JsonValueKind.False) {
            model.Generate = gen.GetValueKind() == JsonValueKind.True;
        }

        if (obj.TryGetPropertyValue("properties", out var propsValue) && propsValue is JsonObject propsObj) {
            foreach (var propKvp in propsObj) {
                var prop = ParseFilterTypeProperty(propKvp.Key, propKvp.Value);
                if (prop != null) {
                    model.Properties.Add(prop);
                }
            }
        }

        return model.Namespace.Length > 0 ? model : null;
    }

    private static FilterTypePropertyModel? ParseFilterTypeProperty(string name, JsonNode? value) {
        if (value is not JsonObject obj) return null;

        var prop = new FilterTypePropertyModel { Name = name };

        if (StringValue(obj, "type") is { } type) {
            prop.CSharpType = MapFilterPropertyType(type);
        }

        if (obj.TryGetPropertyValue("default", out var defaultValue)) {
            prop.Default = GetOpenApiPrimitiveValue(defaultValue);
        }

        if (obj.TryGetPropertyValue("enum", out var enumValue) && enumValue is JsonArray enumArr) {
            prop.EnumValues = enumArr
                .Select(GetOpenApiPrimitiveValue)
                .Where(v => v != null)
                .Select(v => v!)
                .ToList();
        }

        if (StringValue(obj, "enumType") is { } enumType) {
            prop.EnumType = enumType;
        }

        return prop;
    }

    // ── 3.x model bridging ─────────────────────────────────────────────
    //
    // Three things changed shape in Microsoft.OpenApi 2.0, and are read through here rather than at
    // thirty call sites. `type` became a flags enum carrying nullability instead of a separate
    // `nullable`; a `$ref` became its own implementation of IOpenApiSchema rather than a Reference
    // property on every schema; and numeric bounds became strings, so that double and decimal
    // rounding cannot change what a document said.

    /// <summary>
    /// The schema's type as OpenAPI 3.0 spelled it, with any null branch removed.
    /// </summary>
    /// <remarks>
    /// 3.1 writes <c>type: ["string", "null"]</c> where 3.0 wrote <c>type: string</c> and
    /// <c>nullable: true</c>, and the library models both as one flags enum. Stripping
    /// <see cref="JsonSchemaType.Null"/> leaves the shape, which is what every caller is asking
    /// about; <see cref="IsNullable"/> answers the other half.
    /// </remarks>
    private static string? SchemaType(IOpenApiSchema? schema) {
        if (schema?.Type is not { } type) {
            return null;
        }

        return (type & ~JsonSchemaType.Null) switch {
            JsonSchemaType.String => "string",
            JsonSchemaType.Integer => "integer",
            JsonSchemaType.Number => "number",
            JsonSchemaType.Boolean => "boolean",
            JsonSchemaType.Array => "array",
            JsonSchemaType.Object => "object",

            // Either nothing, or a union of real types that no single C# type stands for.
            _ => null
        };
    }

    /// <summary>Whether the schema admits null, however the document said so.</summary>
    private static bool IsNullable(IOpenApiSchema? schema) =>
        schema?.Type is { } type && type.HasFlag(JsonSchemaType.Null);

    /// <summary>
    /// The <c>$ref</c> a schema is, or null when it is written inline.
    /// </summary>
    /// <remarks>
    /// Every schema used to carry a nullable <c>Reference</c>. A reference is now its own type
    /// implementing the same interface, so the question is a type test.
    /// </remarks>
    private static string? SchemaRef(IOpenApiSchema? schema) =>
        schema is OpenApiSchemaReference reference ? reference.Reference?.ReferenceV3 : null;

    /// <summary>A numeric bound, which the library now hands over as a string.</summary>
    private static decimal? Bound(string? value) =>
        value != null && decimal.TryParse(
            value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// An enum member, as the literal text of whichever type the description declared.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Strings and integers both. This took strings alone, which is what the previous reader's
    /// <c>OfType&lt;OpenApiString&gt;</c> filter amounted to - so <c>type: integer, enum: [1, 2, 3]</c>
    /// was still recognised as an enum by <c>ParseSchemaKind</c> and arrived with every member
    /// filtered away, emitting an empty C# enum whose generated converter threw on every value.
    /// </para>
    /// <para>
    /// The note left here was that widening this alone would emit <c>[AllowedValues("true")]</c>
    /// against a member that never holds <c>"true"</c>, which was correct: the wire type has to
    /// travel with the values so the emitters know not to quote them. <see cref="SchemaModel.Type"/>
    /// carries it now, and <c>ConstraintAttributes</c> reads it.
    /// </para>
    /// <para>
    /// Booleans stay out. A two-valued enum is a <c>bool</c>, not a C# enum, and Stripe's
    /// <c>deleted: {type: boolean, enum: [true]}</c> is a constant rather than a type.
    /// </para>
    /// </remarks>
    private static string? EnumMember(JsonNode? value) {
        if (value is not JsonValue jsonValue) {
            return null;
        }

        switch (jsonValue.GetValueKind()) {
            case JsonValueKind.String:
                return jsonValue.GetValue<string>();
            case JsonValueKind.Number:
                return jsonValue.ToJsonString();
            default:
                return null;
        }
    }

    /// <summary>
    /// The type an <c>enum</c>'s members are, from the members rather than from <c>type:</c>.
    /// </summary>
    /// <remarks>
    /// A document is not obliged to write <c>type:</c> beside its <c>enum:</c>, and plenty do not.
    /// Reading it from the values is what the emitters need anyway - the wire form is what the
    /// members are, whatever the schema says about them.
    /// </remarks>
    private static string? EnumMemberType(IList<JsonNode> members) {
        var sawString = false;
        var sawNumber = false;

        foreach (var member in members) {
            if (member is not JsonValue jsonValue) {
                continue;
            }

            switch (jsonValue.GetValueKind()) {
                case JsonValueKind.String:
                    sawString = true;
                    break;
                case JsonValueKind.Number:
                    sawNumber = true;
                    break;
            }
        }

        // Both is not a C# enum in either direction, and guessing which half to honour would put
        // half the document's values out of reach. Reported rather than resolved - see
        // MixedEnumDiagnostics.
        if (sawString && sawNumber) {
            return MixedEnumType;
        }

        return sawNumber ? "integer" : sawString ? "string" : null;
    }

    /// <summary>
    /// Marks an <c>enum</c> declaring both strings and numbers, which is a build error.
    /// </summary>
    internal const string MixedEnumType = "mixed-enum";

    /// <summary>
    /// The C# member names a document supplied for its enum, or null where it supplied none.
    /// </summary>
    /// <remarks>
    /// <c>x-enum-varnames</c> is what openapi-generator reads and <c>x-enumNames</c> is what NSwag
    /// reads; both are common enough in the wild that honouring one and not the other would be
    /// arbitrary. It matters most for an integer enum, which declares values and no names at all -
    /// without this its members are <c>Value1</c>, <c>Value2</c>, and those appear at every call
    /// site.
    /// </remarks>
    private static List<string>? EnumMemberNames(IOpenApiSchema schema) {
        if (schema.Extensions == null) {
            return null;
        }

        foreach (var key in new[] { "x-enum-varnames", "x-enumNames" }) {
            if (!schema.Extensions.TryGetValue(key, out var extension) ||
                extension is not JsonNodeExtension { Node: JsonArray array }) {
                continue;
            }

            var names = new List<string>(array.Count);

            foreach (var node in array) {
                if (node is JsonValue value && value.GetValueKind() == JsonValueKind.String) {
                    names.Add(NamingHelper.ToPascalCase(value.GetValue<string>()));
                }
            }

            if (names.Count == array.Count && names.Count > 0) {
                return names;
            }
        }

        return null;
    }

    /// <summary>A string-valued member of a JSON object, or null.</summary>
    private static string? StringValue(JsonObject obj, string name) =>
        obj.TryGetPropertyValue(name, out var node) &&
        node is JsonValue value &&
        value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : null;

    private static string MapFilterPropertyType(string openApiType) {
        return openApiType.ToLowerInvariant() switch {
            "integer" or "int" => "int",
            "long" => "long",
            "boolean" or "bool" => "bool",
            "number" or "double" => "double",
            "float" => "float",
            _ => "string"
        };
    }

    // ── x-filters parsing ──────────────────────────────────────────────

    private static List<FilterInstanceModel> ParseFilterInstances(JsonObject filtersObj) {
        var instances = new List<FilterInstanceModel>();

        foreach (var kvp in filtersObj) {
            var instance = new FilterInstanceModel { FilterTypeName = kvp.Key };

            if (kvp.Value is JsonObject propsObj) {
                foreach (var propKvp in propsObj) {
                    var propValue = GetOpenApiPrimitiveValue(propKvp.Value);
                    if (propValue != null) {
                        instance.PropertyValues[propKvp.Key] = propValue;
                    }
                }
            }

            instances.Add(instance);
        }

        return instances;
    }

    /// <summary>
    /// Extracts a string representation of a primitive OpenAPI value
    /// suitable for emitting as a C# literal.
    /// </summary>
    private static string? GetOpenApiPrimitiveValue(JsonNode? value) {
        if (value is not JsonValue jsonValue) {
            return null;
        }

        return jsonValue.GetValueKind() switch {
            JsonValueKind.String => jsonValue.GetValue<string>(),

            // Written back exactly as the document had it. Going through a numeric type first would
            // reformat it, and what this feeds is a C# literal.
            JsonValueKind.Number => jsonValue.ToJsonString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}
