using System;
using System.Globalization;
using System.Text;
using Hardened.Idl.Models;

namespace Hardened.Idl;

/// <summary>
/// Writes an <see cref="ServiceSpecModel"/> to text and reads it back. The private contract between
/// the build task, which parses the yaml, and the source generator, which never opens it.
/// </summary>
/// <remarks>
/// <para>
/// A purpose-built line format rather than JSON. System.Text.Json is not in the box on the .NET
/// Framework MSBuild that hosts Roslyn in Visual Studio, so using it would trade the three embedded
/// OpenAPI assemblies this move exists to delete for another embedded assembly. Both ends ship in
/// one package, so there is no version skew to design around.
/// </para>
/// <para>
/// One record per line: a tag, then tab-separated <c>Key=Value</c> pairs. Parents are implied by the
/// tag rather than by indentation - a <c>prop</c> belongs to the last <c>schema</c>, a <c>param</c>
/// to the last <c>op</c> - so the reader carries three cursors and no stack.
/// </para>
/// <para>
/// <b>An omitted key is null, not empty.</b> <c>Key=</c> is the empty string. That distinction is
/// load-bearing: a schema whose <c>Type</c> is null and one whose <c>Type</c> is <c>""</c> generate
/// different C#, and collapsing them was the failure this format is shaped to avoid.
/// </para>
/// </remarks>
internal static class SpecModelSerializer {

    /// <summary>
    /// Bumped when the format changes shape. The reader rejects anything else rather than guessing,
    /// because a half-understood model produces wrong code instead of an error.
    /// </summary>
    /// <remarks>
    /// 3 splits an operation's <c>Summary</c> from its <c>Description</c>. Bumped rather than
    /// treated as an additive key, because the meaning of an existing one changed: in 2,
    /// <c>Description</c> held the summary whenever there was one. A 2 file read by a 3 reader
    /// would find no <c>Summary</c>, take the summary out of <c>Description</c>, and publish a
    /// one-line summary as the long-form description - wrong, and silently. Rejecting a stale
    /// model is what this header is for.
    /// </remarks>
    private const string Header = "#hardened-openapi-model 3";

    private const char FieldSeparator = '\t';

    public static string Write(ServiceSpecModel model) {
        var builder = new StringBuilder();
        builder.Append(Header).Append('\n');

        var spec = new Record("spec");
        spec.Add("FileName", model.FileName);
        spec.Add("JsonTypeInfoResolverName", model.JsonTypeInfoResolverName);
        spec.Add("ContentNegotiation", model.ContentNegotiation);
        spec.Add("ResponseModel", model.ResponseModel.ToString());
        spec.Add("PublishUrl", model.PublishUrl);
        spec.Add("UiUrl", model.UiUrl);
        spec.Add("UiEnvironments", model.UiEnvironments);
        spec.WriteTo(builder);

        foreach (var schema in model.Schemas) {
            WriteSchema(builder, schema);
        }

        foreach (var service in model.Services) {
            WriteService(builder, service);
        }

        foreach (var filterType in model.FilterTypes) {
            WriteFilterType(builder, filterType);
        }

        foreach (var validated in model.ValidatedOperations) {
            var record = new Record("validated");
            record.Add("OperationId", validated.OperationId);
            record.Add("InterfaceName", validated.InterfaceName);
            record.WriteTo(builder);
        }

        return builder.ToString();
    }

