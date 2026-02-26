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
            DictionaryValueRef = addlProps?.Reference?.ReferenceV3
        };
    }

    private static SchemaModel ParseArraySchema(string name, OpenApiSchema schema) {
        return new SchemaModel {
            Name = name,
            Kind = SchemaKind.Array,
            ArrayItemsRef = schema.Items?.Reference?.ReferenceV3,
            ArrayItemsType = schema.Items?.Type,
            ArrayItemsFormat = schema.Items?.Format
        };
    }

    private static PropertyModel ParseProperty(string name, OpenApiSchema prop, bool isRequired) {
        var model = new PropertyModel {
            Name = name,
            IsRequired = isRequired
        };

        if (prop.Reference != null) {
            model.Ref = prop.Reference.ReferenceV3;
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
            model.ArrayItemsRef = prop.Items?.Reference?.ReferenceV3;
            model.ArrayItemsType = prop.Items?.Type;
            model.ArrayItemsFormat = prop.Items?.Format;
            return model;
        }

        if (prop.Type == "object" && prop.AdditionalProperties != null) {
            model.IsDictionary = true;
            model.DictionaryValueType = prop.AdditionalProperties.Type;
            model.DictionaryValueRef = prop.AdditionalProperties.Reference?.ReferenceV3;
            return model;
        }

        model.Type = prop.Type;
        model.Format = prop.Format;
        return model;
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
                    opModel.Parameters.Add(new ParameterModel {
                        Name = param.Name,
                        In = param.In?.ToString()?.ToLowerInvariant() ?? "query",
                        IsRequired = param.Required,
                        Type = param.Schema?.Type,
                        Format = param.Schema?.Format,
                        Ref = param.Schema?.Reference?.ReferenceV3,
                        IsArray = param.Schema?.Type == "array",
                        ArrayItemsType = param.Schema?.Items?.Type,
                        ArrayItemsRef = param.Schema?.Items?.Reference?.ReferenceV3
                    });
                }
            }

            if (operation.RequestBody?.Content != null) {
                var jsonContent = operation.RequestBody.Content
                    .FirstOrDefault(c => c.Key.Contains("json"));

                if (jsonContent.Value?.Schema != null) {
                    var bodySchema = jsonContent.Value.Schema;
                    opModel.RequestBodyRef = bodySchema.Reference?.ReferenceV3;
                    opModel.RequestBodyType = bodySchema.Type;
                }
            }

            if (operation.Responses != null) {
                foreach (var respKvp in operation.Responses.Where(r => r.Key.StartsWith("2")).OrderBy(r => r.Key)) {
                    var response = respKvp.Value;
                    if (int.TryParse(respKvp.Key, out var statusCode)) {
                        opModel.SuccessStatusCode = statusCode;
                    }

                    if (response.Content != null) {
                        var jsonResponse = response.Content
                            .FirstOrDefault(c => c.Key.Contains("json"));

                        if (jsonResponse.Value?.Schema != null) {
                            var responseSchema = jsonResponse.Value.Schema;
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

            if (!operationsByTag.TryGetValue(tag, out var list)) {
                list = new List<OperationModel>();
                operationsByTag[tag] = list;
            }

            list.Add(opModel);
        }
    }

    private static string GenerateOperationId(string method, string path) {
        var parts = path.Split('/')
            .Where(p => !string.IsNullOrEmpty(p) && !p.StartsWith("{"))
            .Select(NamingHelper.ToPascalCase);

        return method.ToLowerInvariant() + string.Join("", parts);
    }
}
