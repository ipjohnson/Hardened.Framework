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
                    groupUntaggedByPath);
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
        // because a dropped base changes which members a type declares.
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
    private static string StableSuffix(string provenance) {
        unchecked {
            var hash = 2166136261;

            foreach (var character in provenance) {
                hash = (hash ^ character) * 16777619;
            }

            return hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
        }
    }


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
    private static void InlineNonObjectRefs(ServiceSpecModel model) {
        // Only objects and enums become types. A reference to anything else - a top-level array
        // alias, an anyOf with no shape of its own, a schema this parser could not read - was still
        // typed by the reference's name, naming something nothing declares. Slack's `blocks`,
        // GitHub's `code-frequency-stat` and Stripe's `external_account` were all CS0246.
        var emittable = new HashSet<string>();
        var arrays = new Dictionary<string, SchemaModel>();

        foreach (var schema in model.Schemas) {
            if (schema.Kind == SchemaKind.Object || schema.Kind == SchemaKind.Enum) {
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

                operation.ErrorResponses.RemoveAll(error => Missing(error.Ref));

                var response = Array(operation.ResponseRef);

                if (response != null) {
                    operation.ResponseRef = null;
                    operation.ResponseIsArray = true;
                    operation.ResponseArrayItemsRef = response.ArrayItemsRef;
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

        if (server == null || string.IsNullOrWhiteSpace(server.Url)) {
            return "";
        }

        var url = server.Url;

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
            return new SchemaModel {
                Name = name,
                Kind = SchemaKind.Enum,
                EnumValues = schema.Enum
                    .Select(EnumMember)
                        .Where(value => value != null)
                        .Select(value => value!)
                    .ToList()
            };
        }

        if (schema.AllOf is { Count: > 0 }) {
            return ParseAllOf(name, schema, collector);
        }

        // A oneOf naming its branches, with a discriminator to choose between them, is the base of
        // a hierarchy. It matched none of the shapes below and fell through to Primitive with a
        // null type, which is what made every property referencing it a JsonElement.
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

        foreach (var allOfSchema in schema.AllOf ?? Enumerable.Empty<IOpenApiSchema>()) {
            if (allOfSchema.Required != null) {
                foreach (var req in allOfSchema.Required) {
                    if (!model.Required.Contains(req)) {
                        model.Required.Add(req);
                    }
                }
            }

            if (allOfSchema.Properties != null) {
                foreach (var propKvp in allOfSchema.Properties) {
                    var isRequired = model.Required.Contains(propKvp.Key) ||
                                     (allOfSchema.Required?.Contains(propKvp.Key) ?? false);
                    var parsed =
                        ParseProperty(propKvp.Key, propKvp.Value, isRequired, name, collector);

                    // allOf is an intersection, so a property named by more than one branch is one
                    // property described twice - most often a base declaring it and a later branch
                    // narrowing a constraint on it. Appending both produced two record parameters
                    // of the same name: CS0100 and CS0102, from a document that is entirely legal.
                    var existing = model.Properties.FindIndex(p => p.Name == parsed.Name);

                    if (existing >= 0) {
                        model.Properties[existing] = MergeProperty(model.Properties[existing], parsed);
                    } else {
                        model.Properties.Add(parsed);
                    }
                }
            }
        }

        return model;
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
        // rename. Qualify by where it came from instead - the provenance is what actually differs.
        if (collector.IsTaken(name)) {
            name += "_" + StableSuffix(parentName + "/" + propertyName);
        }

        // Claimed before the children are read, since they are lifted during that read.
        collector.Reserve(name);

        var model = ParseObjectSchema(name, schema, collector);

        model.Description = FirstNonEmpty(schema.Description);
        model.IsDeprecated = schema.Deprecated;

        collector.Add(model);

        return "#/components/schemas/" + name;
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

        // Only keep $ref when it points to an object or enum that gets a generated C# type.
        // Primitive refs (e.g. CustomId → string) are inlined to their underlying type.
        var nonPrimitiveRef = GetNonPrimitiveRef(prop);
        if (nonPrimitiveRef != null) {
            model.Ref = nonPrimitiveRef;
            return model;
        }

        if (prop.Enum is { Count: > 0 }) {
            model.Type = "string";
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

        // The base of a polymorphic hierarchy gets a generated type like any other object.
        if (schema.OneOf is { Count: > 0 } && schema.Discriminator != null) return SchemaRef(schema);

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

    private static void ParsePath(string path, IOpenApiPathItem pathItem,
        Dictionary<string, List<OperationModel>> operationsByTag, SchemaCollector collector,
        bool groupUntaggedByPath) {
        foreach (var opKvp in pathItem.Operations ?? Enumerable.Empty<KeyValuePair<HttpMethod, OpenApiOperation>>()) {
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
                IsDeprecated = operation.Deprecated
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

                foreach (var respKvp in operation.Responses.Where(r => r.Key.StartsWith("2")).OrderBy(r => r.Key)) {
                    var response = respKvp.Value;
                    if (int.TryParse(respKvp.Key, out var statusCode)) {
                        opModel.SuccessStatusCode = statusCode;
                    }

                    if (response.Content != null) {
                        var responseContent = SelectMediaType(response.Content);

                        if (responseContent.Value?.Schema != null) {
                            var responseSchema = responseContent.Value.Schema;
                            opModel.ResponseContentType = responseContent.Key;
                            opModel.ResponseRef = SchemaRef(responseSchema);
                            opModel.ResponseType = SchemaType(responseSchema);
                            opModel.ResponseFormat = responseSchema.Format;
                            opModel.ResponseIsArray = SchemaType(responseSchema) == "array";
                            opModel.ResponseArrayItemsRef = SchemaRef(responseSchema.Items);
                        }
                    }

                    break;
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
    private static string GenerateOperationId(string method, string path) {
        var name = new StringBuilder(method.ToLowerInvariant());

        foreach (var segment in path.Split('/')) {
            if (segment.Length == 0) {
                continue;
            }

            if (segment[0] == '{') {
                name.Append("By");
                name.Append(NamingHelper.ToPascalCase(segment.Trim('{', '}')));
            } else {
                name.Append(NamingHelper.ToPascalCase(segment));
            }
        }

        return name.ToString();
    }

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
    /// An enum member, when it is one a string-typed C# member can carry.
    /// </summary>
    /// <remarks>
    /// Only strings, which is what the previous reader's <c>OfType&lt;OpenApiString&gt;</c> filter
    /// amounted to. A boolean or numeric <c>enum</c> - Stripe writes <c>deleted: {type: boolean,
    /// enum: [true]}</c> - is still dropped, and would need the property to stop being typed as a
    /// string before its values could mean anything. Widening it here alone emits
    /// <c>[AllowedValues("true")]</c> against a member that never holds <c>"true"</c>.
    /// </remarks>
    private static string? EnumMember(JsonNode? value) =>
        value is JsonValue jsonValue && jsonValue.GetValueKind() == JsonValueKind.String
            ? jsonValue.GetValue<string>()
            : null;

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