    public static ServiceSpecModel Read(string text) {
        var model = new ServiceSpecModel();

        SchemaModel? schema = null;
        ServiceModel? service = null;
        OperationModel? operation = null;
        FilterTypeModel? filterType = null;
        FilterInstanceModel? filterInstance = null;

        var sawHeader = false;

        foreach (var rawLine in text.Split('\n')) {
            var line = rawLine.TrimEnd('\r');

            if (line.Length == 0) {
                continue;
            }

            if (!sawHeader) {
                if (line != Header) {
                    throw new FormatException(
                        $"Unrecognised spec model format. Expected '{Header}', found '{line}'.");
                }

                sawHeader = true;
                continue;
            }

            var record = Record.Parse(line);

            switch (record.Tag) {
                case "spec":
                    model.FileName = record.String("FileName") ?? "";
                    model.JsonTypeInfoResolverName = record.String("JsonTypeInfoResolverName") ?? "";
                    model.ContentNegotiation = record.String("ContentNegotiation") ?? "";
                    model.ResponseModel = ParseResponseModel(record.String("ResponseModel"));
                    model.PublishUrl = record.String("PublishUrl") ?? "";
                    model.UiUrl = record.String("UiUrl") ?? "";
                    model.UiEnvironments = record.String("UiEnvironments") ?? "";
                    break;

                case "schema":
                    schema = ReadSchema(record);
                    model.Schemas.Add(schema);
                    break;

                case "enumvalue":
                    schema?.EnumValues.Add(record.String("Value") ?? "");

                    // Only when it was written. An absent member name means the allocator did not
                    // run, and the model derives one - filling it with the wire value here would
                    // put "available" where "Available" belongs.
                    if (record.String("Member") is { } enumMember) {
                        schema?.EnumMemberNames.Add(enumMember);
                    }

                    break;

                case "required":
                    schema?.Required.Add(record.String("Value") ?? "");
                    break;

                case "prop":
                    schema?.Properties.Add(ReadProperty(record));
                    break;

                case "discriminator":
                    schema?.DiscriminatorMapping.Add(new DiscriminatorMappingModel {
                        Value = record.String("Value") ?? "",
                        Ref = record.String("Ref") ?? ""
                    });
                    break;

                case "service":
                    service = new ServiceModel {
                        Tag = record.String("Tag") ?? "",
                        TypeBaseName = record.String("TypeBaseName") ?? "",
                        DispatchHeader = record.String("DispatchHeader")
                    };
                    model.Services.Add(service);
                    break;

                case "op":
                    operation = ReadOperation(record);
                    service?.Operations.Add(operation);
                    break;

                case "param":
                    operation?.Parameters.Add(ReadParameter(record));
                    break;

                case "successresponse":
                    operation?.SuccessResponses.Add(new SuccessResponseModel {
                        StatusCode = record.Int("StatusCode") ?? 0,
                        Ref = record.String("Ref"),
                        Type = record.String("Type"),
                        Format = record.String("Format"),
                        IsArray = record.Bool("IsArray"),
                        ArrayItemsRef = record.String("ArrayItemsRef"),
                        ArrayItemsType = record.String("ArrayItemsType"),
                        ContentType = record.String("ContentType"),
                        Description = record.String("Description"),
                    });
                    break;

                case "errorresponse":
                    operation?.ErrorResponses.Add(new ErrorResponseModel {
                        StatusCode = record.Int("StatusCode") ?? 0,
                        Ref = record.String("Ref"),
                        Description = record.String("Description"),
                    });
                    break;

                case "bodyprop":
                    operation?.RequestBodyProperties.Add(ReadProperty(record));
                    break;

                case "bodyrequired":
                    operation?.RequestBodyRequired.Add(record.String("Value") ?? "");
                    break;

                // One line per OR-branch. Grants are tab-safe by construction - a scope name
                // containing a tab would already have broken the format - so they travel joined by
                // a space rather than as one record each, which keeps a document declaring the same
                // requirement on 200 operations from doubling the file.
                case "authbranch":
                    operation?.AuthorizationBranches.Add(new AuthorizationBranchModel {
                        RequiresAuthentication = record.Bool("Authenticated"),
                        Grants = Split(record.String("Grants")),
                    });
                    break;

                case "filterinstance":
                    filterInstance = new FilterInstanceModel {
                        FilterTypeName = record.String("FilterTypeName") ?? "",
                    };
                    operation?.FilterInstances.Add(filterInstance);
                    break;

                case "filtervalue":
                    if (filterInstance is not null) {
                        filterInstance.PropertyValues[record.String("Key") ?? ""] = record.String("Value") ?? "";
                    }

                    break;

                case "validated":
                    model.ValidatedOperations.Add(new ValidatedOperationModel {
                        OperationId = record.String("OperationId") ?? "",
                        InterfaceName = record.String("InterfaceName") ?? "",
                    });
                    break;

                case "filtertype":
                    filterType = new FilterTypeModel {
                        Name = record.String("Name") ?? "",
                        Namespace = record.String("Namespace") ?? "",
                        Generate = record.Bool("Generate"),
                    };
                    model.FilterTypes.Add(filterType);
                    break;

                case "filterprop":
                    filterType?.Properties.Add(new FilterTypePropertyModel {
                        Name = record.String("Name") ?? "",
                        CSharpType = record.String("CSharpType") ?? "string",
                        Default = record.String("Default"),
                        EnumType = record.String("EnumType"),
                        EnumValues = record.Strings("EnumValues"),
                    });
                    break;

                default:
                    throw new FormatException($"Unrecognised spec model record '{record.Tag}'.");
            }
        }

        if (!sawHeader) {
            throw new FormatException("Spec model is empty; expected a format header.");
        }

        return model;
    }

