using System.Collections.Generic;
using System.Linq;
using System.Text;
using CSharpAuthor;
using Hardened.Idl.Models;
using Hardened.Idl;

namespace Hardened.OpenApi.SourceGenerator.Emitters;

internal static class JsonTypeInfoEmitter {

    /// <summary>
    /// The resolver's type name for a given spec.
    /// </summary>
    /// <remarks>
    /// Qualified by spec file, because it used to be a bare <c>OpenApiJsonTypeInfoResolver</c> in a
    /// namespace shared by every spec in the project - so two spec files in one project produced two
    /// classes of the same name and the project would not compile. That is finding 3.1 of
    /// OPENAPI-GENERATOR-FINDINGS.md, and it stops being a problem here rather than being fixed,
    /// because naming is now done in one place by the task and handed to the generator.
    /// </remarks>
    public static string ResolverNameFor(string specFileName) =>
        NamingHelper.ToPascalCase(specFileName) + "JsonTypeInfoResolver";

    public static ClassDefinition Emit(
        IConstructContainer container, List<SchemaModel> schemas, string modelsNamespace, string specFileName) {
        var resolverName = ResolverNameFor(specFileName);

        var resolver = container.AddClass(resolverName);

        resolver.Modifiers |= ComponentModifier.Public | ComponentModifier.Sealed;
        resolver.AddBaseType(
            TypeDefinition.Get("System.Text.Json.Serialization.Metadata", "IJsonTypeInfoResolver"));

        var instance = resolver.AddField(TypeDefinition.Get(modelsNamespace, resolverName), "Instance");
        instance.Modifiers |= ComponentModifier.Public | ComponentModifier.Static | ComponentModifier.Readonly;
        instance.InitializeValue = new CodeOutputComponent("new()") { Indented = false };

        // Declared rather than inferred. Usings are collected from the ITypeDefinitions a component
        // names, and these bodies are statements - so JsonMetadataServices, List<> and JsonElement
        // are invisible to that. They resolve today only because the records in the same file happen
        // to import the same namespaces, which is a coincidence and not something to depend on.
        resolver.AddUsingNamespace("System");
        resolver.AddUsingNamespace("System.Collections.Generic");
        resolver.AddUsingNamespace("System.Text.Json");
        resolver.AddUsingNamespace("System.Text.Json.Serialization");
        resolver.AddUsingNamespace("System.Text.Json.Serialization.Metadata");

        AddGetTypeInfo(resolver, schemas);

        foreach (var schema in schemas) {
            switch (schema.Kind) {
                case SchemaKind.Object:
                    AddObjectTypeInfo(resolver, schema, schemas);
                    break;
                case SchemaKind.Enum:
                    AddEnumTypeInfo(resolver, schema);
                    break;
            }
        }

        return resolver;
    }

    /// <summary>
    /// The method bodies here are object initialisers and lambdas - <c>JsonObjectInfoValues&lt;T&gt;
    /// { ObjectWithParameterizedConstructorCreator = static args =&gt; ... }</c> - which CSharpAuthor
    /// has no construct for. The type, its members and their signatures are built properly; the
    /// bodies are statements, the same arrangement OpenApiRoutingTableGenerator uses.
    /// </summary>
    private static void AddStatements(MethodDefinition method, StringBuilder body) {
        var lines = body.ToString().TrimEnd('\n').Split('\n');

        // Dedent by the shallowest line rather than trimming each one: these bodies are nested
        // object initialisers whose relative indentation is the only thing making them readable.
        // CSharpAuthor owns the base indent, so only the depth beyond it is carried over.
        var baseIndent = int.MaxValue;

        foreach (var line in lines) {
            if (line.Trim().Length == 0) {
                continue;
            }

            baseIndent = System.Math.Min(baseIndent, line.Length - line.TrimStart(' ').Length);
        }

        if (baseIndent == int.MaxValue) {
            baseIndent = 0;
        }

        foreach (var line in lines) {
            var text = line.Trim().Length == 0 ? "" : line.Substring(baseIndent);

            // Add rather than AddIndentedStatement: that appends a ";" per component, and these are
            // lines of one expression - an object initialiser spanning twenty of them - not one
            // statement each. The lines carry their own terminators.
            method.Add(new CodeOutputComponent(text) { Indented = true });
        }
    }

