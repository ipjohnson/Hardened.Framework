using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Hardened.OpenApi.SourceGenerator.Models;

namespace Hardened.OpenApi.SourceGenerator;

internal static class OpenApiSpecParser {
    public static OpenApiSpecModel? Parse(string text, string fileName, CancellationToken cancellationToken) {
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

        var model = new OpenApiSpecModel { FileName = fileName };

        if (document.Components?.Schemas != null) {
            foreach (var kvp in document.Components.Schemas) {
                cancellationToken.ThrowIfCancellationRequested();
                var schema = ParseSchema(kvp.Key, kvp.Value);
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

        if (document.Paths != null) {
            foreach (var pathKvp in document.Paths) {
                cancellationToken.ThrowIfCancellationRequested();
                ParsePath(pathKvp.Key, pathKvp.Value, operationsByTag);
            }
        }

        foreach (var kvp in operationsByTag) {
            model.Services.Add(new ServiceModel {
                Tag = kvp.Key,
                Operations = kvp.Value
            });
        }

        return model;
    }

    private static SchemaModel? ParseSchema(string name, OpenApiSchema schema) {
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
            return ParseAllOf(name, schema);
        }

        if (schema.Type == "object" || schema.Properties is { Count: > 0 }) {
            if (schema.AdditionalProperties != null && (schema.Properties == null || schema.Properties.Count == 0)) {
                return ParseDictionarySchema(name, schema);
            }
            return ParseObjectSchema(name, schema);
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

    private static SchemaModel ParseObjectSchema(string name, OpenApiSchema schema) {
        var model = new SchemaModel {
            Name = name,
            Kind = SchemaKind.Object,
            Required = schema.Required?.ToList() ?? new List<string>()
        };

        if (schema.Properties != null) {
            foreach (var propKvp in schema.Properties) {
                model.Properties.Add(ParseProperty(propKvp.Key, propKvp.Value,
                    model.Required.Contains(propKvp.Key)));
            }
        }

        return model;
    }

    private static SchemaModel ParseAllOf(string name, OpenApiSchema schema) {
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
                    model.Properties.Add(ParseProperty(propKvp.Key, propKvp.Value, isRequired));
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

    private static PropertyModel ParseProperty(string name, OpenApiSchema prop, bool isRequired) {
        var model = new PropertyModel {
            Name = name,
            IsRequired = isRequired
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

        model.Type = prop.Type;
        model.Format = prop.Format;
        return model;
    }

    private static void ExtractValidationConstraints(OpenApiSchema schema, PropertyModel model) {
        if (schema.MinLength.HasValue) model.MinLength = schema.MinLength;
        if (schema.MaxLength.HasValue) model.MaxLength = schema.MaxLength;
        if (schema.Minimum.HasValue) model.Minimum = schema.Minimum.Value;
        if (schema.Maximum.HasValue) model.Maximum = schema.Maximum.Value;
        if (schema.ExclusiveMinimum == true) model.ExclusiveMinimum = true;
        if (schema.ExclusiveMaximum == true) model.ExclusiveMaximum = true;
        if (!string.IsNullOrEmpty(schema.Pattern)) model.Pattern = schema.Pattern;
        if (schema.MinItems.HasValue) model.MinItems = schema.MinItems;
        if (schema.MaxItems.HasValue) model.MaxItems = schema.MaxItems;
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

    private static void ParsePath(string path, OpenApiPathItem pathItem,
        Dictionary<string, List<OperationModel>> operationsByTag) {
        foreach (var opKvp in pathItem.Operations) {
            var operation = opKvp.Value;
            var httpMethod = opKvp.Key.ToString().ToUpperInvariant();

            var tag = operation.Tags?.FirstOrDefault()?.Name ?? "Default";
            var operationId = operation.OperationId ?? GenerateOperationId(httpMethod, path);

            var opModel = new OperationModel {
                OperationId = operationId,
                Path = path,
                HttpMethod = httpMethod,
                Tag = tag
            };

            if (operation.Parameters != null) {
                foreach (var param in operation.Parameters) {
                    // Skip parameters marked with x-codegen-exclude
                    if (param.Extensions != null &&
                        param.Extensions.TryGetValue("x-codegen-exclude", out var excludeExt) &&
                        excludeExt is OpenApiBoolean excludeBool && excludeBool.Value) {
                        continue;
                    }

                    var paramModel = new ParameterModel {
                        Name = param.Name,
                        In = param.In?.ToString()?.ToLowerInvariant() ?? "query",
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
                                    ParseProperty(propKvp.Key, propKvp.Value, isRequired));
                            }
                        }
                    }
                }
            }

            if (operation.Responses != null) {
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

            // x-hardened-template names the view the operation's model is rendered through. The
            // spec's own content map cannot carry this: it says the response is text/html, which is
            // true and says nothing about which view produced it.
            if (operation.Extensions != null &&
                operation.Extensions.TryGetValue("x-hardened-template", out var templateExt) &&
                templateExt is OpenApiString templateName &&
                !string.IsNullOrWhiteSpace(templateName.Value)) {
                opModel.TemplateName = templateName.Value;
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
    private static string? GetOpenApiPrimitiveValue(IOpenApiAny value) {
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