    private static void WriteSchema(StringBuilder builder, SchemaModel schema) {
        var record = new Record("schema");
        record.Add("Name", schema.Name);
        record.Add("Kind", schema.Kind.ToString());
        record.Add("Description", schema.Description);
        record.Add("IsDeprecated", schema.IsDeprecated);
        record.Add("EnumMemberNamesAreDeclared", schema.EnumMemberNamesAreDeclared);
        record.Add("DiscriminatorPropertyName", schema.DiscriminatorPropertyName);
        record.Add("BaseRef", schema.BaseRef);
        record.Add("Type", schema.Type);
        record.Add("Format", schema.Format);
        record.Add("ArrayItemsRef", schema.ArrayItemsRef);
        record.Add("ArrayItemsType", schema.ArrayItemsType);
        record.Add("ArrayItemsFormat", schema.ArrayItemsFormat);
        record.Add("DictionaryValueType", schema.DictionaryValueType);
        record.Add("DictionaryValueRef", schema.DictionaryValueRef);
        record.WriteTo(builder);

        for (var i = 0; i < schema.EnumValues.Count; i++) {
            var enumValue = new Record("enumvalue");
            enumValue.Add("Value", schema.EnumValues[i]);

            // The allocated member name travels with the wire value; the generator side reads this
            // file and must not re-derive it.
            enumValue.Add("Member",
                i < schema.EnumMemberNames.Count ? schema.EnumMemberNames[i] : null);

            enumValue.WriteTo(builder);
        }

        foreach (var value in schema.Required) {
            var required = new Record("required");
            required.Add("Value", value);
            required.WriteTo(builder);
        }

        foreach (var mapping in schema.DiscriminatorMapping) {
            var record2 = new Record("discriminator");
            record2.Add("Value", mapping.Value);
            record2.Add("Ref", mapping.Ref);
            record2.WriteTo(builder);
        }

        foreach (var property in schema.Properties) {
            WriteProperty(builder, "prop", property);
        }
    }

