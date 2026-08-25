using System.Collections.Generic;
using System.Linq;
using System.Text;
using CSharpAuthor;
using Hardened.Generation.Models;
using Hardened.Idl;
using Hardened.Generation;

namespace Hardened.Idl.Emitters;

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

        if (AnyRequiresPresenceCheck(schemas)) {
            AddRequireHelper(resolver);
        }

        AddStringConverters(resolver, schemas, modelsNamespace);

        AddGetTypeInfo(resolver, schemas, modelsNamespace);

        foreach (var schema in schemas) {
            switch (schema.Kind) {
                case SchemaKind.Object:
                    AddObjectTypeInfo(resolver, schema, schemas, modelsNamespace);
                    break;
                case SchemaKind.Enum:
                    AddEnumTypeInfo(resolver, schema, modelsNamespace);
                    break;
            }
        }

        return resolver;
    }

    /// <summary>
    /// The method bodies here are object initialisers and lambdas - <c>JsonObjectInfoValues&lt;T&gt;
    /// { ObjectWithParameterizedConstructorCreator = static args =&gt; ... }</c> - which CSharpAuthor
    /// has no construct for. The type, its members and their signatures are built properly; the
    /// bodies are statements, the same arrangement SpecRoutingTableGenerator uses.
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

    private static void AddGetTypeInfo(
        ClassDefinition resolver, List<SchemaModel> schemas, string modelsNamespace) {
        var method = resolver.AddMethod("GetTypeInfo");

        method.Modifiers |= ComponentModifier.Public;
        method.SetReturnType(
            TypeDefinition.Get("System.Text.Json.Serialization.Metadata", "JsonTypeInfo").MakeNullable());
        method.AddParameter(TypeDefinition.Get(typeof(System.Type)), "type");
        method.AddParameter(TypeDefinition.Get("System.Text.Json", "JsonSerializerOptions"), "options");

        var sb = new StringBuilder();

        foreach (var schema in schemas) {
            var name = NamingHelper.ToPascalCase(schema.Name);
            var typeName = TypeMapper.QualifiedName(modelsNamespace, name, false);

            if (schema.Kind == SchemaKind.OneOf) {
                var converter = TypeMapper.QualifiedName(
                    modelsNamespace, OneOfEmitter.ConverterName(schema.Name), false);

                // A value type like the generated enums, so both forms are answered: the nullable
                // one is what an optional payload lands on, and GetNullableConverter finds the
                // converter through the entry above.
                sb.AppendLine(
                    $"        if (type == typeof({typeName})) return JsonMetadataServices.CreateValueInfo<{typeName}>(options, {converter}.Instance);");
                sb.AppendLine(
                    $"        if (type == typeof({typeName}?)) return JsonMetadataServices.CreateValueInfo<{typeName}?>(options, JsonMetadataServices.GetNullableConverter<{typeName}>(options));");
                continue;
            }

            if (schema.Kind != SchemaKind.Object && schema.Kind != SchemaKind.Enum) continue;

            sb.AppendLine($"        if (type == typeof({typeName})) return Create{name}TypeInfo(options);");

            // A generated enum is a value type, so an optional property of one is T? - and the
            // property info asks the resolver for exactly that. Without this line it answers null
            // and the payload cannot be read at all: every optional enum property in every
            // description, which compiled, passed every test, and refused 339 of Square's 453
            // published examples the first time any of them was replayed.
            if (schema.Kind == SchemaKind.Enum) {
                sb.AppendLine(
                    $"        if (type == typeof({typeName}?)) return JsonMetadataServices.CreateValueInfo<{typeName}?>(options, JsonMetadataServices.GetNullableConverter<{typeName}>(options));");
            }
        }

        EmitCollectionTypeEntries(sb, schemas, modelsNamespace);

        // No primitive entries. The BCL leaf types every property info bottoms out in - string, int,
        // DateTimeOffset, their nullable forms - are answered once by
        // Hardened.Shared.Runtime.Json.PrimitiveJsonTypeInfoResolver, which the serializers put at
        // the end of the chain. They are still required (a chain that cannot answer typeof(string)
        // throws on every string property); they were simply emitted once per specification file
        // into a chain that already holds one resolver per specification file, and each copy pinned
        // JsonMetadataServices.StringConverter and friends in a way that silently discarded a
        // converter the application had registered.
        sb.AppendLine("        return null;");

        AddStatements(method, sb);
    }

    private static void AddObjectTypeInfo(
        ClassDefinition resolver, SchemaModel schema, List<SchemaModel> allSchemas, string ns) {
        // The bare name identifies the method; the qualified one is what a type reference needs.
        var name = NamingHelper.ToPascalCase(schema.Name);
        var typeName = TypeMapper.QualifiedName(ns, name, false);
        var method = CreateTypeInfoMethod(resolver, name);
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
                var castType = GetFullCSharpType(prop, allSchemas, ns);
                var comma = i < parameters.Count - 1 ? "," : "),";
                sb.AppendLine($"                ({castType})args[{i}]{comma}");
            }
        }

        // Always, even for a schema that declares none. System.Text.Json requires the initializer
        // to be present and refuses the type outright without it - "did not provide property
        // metadata" - so a free-form `type: object` could not be deserialized at all. Cloudflare
        // declares 57 of those, and every one of them threw.
        sb.AppendLine("            PropertyMetadataInitializer = _ => new JsonPropertyInfo[]");
        sb.AppendLine("            {");

        foreach (var prop in schema.Properties.OrderByDescending(p => !p.HasDefault)) {
            // A member bound to a response header leaves as a header, so it has no place in the
            // body's metadata. The constructor parameter below stays - the positions have to match
            // the constructor the schema emitter wrote, and the handler still sets the member.
            if (prop.IsHeaderBound) {
                continue;
            }

            EmitPropertyInfo(sb, prop, typeName, allSchemas, ns);
        }

        sb.AppendLine("            },");

        if (parameters.Count > 0) {
            // ConstructorParameterMetadataInitializer
            sb.AppendLine("            ConstructorParameterMetadataInitializer = static () => new JsonParameterInfoValues[]");
            sb.AppendLine("            {");
            for (var i = 0; i < parameters.Count; i++) {
                EmitParameterInfo(sb, parameters[i], i, allSchemas, ns);
            }
            sb.AppendLine("            },");
        }

        sb.AppendLine("        });");

        EmitPolymorphism(sb, schema, allSchemas, ns);

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
        StringBuilder sb, SchemaModel schema, List<SchemaModel> allSchemas, string ns) {
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
            var derivedName = TypeMapper.QualifiedName(
                ns, NamingHelper.ToPascalCase(TypeMapper.GetRefName(mapping.Ref)), false);

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
        List<SchemaModel> allSchemas, string ns) {
        var genericType = GetPropertyInfoGenericType(prop, allSchemas, ns);
        var propName = prop.MemberName;

        // Both directions read and write. A writeOnly property used to have no getter, which made
        // the request body it describes unserializable; see ResponseOnlyAttribute.
        var getter = $"static obj => (({declaringTypeName})obj).{propName}";

        var required = RequiresPresenceCheck(prop, allSchemas);

        var open = required ? RequireMethodName + "(" : "";
        var close = required ? ")," : ",";

        sb.AppendLine($"                {open}JsonMetadataServices.CreatePropertyInfo<{genericType}>(options, new JsonPropertyInfoValues<{genericType}>");
        sb.AppendLine("                {");
        sb.AppendLine("                    IsProperty = true,");
        sb.AppendLine("                    IsPublic = true,");
        sb.AppendLine($"                    DeclaringType = typeof({declaringTypeName}),");
        sb.AppendLine($"                    PropertyName = \"{prop.Name}\",");
        sb.AppendLine($"                    Getter = {getter},");

        // A setter that does nothing, and it has to exist: System.Text.Json refuses IsRequired on a
        // property with a null setter - "marked required but does not specify a setter" - and these
        // records are populated through their constructor, whose init-only members cannot be
        // assigned through a delegate. The constructor still supplies the value; the setter is never
        // the thing that fills it.
        sb.AppendLine(required
            ? $"                    Setter = static (object obj, {genericType} value) => {{ }},"
            : "                    Setter = null,");

        sb.AppendLine($"                }}){close}");
    }

    /// <summary>
    /// Whether absence of this member has to be caught by the deserializer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The exact complement of where <c>[Required]</c> is emitted.</b> A required member of a
    /// reference type carries <c>[Required]</c> and is checked by the generated validator, which
    /// aggregates its error with every other failed constraint in the same body. A required member
    /// of a <em>value</em> type carries nothing: the validation generator answers
    /// <c>value.x is null</c> against an <c>int</c>, which is CS0037, so the constraint is
    /// suppressed - correctly - and nothing takes its place.
    /// </para>
    /// <para>
    /// The result was that a missing required value silently became <c>default(T)</c>. An omitted
    /// enum became its first declared member and the API answered 201 with a value the caller never
    /// sent; an omitted integer became 0, caught only where some unrelated constraint happened to
    /// reject 0. This is what closes that, and it is deliberately narrow: reference types keep the
    /// aggregating validator path they already had.
    /// </para>
    /// <para>
    /// <b>A declared <c>default</c> does not exempt it.</b> <c>required</c> and <c>default</c> are
    /// contradictory - one says the caller must send the member, the other names what absence means
    /// - and the contract's <c>required</c> wins. It is not a near miss either: a required member's
    /// generated parameter carries no <c>= default</c>, so the specification's default is inert
    /// today. Exempting these would preserve the silent zero for the one shape where the document
    /// most obviously disagrees with itself.
    /// </para>
    /// </remarks>
    private static bool RequiresPresenceCheck(PropertyModel prop, List<SchemaModel> allSchemas) {
        if (!prop.ConstrainedAsRequired) {
            return false;
        }

        return TypeMapper.IsNonNullableValueType(
            TypeMapper.MapPropertyToCSharpType(prop), allSchemas);
    }

    private const string RequireMethodName = "Required";

    /// <summary>
    /// Marks a property the deserializer must see, as a method because <c>IsRequired</c> is set on
    /// the built <c>JsonPropertyInfo</c> rather than declared in <c>JsonPropertyInfoValues</c>.
    /// </summary>
    private static void AddRequireHelper(ClassDefinition resolver) {
        var method = resolver.AddMethod(RequireMethodName);

        method.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;
        method.SetReturnType(
            TypeDefinition.Get("System.Text.Json.Serialization.Metadata", "JsonPropertyInfo"));
        method.AddParameter(
            TypeDefinition.Get("System.Text.Json.Serialization.Metadata", "JsonPropertyInfo"),
            "property");

        method.AddIndentedStatement(new CodeOutputComponent("property.IsRequired = true") );
        method.AddIndentedStatement(new CodeOutputComponent("return property"));
    }

    /// <summary>Whether anything in this specification needs the helper above.</summary>
    private static bool AnyRequiresPresenceCheck(List<SchemaModel> schemas) =>
        schemas.Any(schema => schema.Kind == SchemaKind.Object &&
                              schema.Properties.Any(prop => RequiresPresenceCheck(prop, schemas)));

    private static void EmitParameterInfo(StringBuilder sb, PropertyModel prop, int position,
        List<SchemaModel> allSchemas, string ns) {
        var paramType = GetPropertyInfoGenericType(prop, allSchemas, ns);
        var propName = prop.MemberName;

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

    /// <summary>
    /// Every generated enum converter, as the parameter binder consumes them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The converters are already emitted beside their enums and named by the resolver; this is the
    /// list of them, so the routing table can register each as an <c>IStringConverter</c> without
    /// knowing which enums a description declared.
    /// </para>
    /// <para>
    /// A path or query parameter is text rather than JSON, so it never went through these - the
    /// binder called <c>Enum.Parse</c> on the C# member name instead, and any declared value that is
    /// not already a valid identifier was unreachable as a parameter.
    /// </para>
    /// </remarks>
    private static void AddStringConverters(
        ClassDefinition resolver, List<SchemaModel> schemas, string ns) {
        var converters = new List<string>();

        foreach (var schema in schemas) {
            if (schema.Kind != SchemaKind.Enum) {
                continue;
            }

            var enumType = TypeMapper.QualifiedName(
                ns, NamingHelper.ToPascalCase(schema.Name), false);
            var converterType = TypeMapper.QualifiedName(
                ns, EnumConverterEmitter.ConverterName(schema.Name), false);

            // The document's own name for the type, not the C# one, because the message it produces
            // is read by whoever wrote the request against the document.
            converters.Add(
                $"new global::Hardened.Requests.Abstract.Serializer.DelegatingStringConverter<{enumType}>(" +
                $"{converterType}.TryParseWire, \"{schema.Name}\")");
        }

        var field = resolver.AddField(
            TypeDefinition.Get("Hardened.Requests.Abstract.Serializer", "IStringConverter").MakeArray(),
            "StringConverters");

        field.Modifiers |=
            ComponentModifier.Public | ComponentModifier.Static | ComponentModifier.Readonly;
        field.InitializeValue = new CodeOutputComponent(
            converters.Count == 0
                ? "global::System.Array.Empty<global::Hardened.Requests.Abstract.Serializer.IStringConverter>()"
                : "{ " + string.Join(", ", converters) + " }") { Indented = false };
    }

    private static void AddEnumTypeInfo(ClassDefinition resolver, SchemaModel schema, string ns) {
        var name = NamingHelper.ToPascalCase(schema.Name);
        var typeName = TypeMapper.QualifiedName(ns, name, false);
        var method = CreateTypeInfoMethod(resolver, name);
        var sb = new StringBuilder();

        // Not GetEnumConverter: that is the numeric one, and an OpenAPI enum travels as the string
        // the description declares. See EnumConverterEmitter.
        var converter = TypeMapper.QualifiedName(
            ns, EnumConverterEmitter.ConverterName(schema.Name), false);

        sb.AppendLine($"        return JsonMetadataServices.CreateValueInfo<{typeName}>(options, {converter}.Instance);");

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
    private static string GetFullCSharpType(
        PropertyModel prop, List<SchemaModel> allSchemas, string ns) {
        var baseType = TypeMapper.MapPropertyToCSharpType(prop);

        // Qualified for the same reason the property infos are: a cast is a type reference, and an
        // unqualified one binds to whatever the consumer has in scope.
        var qualified = TypeMapper.QualifiedName(ns, baseType, false);

        return prop.IsCSharpNullable ? qualified + "?" : qualified;
    }

    /// <summary>
    /// Returns the C# type for CreatePropertyInfo&lt;T&gt; and typeof() expressions.
    /// Reference types: always non-nullable (string, not string?).
    /// Value types: nullable if optional (int? for optional int).
    /// </summary>
    private static string GetPropertyInfoGenericType(
        PropertyModel prop, List<SchemaModel> allSchemas, string ns) {
        var baseType = TypeMapper.MapPropertyToCSharpType(prop);

        // Qualified, because this emitter writes text rather than going through the output
        // context - see TypeMapper.QualifiedName.
        return TypeMapper.QualifiedName(
            ns, baseType, prop.IsCSharpNullable && IsValueType(baseType, prop, allSchemas));
    }

    private static void EmitCollectionTypeEntries(
        StringBuilder sb, List<SchemaModel> schemas, string modelsNamespace) {
        var listTypes = new HashSet<string>();
        var dictTypes = new HashSet<string>();

        foreach (var schema in schemas) {
            if (schema.Properties == null) continue;
            foreach (var prop in schema.Properties) {
                if (prop.IsArray) {
                    listTypes.Add(GetArrayElementType(prop, modelsNamespace));
                }
                if (prop.IsDictionary) {
                    dictTypes.Add(GetDictionaryValueType(prop, modelsNamespace));
                }
            }
        }

        if (listTypes.Count == 0 && dictTypes.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine("        // Collection types");
        foreach (var elementType in listTypes) {
            sb.AppendLine($"        if (type == typeof(global::System.Collections.Generic.List<{elementType}>)) return JsonMetadataServices.CreateListInfo<global::System.Collections.Generic.List<{elementType}>, {elementType}>(options, new JsonCollectionInfoValues<global::System.Collections.Generic.List<{elementType}>> {{ ObjectCreator = static () => new global::System.Collections.Generic.List<{elementType}>() }});");
        }
        foreach (var valueType in dictTypes) {
            sb.AppendLine($"        if (type == typeof(global::System.Collections.Generic.Dictionary<string,{valueType}>)) return JsonMetadataServices.CreateDictionaryInfo<global::System.Collections.Generic.Dictionary<string,{valueType}>, string, {valueType}>(options, new JsonCollectionInfoValues<global::System.Collections.Generic.Dictionary<string,{valueType}>> {{ ObjectCreator = static () => new global::System.Collections.Generic.Dictionary<string,{valueType}>() }});");
        }
    }

    /// <remarks>
    /// Qualified, because the element type is written into the collection table as text. The entry
    /// has to name the same type the property info asks for, and the property info is qualified.
    /// </remarks>
    private static string GetArrayElementType(PropertyModel prop, string ns) {
        if (prop.ArrayItemsRef != null) {
            return TypeMapper.QualifiedName(
                ns, NamingHelper.ToPascalCase(TypeMapper.GetRefName(prop.ArrayItemsRef)), false);
        }

        return TypeMapper.QualifiedName(
            ns, TypeMapper.MapToCSharpType(prop.ArrayItemsType, prop.ArrayItemsFormat), false);
    }

    private static string GetDictionaryValueType(PropertyModel prop, string ns) {
        if (prop.DictionaryValueRef != null) {
            return TypeMapper.QualifiedName(
                ns, NamingHelper.ToPascalCase(TypeMapper.GetRefName(prop.DictionaryValueRef)), false);
        }

        return TypeMapper.QualifiedName(
            ns, TypeMapper.MapToCSharpType(prop.DictionaryValueType, null), false);
    }

    private static bool IsValueType(string csType, PropertyModel prop, List<SchemaModel> allSchemas) {
        switch (csType) {
            case "int":
            case "uint":
            case "long":
            case "float":
            case "double":
            case "bool":
            case "DateTimeOffset":
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
            // A choice type is a struct for the same reason a generated enum is - see
            // TypeMapper.IsGeneratedChoice - so an optional one is T? here too.
            if (refSchema != null &&
                (refSchema.Kind == SchemaKind.Enum || refSchema.Kind == SchemaKind.OneOf)) {
                return true;
            }
        }

        return false;
    }
}