    private static void AddGetTypeInfo(ClassDefinition resolver, List<SchemaModel> schemas) {
        var method = resolver.AddMethod("GetTypeInfo");

        method.Modifiers |= ComponentModifier.Public;
        method.SetReturnType(
            TypeDefinition.Get("System.Text.Json.Serialization.Metadata", "JsonTypeInfo").MakeNullable());
        method.AddParameter(TypeDefinition.Get(typeof(System.Type)), "type");
        method.AddParameter(TypeDefinition.Get("System.Text.Json", "JsonSerializerOptions"), "options");

        var sb = new StringBuilder();

        foreach (var schema in schemas) {
            if (schema.Kind != SchemaKind.Object && schema.Kind != SchemaKind.Enum) continue;
            var typeName = NamingHelper.ToPascalCase(schema.Name);
            sb.AppendLine($"        if (type == typeof({typeName})) return Create{typeName}TypeInfo(options);");
        }

        EmitPrimitiveTypeEntries(sb);
        EmitCollectionTypeEntries(sb, schemas);

        sb.AppendLine("        return null;");

        AddStatements(method, sb);
    }

    private static void EmitPrimitiveTypeEntries(StringBuilder sb) {
        sb.AppendLine();
        sb.AppendLine("        // Primitive types");
        sb.AppendLine("        if (type == typeof(string)) return JsonMetadataServices.CreateValueInfo<string>(options, JsonMetadataServices.StringConverter);");
        sb.AppendLine("        if (type == typeof(bool)) return JsonMetadataServices.CreateValueInfo<bool>(options, JsonMetadataServices.BooleanConverter);");
        sb.AppendLine("        if (type == typeof(int)) return JsonMetadataServices.CreateValueInfo<int>(options, JsonMetadataServices.Int32Converter);");
        sb.AppendLine("        if (type == typeof(uint)) return JsonMetadataServices.CreateValueInfo<uint>(options, JsonMetadataServices.UInt32Converter);");
        sb.AppendLine("        if (type == typeof(long)) return JsonMetadataServices.CreateValueInfo<long>(options, JsonMetadataServices.Int64Converter);");
        sb.AppendLine("        if (type == typeof(float)) return JsonMetadataServices.CreateValueInfo<float>(options, JsonMetadataServices.SingleConverter);");
        sb.AppendLine("        if (type == typeof(double)) return JsonMetadataServices.CreateValueInfo<double>(options, JsonMetadataServices.DoubleConverter);");
        sb.AppendLine("        if (type == typeof(DateTime)) return JsonMetadataServices.CreateValueInfo<DateTime>(options, JsonMetadataServices.DateTimeConverter);");
        sb.AppendLine("        if (type == typeof(DateOnly)) return JsonMetadataServices.CreateValueInfo<DateOnly>(options, JsonMetadataServices.DateOnlyConverter);");
        sb.AppendLine("        if (type == typeof(byte[])) return JsonMetadataServices.CreateValueInfo<byte[]>(options, JsonMetadataServices.ByteArrayConverter);");
        sb.AppendLine("        if (type == typeof(JsonElement)) return JsonMetadataServices.CreateValueInfo<JsonElement>(options, JsonMetadataServices.JsonElementConverter);");
        sb.AppendLine();
        sb.AppendLine("        // Nullable value types");
        sb.AppendLine("        if (type == typeof(bool?)) return JsonMetadataServices.CreateValueInfo<bool?>(options, JsonMetadataServices.GetNullableConverter<bool>(options));");
        sb.AppendLine("        if (type == typeof(int?)) return JsonMetadataServices.CreateValueInfo<int?>(options, JsonMetadataServices.GetNullableConverter<int>(options));");
        sb.AppendLine("        if (type == typeof(uint?)) return JsonMetadataServices.CreateValueInfo<uint?>(options, JsonMetadataServices.GetNullableConverter<uint>(options));");
        sb.AppendLine("        if (type == typeof(long?)) return JsonMetadataServices.CreateValueInfo<long?>(options, JsonMetadataServices.GetNullableConverter<long>(options));");
        sb.AppendLine("        if (type == typeof(float?)) return JsonMetadataServices.CreateValueInfo<float?>(options, JsonMetadataServices.GetNullableConverter<float>(options));");
        sb.AppendLine("        if (type == typeof(double?)) return JsonMetadataServices.CreateValueInfo<double?>(options, JsonMetadataServices.GetNullableConverter<double>(options));");
        sb.AppendLine("        if (type == typeof(DateTime?)) return JsonMetadataServices.CreateValueInfo<DateTime?>(options, JsonMetadataServices.GetNullableConverter<DateTime>(options));");
        sb.AppendLine("        if (type == typeof(DateOnly?)) return JsonMetadataServices.CreateValueInfo<DateOnly?>(options, JsonMetadataServices.GetNullableConverter<DateOnly>(options));");
        sb.AppendLine("        if (type == typeof(JsonElement?)) return JsonMetadataServices.CreateValueInfo<JsonElement?>(options, JsonMetadataServices.GetNullableConverter<JsonElement>(options));");
    }