    /// <summary>
    /// The kind as written, rather than "Enum or else Object".
    /// </summary>
    /// <remarks>
    /// Every kind but <see cref="SchemaKind.Enum"/> used to read back as
    /// <see cref="SchemaKind.Object"/>, so Primitive, Array and Dictionary were destroyed crossing
    /// the seam between the task and the generator. Mostly invisible, because the generator half
    /// barely switches on it - and a trap for any kind added later, which would have been silently
    /// collapsed the same way.
    /// </remarks>
    /// <summary>A space-joined list back to its parts, with an absent or empty value as empty.</summary>
    private static List<string> Split(string? value) =>
        string.IsNullOrEmpty(value)
            ? new List<string>()
            : new List<string>(value!.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

    private static SchemaKind ReadSchemaKind(string? value) {
        switch (value) {
            case nameof(SchemaKind.Enum): return SchemaKind.Enum;
            case nameof(SchemaKind.Array): return SchemaKind.Array;
            case nameof(SchemaKind.Primitive): return SchemaKind.Primitive;
            case nameof(SchemaKind.Dictionary): return SchemaKind.Dictionary;
            default: return SchemaKind.Object;
        }
    }

    private static SchemaModel ReadSchema(Record record) => new() {
        Name = record.String("Name") ?? "",
        Kind = ReadSchemaKind(record.String("Kind")),
        Description = record.String("Description"),
        IsDeprecated = record.Bool("IsDeprecated"),
        EnumMemberNamesAreDeclared = record.Bool("EnumMemberNamesAreDeclared"),
        DiscriminatorPropertyName = record.String("DiscriminatorPropertyName"),
        BaseRef = record.String("BaseRef"),
        Type = record.String("Type"),
        Format = record.String("Format"),
        ArrayItemsRef = record.String("ArrayItemsRef"),
        ArrayItemsType = record.String("ArrayItemsType"),
        ArrayItemsFormat = record.String("ArrayItemsFormat"),
        DictionaryValueType = record.String("DictionaryValueType"),
        DictionaryValueRef = record.String("DictionaryValueRef"),
    };

    private static void WriteProperty(StringBuilder builder, string tag, PropertyModel property) {
        var record = new Record(tag);
        record.Add("Name", property.Name);
        record.Add("Description", property.Description);
        record.Add("Type", property.Type);
        record.Add("Format", property.Format);
        record.Add("Ref", property.Ref);
        record.Add("IsArray", property.IsArray);
        record.Add("ArrayItemsRef", property.ArrayItemsRef);
        record.Add("ArrayItemsType", property.ArrayItemsType);
        record.Add("ArrayItemsFormat", property.ArrayItemsFormat);
        record.Add("IsRequired", property.IsRequired);
        record.Add("IsNullable", property.IsNullable);
        record.Add("IsReadOnly", property.IsReadOnly);
        record.Add("IsWriteOnly", property.IsWriteOnly);
        record.Add("Default", property.Default);
        record.Add("IsDictionary", property.IsDictionary);
        record.Add("DictionaryValueType", property.DictionaryValueType);
        record.Add("DictionaryValueRef", property.DictionaryValueRef);
        record.Add("EnumValues", property.EnumValues);
        record.Add("OneOf", Encode(property.OneOf));
        record.Add("MinLength", property.MinLength);
        record.Add("MaxLength", property.MaxLength);
        record.Add("Minimum", property.Minimum);
        record.Add("Maximum", property.Maximum);
        record.Add("ExclusiveMinimum", property.ExclusiveMinimum);
        record.Add("ExclusiveMaximum", property.ExclusiveMaximum);
        record.Add("Pattern", property.Pattern);
        record.Add("MinItems", property.MinItems);
        record.Add("MaxItems", property.MaxItems);
        record.WriteTo(builder);
    }

    private static List<string> Encode(List<ChoiceBranchModel> branches) {
        var encoded = new List<string>(branches.Count);

        foreach (var branch in branches) {
            encoded.Add(branch.Encoded);
        }

        return encoded;
    }

    private static List<ChoiceBranchModel> Decode(List<string>? encoded) {
        var branches = new List<ChoiceBranchModel>();

        if (encoded == null) {
            return branches;
        }

        foreach (var value in encoded) {
            branches.Add(ChoiceBranchModel.Decode(value));
        }

        return branches;
    }

    private static PropertyModel ReadProperty(Record record) => new() {
        Name = record.String("Name") ?? "",
        Description = record.String("Description"),
        Type = record.String("Type"),
        Format = record.String("Format"),
        Ref = record.String("Ref"),
        IsArray = record.Bool("IsArray"),
        ArrayItemsRef = record.String("ArrayItemsRef"),
        ArrayItemsType = record.String("ArrayItemsType"),
        ArrayItemsFormat = record.String("ArrayItemsFormat"),
        IsRequired = record.Bool("IsRequired"),
        IsNullable = record.Bool("IsNullable"),
        IsReadOnly = record.Bool("IsReadOnly"),
        IsWriteOnly = record.Bool("IsWriteOnly"),
        Default = record.String("Default"),
        IsDictionary = record.Bool("IsDictionary"),
        DictionaryValueType = record.String("DictionaryValueType"),
        DictionaryValueRef = record.String("DictionaryValueRef"),
        EnumValues = record.Strings("EnumValues"),
        OneOf = Decode(record.Strings("OneOf")),
        MinLength = record.Int("MinLength"),
        MaxLength = record.Int("MaxLength"),
        Minimum = record.Decimal("Minimum"),
        Maximum = record.Decimal("Maximum"),
        ExclusiveMinimum = record.Bool("ExclusiveMinimum"),
        ExclusiveMaximum = record.Bool("ExclusiveMaximum"),
        Pattern = record.String("Pattern"),
        MinItems = record.Int("MinItems"),
        MaxItems = record.Int("MaxItems"),
    };

    private static void WriteService(StringBuilder builder, ServiceModel service) {
        var record = new Record("service");
        record.Add("Tag", service.Tag);
        record.Add("TypeBaseName", service.TypeBaseName);
        record.Add("DispatchHeader", service.DispatchHeader);
        record.WriteTo(builder);

        foreach (var operation in service.Operations) {
            WriteOperation(builder, operation);
        }
    }

    private static void WriteOperation(StringBuilder builder, OperationModel operation) {
        var record = new Record("op");
        record.Add("OperationId", operation.OperationId);
        record.Add("MethodName", operation.MethodName);
        record.Add("Path", operation.Path);
        record.Add("HttpMethod", operation.HttpMethod);
        record.Add("DispatchKey", operation.DispatchKey);
        record.Add("Tag", operation.Tag);
        record.Add("Tags", operation.Tags);
        record.Add("Summary", operation.Summary);
        record.Add("Description", operation.Description);
        record.Add("IsDeprecated", operation.IsDeprecated);
        record.Add("RequestBodyContentType", operation.RequestBodyContentType);
        record.Add("RequestBodyRef", operation.RequestBodyRef);
        record.Add("RequestBodyType", operation.RequestBodyType);
        record.Add("ResponseContentType", operation.ResponseContentType);
        record.Add("RawBytesResponse", operation.RawBytesResponse);
        record.Add("ResponseRef", operation.ResponseRef);
        record.Add("ResponseType", operation.ResponseType);
        record.Add("ResponseFormat", operation.ResponseFormat);
        record.Add("ResponseIsArray", operation.ResponseIsArray);
        record.Add("ProducedContentTypes", operation.ProducedContentTypes);
        record.Add("ResponseArrayItemsRef", operation.ResponseArrayItemsRef);
        record.Add("ResponseArrayItemsType", operation.ResponseArrayItemsType);
        record.Add("ResponseArrayItemsFormat", operation.ResponseArrayItemsFormat);
        record.Add("SuccessStatusCode", operation.SuccessStatusCode);
        record.WriteTo(builder);

        // Before the errors, so a reader sees an operation's successes in the order the document
        // declares them and the primary stays first. Written even when there is one, because a
        // reader that has to infer the list from the flat fields is a second definition of the
        // set that can disagree with the first.
        foreach (var successResponse in operation.SuccessResponses) {
            var successRecord = new Record("successresponse");
            successRecord.AddAlways("StatusCode", successResponse.StatusCode.ToString(CultureInfo.InvariantCulture));
            successRecord.Add("Ref", successResponse.Ref);
            successRecord.Add("Type", successResponse.Type);
            successRecord.Add("Format", successResponse.Format);
            successRecord.Add("IsArray", successResponse.IsArray);
            successRecord.Add("ArrayItemsRef", successResponse.ArrayItemsRef);
            successRecord.Add("ArrayItemsType", successResponse.ArrayItemsType);
            successRecord.Add("ContentType", successResponse.ContentType);
            successRecord.Add("Description", successResponse.Description);
            successRecord.WriteTo(builder);
        }

        foreach (var errorResponse in operation.ErrorResponses) {
            var record2 = new Record("errorresponse");
            record2.AddAlways("StatusCode", errorResponse.StatusCode.ToString(CultureInfo.InvariantCulture));
            record2.Add("Ref", errorResponse.Ref);
            record2.Add("Description", errorResponse.Description);
            record2.WriteTo(builder);
        }

        foreach (var parameter in operation.Parameters) {
            WriteParameter(builder, parameter);
        }

        foreach (var property in operation.RequestBodyProperties) {
            WriteProperty(builder, "bodyprop", property);
        }

        foreach (var value in operation.RequestBodyRequired) {
            var required = new Record("bodyrequired");
            required.Add("Value", value);
            required.WriteTo(builder);
        }

        foreach (var branch in operation.AuthorizationBranches) {
            var authBranch = new Record("authbranch");
            authBranch.Add("Authenticated", branch.RequiresAuthentication);
            authBranch.Add("Grants", string.Join(" ", branch.Grants));
            authBranch.WriteTo(builder);
        }

        foreach (var instance in operation.FilterInstances) {
            var filterInstance = new Record("filterinstance");
            filterInstance.Add("FilterTypeName", instance.FilterTypeName);
            filterInstance.WriteTo(builder);

            // Ordered so the file does not reshuffle between builds, which would defeat the
            // Inputs/Outputs check on the target and make every build look dirty.
            foreach (var pair in instance.PropertyValues.OrderBy(p => p.Key, StringComparer.Ordinal)) {
                var value = new Record("filtervalue");
                value.Add("Key", pair.Key);
                value.Add("Value", pair.Value);
                value.WriteTo(builder);
            }
        }
    }

    private static OperationModel ReadOperation(Record record) => new() {
        MethodName = record.String("MethodName") ?? "",
        OperationId = record.String("OperationId") ?? "",
        Path = record.String("Path") ?? "",
        HttpMethod = record.String("HttpMethod") ?? "",
        DispatchKey = record.String("DispatchKey"),
        Tag = record.String("Tag"),
        Tags = record.Strings("Tags") ?? new List<string>(),
        Summary = record.String("Summary"),
        Description = record.String("Description"),
        IsDeprecated = record.Bool("IsDeprecated"),
        RequestBodyContentType = record.String("RequestBodyContentType"),
        RequestBodyRef = record.String("RequestBodyRef"),
        RequestBodyType = record.String("RequestBodyType"),
        ResponseContentType = record.String("ResponseContentType"),
        RawBytesResponse = record.Bool("RawBytesResponse"),
        ResponseRef = record.String("ResponseRef"),
        ResponseType = record.String("ResponseType"),
        ResponseFormat = record.String("ResponseFormat"),
        ResponseIsArray = record.Bool("ResponseIsArray"),
        ProducedContentTypes = record.Strings("ProducedContentTypes") ?? new List<string>(),
        ResponseArrayItemsRef = record.String("ResponseArrayItemsRef"),
        ResponseArrayItemsType = record.String("ResponseArrayItemsType"),
        ResponseArrayItemsFormat = record.String("ResponseArrayItemsFormat"),
        SuccessStatusCode = record.Int("SuccessStatusCode") ?? 200,
    };

    private static void WriteParameter(StringBuilder builder, ParameterModel parameter) {
        var record = new Record("param");
        record.Add("Name", parameter.Name);
        record.Add("MemberNameOverride", parameter.MemberNameOverride);
        record.Add("In", parameter.In);
        record.Add("Description", parameter.Description);
        record.Add("IsRequired", parameter.IsRequired);
        record.Add("IsNullable", parameter.IsNullable);
        record.Add("Default", parameter.Default);
        record.Add("Type", parameter.Type);
        record.Add("Format", parameter.Format);
        record.Add("Ref", parameter.Ref);
        record.Add("IsArray", parameter.IsArray);
        record.Add("ArrayItemsType", parameter.ArrayItemsType);
        record.Add("ArrayItemsRef", parameter.ArrayItemsRef);
        record.Add("EnumValues", parameter.EnumValues);
        record.Add("MinLength", parameter.MinLength);
        record.Add("MaxLength", parameter.MaxLength);
        record.Add("Minimum", parameter.Minimum);
        record.Add("Maximum", parameter.Maximum);
        record.Add("ExclusiveMinimum", parameter.ExclusiveMinimum);
        record.Add("ExclusiveMaximum", parameter.ExclusiveMaximum);
        record.Add("Pattern", parameter.Pattern);
        record.Add("MinItems", parameter.MinItems);
        record.Add("MaxItems", parameter.MaxItems);
        record.WriteTo(builder);
    }

    private static ParameterModel ReadParameter(Record record) => new() {
        MemberNameOverride = record.String("MemberNameOverride"),
        Name = record.String("Name") ?? "",
        In = record.String("In") ?? "",
        Description = record.String("Description"),
        IsRequired = record.Bool("IsRequired"),
        IsNullable = record.Bool("IsNullable"),
        Default = record.String("Default"),
        Type = record.String("Type"),
        Format = record.String("Format"),
        Ref = record.String("Ref"),
        IsArray = record.Bool("IsArray"),
        ArrayItemsType = record.String("ArrayItemsType"),
        ArrayItemsRef = record.String("ArrayItemsRef"),
        EnumValues = record.Strings("EnumValues"),
        MinLength = record.Int("MinLength"),
        MaxLength = record.Int("MaxLength"),
        Minimum = record.Decimal("Minimum"),
        Maximum = record.Decimal("Maximum"),
        ExclusiveMinimum = record.Bool("ExclusiveMinimum"),
        ExclusiveMaximum = record.Bool("ExclusiveMaximum"),
        Pattern = record.String("Pattern"),
        MinItems = record.Int("MinItems"),
        MaxItems = record.Int("MaxItems"),
    };

    private static void WriteFilterType(StringBuilder builder, FilterTypeModel filterType) {
        var record = new Record("filtertype");
        record.Add("Name", filterType.Name);
        record.Add("Namespace", filterType.Namespace);

        // Written unconditionally: it defaults to true, so omitting it when false would read back
        // as true and silently start generating a type the spec asked us not to.
        record.AddAlways("Generate", filterType.Generate ? "true" : "false");
        record.WriteTo(builder);

        foreach (var property in filterType.Properties) {
            var propertyRecord = new Record("filterprop");
            propertyRecord.Add("Name", property.Name);
            propertyRecord.Add("CSharpType", property.CSharpType);
            propertyRecord.Add("Default", property.Default);
            propertyRecord.Add("EnumType", property.EnumType);
            propertyRecord.Add("EnumValues", property.EnumValues);
            propertyRecord.WriteTo(builder);
        }
    }

    /// <summary>
    /// One line. Values are escaped on the way in and unescaped on the way out; a null value is
    /// simply not written, which is how null and empty stay distinguishable.
    /// </summary>
    private sealed class Record {
        private readonly List<string> _fields = new();
        private readonly Dictionary<string, string> _values;

        public Record(string tag) {
            Tag = tag;
            _values = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private Record(string tag, Dictionary<string, string> values) {
            Tag = tag;
            _values = values;
        }

        public string Tag { get; }

        public void Add(string key, string? value) {
            if (value is not null) {
                _fields.Add(key + "=" + Escape(value));
            }
        }

        public void AddAlways(string key, string value) => _fields.Add(key + "=" + Escape(value));

        public void Add(string key, bool value) {
            if (value) {
                _fields.Add(key + "=true");
            }
        }

        public void Add(string key, int? value) {
            if (value.HasValue) {
                _fields.Add(key + "=" + value.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        public void Add(string key, decimal? value) {
            if (value.HasValue) {
                _fields.Add(key + "=" + value.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        /// <summary>
        /// An empty list is written as an empty value, so a schema that declares no enum values and
        /// one that has none stay distinguishable - the first is null, the second is an empty list.
        /// </summary>
        public void Add(string key, List<string>? values) {
            if (values is not null) {
                _fields.Add(key + "=" + string.Join("", values.Select(Escape)));
            }
        }

        public void WriteTo(StringBuilder builder) {
            builder.Append(Tag);

            foreach (var field in _fields) {
                builder.Append(FieldSeparator).Append(field);
            }

            builder.Append('\n');
        }

        public static Record Parse(string line) {
            var parts = line.Split(FieldSeparator);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);

            for (var i = 1; i < parts.Length; i++) {
                var separator = parts[i].IndexOf('=');

                if (separator < 0) {
                    throw new FormatException($"Malformed field '{parts[i]}' in spec model record '{parts[0]}'.");
                }

                values[parts[i].Substring(0, separator)] = parts[i].Substring(separator + 1);
            }

            return new Record(parts[0], values);
        }

        public string? String(string key) => _values.TryGetValue(key, out var value) ? Unescape(value) : null;

        public bool Bool(string key) => _values.TryGetValue(key, out var value) && value == "true";

        public int? Int(string key) =>
            _values.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;

        public decimal? Decimal(string key) =>
            _values.TryGetValue(key, out var value) && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;

        public List<string>? Strings(string key) {
            if (!_values.TryGetValue(key, out var value)) {
                return null;
            }

            return value.Length == 0
                ? new List<string>()
                : value.Split('').Select(Unescape).ToList();
        }

        private static string Escape(string value) => value
            .Replace("\\", "\\\\")
            .Replace("\t", "\\t")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("", "\\u");

        private static string Unescape(string value) {
            if (value.IndexOf('\\') < 0) {
                return value;
            }

            var builder = new StringBuilder(value.Length);

            for (var i = 0; i < value.Length; i++) {
                if (value[i] != '\\' || i + 1 >= value.Length) {
                    builder.Append(value[i]);
                    continue;
                }

                i++;

                builder.Append(value[i] switch {
                    't' => '\t',
                    'n' => '\n',
                    'r' => '\r',
                    'u' => '',
                    _ => value[i],
                });
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// The response model a written record names, defaulting to Standard.
    /// </summary>
    /// <remarks>
    /// An unrecognised value is Standard rather than a throw, matching how the build task reads
    /// $(HardenedResponseModel): a model file written by a newer build should degrade to the shape
    /// every consumer already understands rather than fail the read.
    /// </remarks>
    private static SpecResponseModel ParseResponseModel(string? value) {
        if (string.Equals(value, nameof(SpecResponseModel.Response), StringComparison.OrdinalIgnoreCase)) {
            return SpecResponseModel.Response;
        }

        return string.Equals(value, nameof(SpecResponseModel.Union), StringComparison.OrdinalIgnoreCase)
            ? SpecResponseModel.Union
            : SpecResponseModel.Standard;
    }
}
