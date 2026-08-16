using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Hardened.Idl.Models;
using Hardened.Idl;

namespace Hardened.OpenApi.SourceGenerator;

internal static class OpenApiSpecParser {
    public static ServiceSpecModel? Parse(
        string text, string fileName, CancellationToken cancellationToken,
        bool applyServerBasePath = false) {
        cancellationToken.ThrowIfCancellationRequested();

        OpenApiDocument? document;
        try {
            var reader = new OpenApiStringReader();
            document = reader.Read(text, out var diagnostic);
        } catch (Exception) {
            return null;
        }

        if (document == null) {
            return null;
        }

        var model = new ServiceSpecModel { FileName = fileName };

        // Schemas the document does not name, lifted out of the places they were written inline.
        // Collected separately and appended, so a synthesized name colliding with a declared one is
        // visible to SpecDiagnostics as a duplicate rather than quietly overwriting it.
        var synthesized = new List<SchemaModel>();

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
            filterTypesExt is OpenApiObject filterTypesObj) {
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
                ParsePath(basePath + pathKvp.Key, pathKvp.Value, operationsByTag, synthesized);
            }
        }

        foreach (var schema in synthesized) {
            model.Schemas.Add(schema);
        }

        foreach (var kvp in operationsByTag) {
            model.Services.Add(new ServiceModel {
                Tag = kvp.Key,
                Operations = kvp.Value
            });
        }

        return model;
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
        string name, OpenApiSchema schema, List<SchemaModel> collector) {
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
    private static void ParsePolymorphism(OpenApiSchema schema, SchemaModel model) {
        if (schema.Discriminator != null &&
            !string.IsNullOrEmpty(schema.Discriminator.PropertyName)) {
            model.DiscriminatorPropertyName = schema.Discriminator.PropertyName;

            if (schema.Discriminator.Mapping != null) {
                foreach (var mapping in schema.Discriminator.Mapping) {
                    model.DiscriminatorMapping.Add(new DiscriminatorMappingModel {
                        Value = mapping.Key,
                        Ref = mapping.Value
                    });
                }
            }

            // No explicit mapping means the branches are the derived types, keyed by their own
            // names - which is what the specification says a bare discriminator implies.
            if (model.DiscriminatorMapping.Count == 0 && schema.OneOf is { Count: > 0 }) {
                foreach (var branch in schema.OneOf) {
                    var reference = branch.Reference?.ReferenceV3;

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
                if (branch.Reference?.ReferenceV3 != null && branch.Discriminator != null) {
                    model.BaseRef = branch.Reference.ReferenceV3;
                    break;
                }
            }
        }
    }

    private static SchemaModel? ParseSchemaKind(
        string name, OpenApiSchema schema, List<SchemaModel> collector) {
        if (schema.Enum is { Count: > 0 }) {
            return new SchemaModel {
                Name = name,
                Kind = SchemaKind.Enum,
                EnumValues = schema.Enum
                    .OfType<Microsoft.OpenApi.Any.OpenApiString>()
                    .Select(e => e.Value)
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

        if (schema.Type == "object" || schema.Properties is { Count: > 0 }) {
            if (schema.AdditionalProperties != null && (schema.Properties == null || schema.Properties.Count == 0)) {
                return ParseDictionarySchema(name, schema);
            }
            return ParseObjectSchema(name, schema, collector);
        }

        if (schema.Type == "array") {
            return ParseArraySchema(name, schema);
        }

        return new SchemaModel {
            Name = name,
            Kind = SchemaKind.Primitive,
            Type = schema.Type,
            Format = schema.Format
        };
    }

    private static SchemaModel ParseObjectSchema(
        string name, OpenApiSchema schema, List<SchemaModel> collector) {
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
        string name, OpenApiSchema schema, List<SchemaModel> collector) {
        var model = new SchemaModel {
            Name = name,
            Kind = SchemaKind.Object,
            Required = schema.Required?.ToList() ?? new List<string>()
        };

        foreach (var allOfSchema in schema.AllOf) {
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
                    model.Properties.Add(
                        ParseProperty(propKvp.Key, propKvp.Value, isRequired, name, collector));
                }
            }
        }

        return model;
    }

    private static SchemaModel ParseDictionarySchema(string name, OpenApiSchema schema) {
        var addlProps = schema.AdditionalProperties;
        return new SchemaModel {
            Name = name,
            Kind = SchemaKind.Dictionary,
            DictionaryValueType = addlProps?.Type,
            DictionaryValueRef = GetNonPrimitiveRef(addlProps)
        };
    }

    private static SchemaModel ParseArraySchema(string name, OpenApiSchema schema) {
        return new SchemaModel {
            Name = name,
            Kind = SchemaKind.Array,
            ArrayItemsRef = GetNonPrimitiveRef(schema.Items),
            ArrayItemsType = schema.Items?.Type,
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
        string parentName, string propertyName, OpenApiSchema schema, List<SchemaModel> collector) {
        var name = NamingHelper.ToPascalCase(parentName) + NamingHelper.ToPascalCase(propertyName);

        var model = ParseObjectSchema(name, schema, collector);

        model.Description = FirstNonEmpty(schema.Description);
        model.IsDeprecated = schema.Deprecated;

        collector.Add(model);

        return "#/components/schemas/" + name;
    }

    private static PropertyModel ParseProperty(
        string name, OpenApiSchema prop, bool isRequired, string parentName, List<SchemaModel> collector) {
        var model = new PropertyModel {
            Name = name,
            IsRequired = isRequired,
            IsNullable = prop.Nullable,
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
                .OfType<Microsoft.OpenApi.Any.OpenApiString>()
                .Select(e => e.Value)
                .ToList();
            return model;
        }

        if (prop.Type == "array") {
            model.IsArray = true;
            model.ArrayItemsRef = GetNonPrimitiveRef(prop.Items);
            model.ArrayItemsType = prop.Items?.Type;
            model.ArrayItemsFormat = prop.Items?.Format;
            return model;
        }

        if (prop.Type == "object" && prop.AdditionalProperties != null) {
            model.IsDictionary = true;
            model.DictionaryValueType = prop.AdditionalProperties.Type;
            model.DictionaryValueRef = GetNonPrimitiveRef(prop.AdditionalProperties);
            return model;
        }

        // An object with properties but no name of its own. Lifted into one rather than left to
        // fall through to JsonElement, which discarded the nested shape entirely.
        if (prop.Properties is { Count: > 0 }) {
            model.Ref = SynthesizeSchema(parentName, name, prop, collector);
            return model;
        }

        model.Type = prop.Type;
        model.Format = prop.Format;
        return model;
    }

    private static void ExtractValidationConstraints(OpenApiSchema schema, PropertyModel model) {
        // Not constraints, but read here because this is the one place a property's own schema is in
        // hand. They shape the generated type rather than validating it - see PropertyModel.
        model.IsReadOnly = schema.ReadOnly;
        model.IsWriteOnly = schema.WriteOnly;

        if (schema.MinLength.HasValue) model.MinLength = schema.MinLength;
        if (schema.MaxLength.HasValue) model.MaxLength = schema.MaxLength;
        if (schema.Minimum.HasValue) model.Minimum = schema.Minimum.Value;
        if (schema.Maximum.HasValue) model.Maximum = schema.Maximum.Value;
        if (schema.ExclusiveMinimum == true) model.ExclusiveMinimum = true;
        if (schema.ExclusiveMaximum == true) model.ExclusiveMaximum = true;
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
    private static string? GetNonPrimitiveRef(OpenApiSchema? schema) {
        if (schema?.Reference == null) return null;

        // Enums and objects get generated C# types — keep the ref.
        if (schema.Enum is { Count: > 0 }) return schema.Reference.ReferenceV3;
        if (schema.Type == "object" || schema.Properties is { Count: > 0 }) return schema.Reference.ReferenceV3;
        if (schema.Type == "array") return schema.Reference.ReferenceV3;
        if (schema.AllOf is { Count: > 0 }) return schema.Reference.ReferenceV3;

        // The base of a polymorphic hierarchy gets a generated type like any other object.
        if (schema.OneOf is { Count: > 0 } && schema.Discriminator != null) return schema.Reference.ReferenceV3;

        // Primitive types (string, integer, number, boolean) — inline them.
        return null;
    }

    /// <summary>
    /// Follows $ref to resolve the actual schema with properties.
    /// OpenApiSchema.Reference is non-null for $ref entries; the reader
    /// already resolves these into the same schema object graph, so
    /// properties/required are on the same object.
    /// </summary>
    private static OpenApiSchema? ResolveSchema(OpenApiSchema? schema) {
        if (schema == null) return null;

        // If the schema has properties directly, use it
        if (schema.Properties is { Count: > 0 }) return schema;

        // For allOf, merge properties
        if (schema.AllOf is { Count: > 0 }) {
            var merged = new OpenApiSchema {
                Required = new HashSet<string>(schema.Required ?? Enumerable.Empty<string>())
            };
            foreach (var allOfSchema in schema.AllOf) {
                if (allOfSchema.Required != null) {
                    foreach (var req in allOfSchema.Required) {
                        merged.Required.Add(req);
                    }
                }
                if (allOfSchema.Properties != null) {
                    merged.Properties ??= new Dictionary<string, OpenApiSchema>();
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
    private static IEnumerable<OpenApiParameter> MergeParameters(
        IList<OpenApiParameter>? pathItemParameters, IList<OpenApiParameter>? operationParameters) {
        if (pathItemParameters == null || pathItemParameters.Count == 0) {
            return operationParameters ?? (IList<OpenApiParameter>)new List<OpenApiParameter>();
        }

        if (operationParameters == null || operationParameters.Count == 0) {
            return pathItemParameters;
        }

        var merged = new List<OpenApiParameter>();
        var overridden = new List<OpenApiParameter>();

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
    private static bool SameParameter(OpenApiParameter left, OpenApiParameter right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) && left.In == right.In;

    private static ParameterModel? ParseParameter(OpenApiParameter param) {
        // Skip parameters marked with x-codegen-exclude
        if (param.Extensions != null &&
            param.Extensions.TryGetValue("x-codegen-exclude", out var excludeExt) &&
            excludeExt is OpenApiBoolean excludeBool && excludeBool.Value) {
            return null;
        }

        var paramModel = new ParameterModel {
            Name = param.Name,
            In = param.In?.ToString()?.ToLowerInvariant() ?? "query",
            IsNullable = param.Schema?.Nullable ?? false,
            Default = GetOpenApiPrimitiveValue(param.Schema?.Default),
            Description = FirstNonEmpty(param.Description),
            IsRequired = param.Required,
            Type = param.Schema?.Type,
            Format = param.Schema?.Format,
            Ref = GetNonPrimitiveRef(param.Schema),
            IsArray = param.Schema?.Type == "array",
            ArrayItemsType = param.Schema?.Items?.Type,
            ArrayItemsRef = GetNonPrimitiveRef(param.Schema?.Items)
        };

        // Extract validation constraints from parameter schema
        if (param.Schema != null) {
            if (param.Schema.MinLength.HasValue) paramModel.MinLength = param.Schema.MinLength;
            if (param.Schema.MaxLength.HasValue) paramModel.MaxLength = param.Schema.MaxLength;
            if (param.Schema.Minimum.HasValue) paramModel.Minimum = param.Schema.Minimum.Value;
            if (param.Schema.Maximum.HasValue) paramModel.Maximum = param.Schema.Maximum.Value;
            if (param.Schema.ExclusiveMinimum == true) paramModel.ExclusiveMinimum = true;
            if (param.Schema.ExclusiveMaximum == true) paramModel.ExclusiveMaximum = true;
            if (!string.IsNullOrEmpty(param.Schema.Pattern)) paramModel.Pattern = param.Schema.Pattern;
            if (param.Schema.MinItems.HasValue) paramModel.MinItems = param.Schema.MinItems;
            if (param.Schema.MaxItems.HasValue) paramModel.MaxItems = param.Schema.MaxItems;
            if (param.Schema.Enum is { Count: > 0 }) {
                paramModel.EnumValues = param.Schema.Enum
                    .OfType<Microsoft.OpenApi.Any.OpenApiString>()
                    .Select(e => e.Value)
                    .ToList();
            }
        }

        return paramModel;
    }

    private static void ParsePath(string path, OpenApiPathItem pathItem,
        Dictionary<string, List<OperationModel>> operationsByTag, List<SchemaModel> collector) {
        foreach (var opKvp in pathItem.Operations) {
            var operation = opKvp.Value;
            var httpMethod = opKvp.Key.ToString().ToUpperInvariant();

            var tag = operation.Tags?.FirstOrDefault()?.Name ?? "Default";
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
                    opModel.RequestBodyRef = bodySchema.Reference?.ReferenceV3;
                    opModel.RequestBodyType = bodySchema.Type;

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
                        Ref = errorContent.Value?.Schema?.Reference?.ReferenceV3,
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
                            opModel.ResponseRef = responseSchema.Reference?.ReferenceV3;
                            opModel.ResponseType = responseSchema.Type;
                            opModel.ResponseFormat = responseSchema.Format;
                            opModel.ResponseIsArray = responseSchema.Type == "array";
                            opModel.ResponseArrayItemsRef = responseSchema.Items?.Reference?.ReferenceV3;
                        }
                    }

                    break;
                }
            }

            // Parse x-filters extension on the operation
            if (operation.Extensions != null &&
                operation.Extensions.TryGetValue("x-filters", out var filtersExt) &&
                filtersExt is OpenApiObject filtersObj) {
                opModel.FilterInstances = ParseFilterInstances(filtersObj);
            }

            // x-hardened-raw-bytes opts the signature into byte[] for a response the spec types as
            // a string. Also not something the content map can say: text/plain describes the wire,
            // not whether the application holds the payload already encoded.
            if (operation.Extensions != null &&
                operation.Extensions.TryGetValue("x-hardened-raw-bytes", out var rawBytesExt) &&
                rawBytesExt is OpenApiBoolean { Value: true }) {
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
    private static KeyValuePair<string, OpenApiMediaType> SelectMediaType(
        IDictionary<string, OpenApiMediaType> content) {
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

    private static string GenerateOperationId(string method, string path) {
        var parts = path.Split('/')
            .Where(p => !string.IsNullOrEmpty(p) && !p.StartsWith("{"))
            .Select(NamingHelper.ToPascalCase);

        return method.ToLowerInvariant() + string.Join("", parts);
    }

    // ── x-filter-types parsing ─────────────────────────────────────────

    private static FilterTypeModel? ParseFilterType(string name, IOpenApiAny value) {
        if (value is not OpenApiObject obj) return null;

        var model = new FilterTypeModel { Name = name };

        if (obj.TryGetValue("namespace", out var nsValue) && nsValue is OpenApiString nsStr) {
            model.Namespace = nsStr.Value;
        }

        if (obj.TryGetValue("generate", out var genValue) && genValue is OpenApiBoolean genBool) {
            model.Generate = genBool.Value;
        }

        if (obj.TryGetValue("properties", out var propsValue) && propsValue is OpenApiObject propsObj) {
            foreach (var propKvp in propsObj) {
                var prop = ParseFilterTypeProperty(propKvp.Key, propKvp.Value);
                if (prop != null) {
                    model.Properties.Add(prop);
                }
            }
        }

        return model.Namespace.Length > 0 ? model : null;
    }

    private static FilterTypePropertyModel? ParseFilterTypeProperty(string name, IOpenApiAny value) {
        if (value is not OpenApiObject obj) return null;

        var prop = new FilterTypePropertyModel { Name = name };

        if (obj.TryGetValue("type", out var typeValue) && typeValue is OpenApiString typeStr) {
            prop.CSharpType = MapFilterPropertyType(typeStr.Value);
        }

        if (obj.TryGetValue("default", out var defaultValue)) {
            prop.Default = GetOpenApiPrimitiveValue(defaultValue);
        }

        if (obj.TryGetValue("enum", out var enumValue) && enumValue is OpenApiArray enumArr) {
            prop.EnumValues = enumArr
                .OfType<OpenApiString>()
                .Select(s => s.Value)
                .ToList();
        }

        if (obj.TryGetValue("enumType", out var enumTypeValue) && enumTypeValue is OpenApiString enumTypeStr) {
            prop.EnumType = enumTypeStr.Value;
        }

        return prop;
    }

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

    private static List<FilterInstanceModel> ParseFilterInstances(OpenApiObject filtersObj) {
        var instances = new List<FilterInstanceModel>();

        foreach (var kvp in filtersObj) {
            var instance = new FilterInstanceModel { FilterTypeName = kvp.Key };

            if (kvp.Value is OpenApiObject propsObj) {
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
    private static string? GetOpenApiPrimitiveValue(IOpenApiAny? value) {
        return value switch {
            OpenApiString s => s.Value,
            OpenApiInteger i => i.Value.ToString(),
            OpenApiLong l => l.Value.ToString(),
            OpenApiBoolean b => b.Value ? "true" : "false",
            OpenApiDouble d => d.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            OpenApiFloat f => f.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };
    }
}