    private static void AddObjectTypeInfo(
        ClassDefinition resolver, SchemaModel schema, List<SchemaModel> allSchemas) {
        var typeName = NamingHelper.ToPascalCase(schema.Name);
        var method = CreateTypeInfoMethod(resolver, typeName);
        var sb = new StringBuilder();

        // The same split SchemaEmitter writes the record from. The constructor list drives both the
        // positional argument casts and the parameter positions, so it must be the constructor the
        // record actually has - a disagreement lands arguments in the wrong parameters at run time
        // rather than failing the build.
        var parameters = SchemaShape.Constructor(schema);

        sb.AppendLine($"        var typeInfo = JsonMetadataServices.CreateObjectInfo<{typeName}>(options, new JsonObjectInfoValues<{typeName}>");
        sb.AppendLine("        {");

        if (parameters.Count == 0) {
            sb.AppendLine($"            ObjectCreator = static () => new {typeName}(),");
        } else {
            // ObjectWithParameterizedConstructorCreator
            sb.AppendLine($"            ObjectWithParameterizedConstructorCreator = static args => new {typeName}(");
            for (var i = 0; i < parameters.Count; i++) {
                var prop = parameters[i];
                var castType = GetFullCSharpType(prop, allSchemas);
                var comma = i < parameters.Count - 1 ? "," : "),";
                sb.AppendLine($"                ({castType})args[{i}]{comma}");
            }
        }

        if (schema.Properties.Count > 0) {
            // Every property, including the readOnly ones the constructor does not take: they are
            // serialized, and the property list is what carries a getter for them.
            sb.AppendLine("            PropertyMetadataInitializer = _ => new JsonPropertyInfo[]");
            sb.AppendLine("            {");
            foreach (var prop in schema.Properties.OrderByDescending(p => p.IsRequired)) {
                EmitPropertyInfo(sb, prop, typeName, allSchemas);
            }
            sb.AppendLine("            },");
        }

        if (parameters.Count > 0) {
            // ConstructorParameterMetadataInitializer
            sb.AppendLine("            ConstructorParameterMetadataInitializer = static () => new JsonParameterInfoValues[]");
            sb.AppendLine("            {");
            for (var i = 0; i < parameters.Count; i++) {
                EmitParameterInfo(sb, parameters[i], i, allSchemas);
            }
            sb.AppendLine("            },");
        }

        sb.AppendLine("        });");

        EmitPolymorphism(sb, schema, allSchemas);

        sb.AppendLine("        return typeInfo;");

        AddStatements(method, sb);
    }

    /// <summary>
    /// The polymorphism metadata for the base of a hierarchy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set on the <c>JsonTypeInfo</c> after it is created rather than declared with it, because
    /// <c>JsonObjectInfoValues&lt;T&gt;</c> has no member for it - the property is on
    /// <c>JsonTypeInfo</c> itself and is settable from net7.0.
    /// </para>
    /// <para>
    /// Deliberately not <c>[JsonPolymorphic]</c> and <c>[JsonDerivedType]</c> on the records. Those
    /// are read by <c>DefaultJsonTypeInfoResolver</c>'s reflection path, which this resolver exists
    /// to avoid entirely - they would emit as documentation and change nothing.
    /// </para>
    /// <para>
    /// <c>FailSerialization</c> for an unrecognised runtime type: silently writing a base-shaped
    /// object for a derived one the spec never declared loses data at the point it is hardest to
    /// notice.
    /// </para>
    /// </remarks>
    private static void EmitPolymorphism(
        StringBuilder sb, SchemaModel schema, List<SchemaModel> allSchemas) {
        if (!schema.IsPolymorphicBase || schema.DiscriminatorMapping.Count == 0) {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("        typeInfo.PolymorphismOptions = new JsonPolymorphismOptions");
        sb.AppendLine("        {");
        sb.AppendLine(
            $"            TypeDiscriminatorPropertyName = \"{schema.DiscriminatorPropertyName}\",");
        sb.AppendLine(
            "            UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,");
        sb.AppendLine("            DerivedTypes =");
        sb.AppendLine("            {");

        foreach (var mapping in schema.DiscriminatorMapping) {
            var derivedName = NamingHelper.ToPascalCase(TypeMapper.GetRefName(mapping.Ref));

            sb.AppendLine(
                $"                new JsonDerivedType(typeof({derivedName}), \"{mapping.Value}\"),");
        }

        sb.AppendLine("            },");
        sb.AppendLine("        };");
    }

    /// <summary>
    /// One property's metadata.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two accessors are what enforce <c>readOnly</c> and <c>writeOnly</c>, because
    /// <c>System.Text.Json</c> skips a property with a null accessor in the corresponding direction:
    /// no getter means it is never serialized, no setter means it is never deserialized.
    /// </para>
    /// <para>
    /// <c>Setter</c> is null for every property, not only the <c>readOnly</c> ones - these records
    /// are immutable and populated through their constructor, so the constructor parameter list is
    /// what admits a value. Leaving a <c>readOnly</c> property out of that list is therefore already
    /// enough to make it undeserializable; the null setter is what stops it being written to
    /// afterwards.
    /// </para>
    /// <para>
    /// <c>writeOnly</c> is the mirror image and needs the explicit null: the property is a normal
    /// constructor parameter, so it deserializes, and dropping the getter is the only thing keeping
    /// it out of responses.
    /// </para>
    /// </remarks>
    private static void EmitPropertyInfo(StringBuilder sb, PropertyModel prop, string declaringTypeName,
        List<SchemaModel> allSchemas) {
        var genericType = GetPropertyInfoGenericType(prop, allSchemas);
        var propName = NamingHelper.ToPascalCase(prop.Name);

        var getter = prop.IsWriteOnly
            ? "null"
            : $"static obj => (({declaringTypeName})obj).{propName}";

        sb.AppendLine($"                JsonMetadataServices.CreatePropertyInfo<{genericType}>(options, new JsonPropertyInfoValues<{genericType}>");
        sb.AppendLine("                {");
        sb.AppendLine("                    IsProperty = true,");
        sb.AppendLine("                    IsPublic = true,");
        sb.AppendLine($"                    DeclaringType = typeof({declaringTypeName}),");
        sb.AppendLine($"                    PropertyName = \"{prop.Name}\",");
        sb.AppendLine($"                    Getter = {getter},");
        sb.AppendLine("                    Setter = null,");
        sb.AppendLine("                }),");
    }

    private static void EmitParameterInfo(StringBuilder sb, PropertyModel prop, int position,
        List<SchemaModel> allSchemas) {
        var paramType = GetPropertyInfoGenericType(prop, allSchemas);
        var propName = NamingHelper.ToPascalCase(prop.Name);

        var baseType = TypeMapper.MapPropertyToCSharpType(prop);
        var isValueType = IsValueType(baseType, prop, allSchemas);
        var defaultValue = isValueType && !prop.HasDefault ? $"default({paramType})" : "null";

        sb.AppendLine("                new()");
        sb.AppendLine("                {");
        sb.AppendLine($"                    Name = \"{prop.Name}\",");
        sb.AppendLine($"                    ParameterType = typeof({paramType}),");
        sb.AppendLine($"                    Position = {position},");
        sb.AppendLine($"                    HasDefaultValue = {(prop.HasDefault ? "true" : "false")},");
        sb.AppendLine($"                    DefaultValue = {defaultValue},");
        sb.AppendLine("                },");
    }

    private static void AddEnumTypeInfo(ClassDefinition resolver, SchemaModel schema) {
        var typeName = NamingHelper.ToPascalCase(schema.Name);
        var method = CreateTypeInfoMethod(resolver, typeName);
        var sb = new StringBuilder();

        sb.AppendLine($"        return JsonMetadataServices.CreateValueInfo<{typeName}>(options, JsonMetadataServices.GetEnumConverter<{typeName}>(options));");

        AddStatements(method, sb);
    }

    /// <summary>
    /// The <c>Create{Type}TypeInfo(JsonSerializerOptions options)</c> factory every schema gets, of
    /// whichever kind.
    /// </summary>
    private static MethodDefinition CreateTypeInfoMethod(ClassDefinition resolver, string typeName) {
        var method = resolver.AddMethod("Create" + typeName + "TypeInfo");

        method.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;
        method.SetReturnType(new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition,
            "System.Text.Json.Serialization.Metadata",
            "JsonTypeInfo",
            new[] { TypeDefinition.Get("", typeName) }));
        method.AddParameter(TypeDefinition.Get("System.Text.Json", "JsonSerializerOptions"), "options");

        return method;
    }

    /// <summary>
    /// Returns the full C# type including nullable suffix for optional properties.
    /// Used for constructor cast expressions.
    /// </summary>
    private static string GetFullCSharpType(PropertyModel prop, List<SchemaModel> allSchemas) {
        var baseType = TypeMapper.MapPropertyToCSharpType(prop);
        if (prop.IsCSharpNullable) {
            return baseType + "?";
        }
        return baseType;
    }

    /// <summary>
    /// Returns the C# type for CreatePropertyInfo&lt;T&gt; and typeof() expressions.
    /// Reference types: always non-nullable (string, not string?).
    /// Value types: nullable if optional (int? for optional int).
    /// </summary>
    private static string GetPropertyInfoGenericType(PropertyModel prop, List<SchemaModel> allSchemas) {
        var baseType = TypeMapper.MapPropertyToCSharpType(prop);
        if (prop.IsCSharpNullable && IsValueType(baseType, prop, allSchemas)) {
            return baseType + "?";
        }
        return baseType;
    }

    private static void EmitCollectionTypeEntries(StringBuilder sb, List<SchemaModel> schemas) {
        var listTypes = new HashSet<string>();
        var dictTypes = new HashSet<string>();

        foreach (var schema in schemas) {
            if (schema.Properties == null) continue;
            foreach (var prop in schema.Properties) {
                if (prop.IsArray) {
                    listTypes.Add(GetArrayElementType(prop));
                }
                if (prop.IsDictionary) {
                    dictTypes.Add(GetDictionaryValueType(prop));
                }
            }
        }

        if (listTypes.Count == 0 && dictTypes.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine("        // Collection types");
        foreach (var elementType in listTypes) {
            sb.AppendLine($"        if (type == typeof(List<{elementType}>)) return JsonMetadataServices.CreateListInfo<List<{elementType}>, {elementType}>(options, new JsonCollectionInfoValues<List<{elementType}>> {{ ObjectCreator = static () => new List<{elementType}>() }});");
        }
        foreach (var valueType in dictTypes) {
            sb.AppendLine($"        if (type == typeof(Dictionary<string, {valueType}>)) return JsonMetadataServices.CreateDictionaryInfo<Dictionary<string, {valueType}>, string, {valueType}>(options, new JsonCollectionInfoValues<Dictionary<string, {valueType}>> {{ ObjectCreator = static () => new Dictionary<string, {valueType}>() }});");
        }
    }

    private static string GetArrayElementType(PropertyModel prop) {
        if (prop.ArrayItemsRef != null) {
            return NamingHelper.ToPascalCase(TypeMapper.GetRefName(prop.ArrayItemsRef));
        }
        return TypeMapper.MapToCSharpType(prop.ArrayItemsType, prop.ArrayItemsFormat);
    }

    private static string GetDictionaryValueType(PropertyModel prop) {
        if (prop.DictionaryValueRef != null) {
            return NamingHelper.ToPascalCase(TypeMapper.GetRefName(prop.DictionaryValueRef));
        }
        return TypeMapper.MapToCSharpType(prop.DictionaryValueType, null);
    }

    private static bool IsValueType(string csType, PropertyModel prop, List<SchemaModel> allSchemas) {
        switch (csType) {
            case "int":
            case "uint":
            case "long":
            case "float":
            case "double":
            case "bool":
            case "DateTime":
            case "DateOnly":
            case "JsonElement":
                return true;
        }

        // Check if it's an enum reference
        if (prop.Ref != null) {
            var refName = TypeMapper.GetRefName(prop.Ref);
            var refSchema = allSchemas.FirstOrDefault(s =>
                string.Equals(s.Name, refName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NamingHelper.ToPascalCase(s.Name), NamingHelper.ToPascalCase(refName),
                    StringComparison.OrdinalIgnoreCase));
            if (refSchema != null && refSchema.Kind == SchemaKind.Enum) {
                return true;
            }
        }

        return false;
    }
}
