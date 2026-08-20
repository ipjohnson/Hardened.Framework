using System.Collections.Generic;
using CSharpAuthor;
using Hardened.Idl;
using Hardened.Idl.Models;

namespace Hardened.Idl.Emitters;

/// <summary>
/// The converter that decides which branch a payload is, and reads it into that type.
/// </summary>
/// <remarks>
/// <para>
/// This is the part that makes the wrapper worth having. The document says a payload is one of
/// several schemas; something has to decide which, and doing it here means it is decided once, while
/// reading, rather than by every caller afterwards.
/// </para>
/// <para>
/// A discriminator is the document saying which branch a payload is, so it is what this reads when
/// there is one - <c>mapping</c> where declared, otherwise the schema's own name, which is what the
/// specification says a bare discriminator implies. Without one the only thing left is the payload's
/// shape, and that is a guess: two branches whose properties are all optional both match, and
/// binding the first would be silently wrong. Hence off unless the file asks for it.
/// </para>
/// <para>
/// Every read and write goes through a <c>JsonTypeInfo</c> taken from the options rather than the
/// reflection overloads, so the whole path stays valid under trimming and AOT - which is the point
/// of generating a resolver at all.
/// </para>
/// </remarks>
internal static class OneOfConverterEmitter {

    private static readonly ITypeDefinition Reader =
        TypeDefinition.Get("System.Text.Json", "Utf8JsonReader");

    private static readonly ITypeDefinition Writer =
        TypeDefinition.Get("System.Text.Json", "Utf8JsonWriter");

    /// <summary>The helpers' type parameter, which is a name rather than a type.</summary>
    private static readonly ITypeDefinition Generic = TypeDefinition.Get("", "T");

    private static readonly ITypeDefinition Options =
        TypeDefinition.Get("System.Text.Json", "JsonSerializerOptions");

    public static ClassDefinition Emit(
        IConstructContainer container, SchemaModel schema, string modelsNamespace,
        IReadOnlyList<SchemaModel> allSchemas) {
        var name = NamingHelper.ToPascalCase(schema.Name);
        var converterName = OneOfEmitter.ConverterName(schema.Name);
        var wrapper = TypeDefinition.Get(modelsNamespace, name);
        var branches = OneOfEmitter.Branches(schema, modelsNamespace);

        var converter = container.AddClass(converterName);

        converter.Modifiers |= ComponentModifier.Public | ComponentModifier.Sealed;
        converter.AddBaseType(
            new GenericTypeDefinition(
                TypeDefinitionEnum.ClassDefinition,
                "System.Text.Json.Serialization",
                "JsonConverter",
                new[] { wrapper }));

        converter.Comment = $"Reads and writes {name}, resolving which type a payload is.";

        var instance = converter.AddField(
            TypeDefinition.Get(modelsNamespace, converterName), "Instance");

        instance.Modifiers |=
            ComponentModifier.Public | ComponentModifier.Static | ComponentModifier.Readonly;
        instance.InitializeValue = new CodeOutputComponent("new()") { Indented = false };

        EmitRead(converter, schema, wrapper, branches, modelsNamespace, allSchemas);
        EmitWrite(converter, schema, wrapper, branches, modelsNamespace);
        EmitHelpers(converter);

        return converter;
    }

    private static void EmitRead(
        ClassDefinition converter, SchemaModel schema, ITypeDefinition wrapper,
        List<string> branches, string modelsNamespace, IReadOnlyList<SchemaModel> allSchemas) {
        var method = converter.AddMethod("Read");

        method.Modifiers |= ComponentModifier.Public | ComponentModifier.Override;
        method.SetReturnType(wrapper);
        method.AddParameter(Reader, "reader").Modifier = ParameterModifier.Ref;
        method.AddParameter(TypeDefinition.Get(typeof(System.Type)), "typeToConvert");
        method.AddParameter(Options, "options");

        var lines = new List<string> {
            // Buffered, because deciding the branch means looking at the payload before reading it,
            // and a reader cannot be rewound.
            "using var document = global::System.Text.Json.JsonDocument.ParseValue(ref reader);",
            "var element = document.RootElement;",
            ""
        };

        if (schema.DiscriminatorPropertyName != null && schema.DiscriminatorMapping.Count > 0) {
            Discriminated(lines, schema, modelsNamespace);
        } else {
            ShapeMatched(lines, schema, modelsNamespace, allSchemas);
        }

        Write(method, lines);
    }

    private static void Discriminated(
        List<string> lines, SchemaModel schema, string modelsNamespace) {
        var property = Escape(schema.DiscriminatorPropertyName!);

        lines.Add($"if (!element.TryGetProperty(\"{property}\", out var discriminator))");
        lines.Add("{");
        lines.Add("    throw new global::System.Text.Json.JsonException(");
        lines.Add($"        \"Expected a '{property}' property to say which type this is.\");");
        lines.Add("}");
        lines.Add("");
        lines.Add("var kind = discriminator.GetString();");
        lines.Add("");
        lines.Add("return kind switch");
        lines.Add("{");

        foreach (var mapping in schema.DiscriminatorMapping) {
            var branch = TypeMapper.QualifiedName(
                modelsNamespace, NamingHelper.ToPascalCase(TypeMapper.GetRefName(mapping.Ref)), false);

            lines.Add($"    \"{Escape(mapping.Value)}\" => new(Read<{branch}>(element, options)),");
        }

        lines.Add("    _ => throw new global::System.Text.Json.JsonException(");
        lines.Add($"        \"'\" + kind + \"' is not a type '{property}' may name.\")");
        lines.Add("};");
    }

    /// <summary>
    /// The test <see cref="ChoiceResolution"/> worked out for each branch, strongest evidence first.
    /// </summary>
    /// <remarks>
    /// Not a try-each-in-turn: two branches whose properties are all optional both read, so the
    /// first would win and the payload would silently become the wrong type. Every test emitted
    /// here was proved to hold for exactly one branch before any of this was written - which is
    /// also why there is no case for "matched several".
    /// </remarks>
    /// <summary>
    /// The test <see cref="ChoiceResolution"/> proved for each branch, and match counting for any
    /// branch it could not prove.
    /// </summary>
    /// <remarks>
    /// Never a first-one-that-reads: that is what <c>serde(untagged)</c> and Pydantic's default do,
    /// and it binds the wrong type without saying so. Where the schemas prove a branch apart the
    /// test is exact and costs one comparison. Where they do not, every candidate is tried and
    /// exactly one has to match - the same answer openapi-generator gives, and reached only for the
    /// branches that needed it.
    /// </remarks>
    private static void ShapeMatched(
        List<string> lines, SchemaModel schema, string modelsNamespace,
        IReadOnlyList<SchemaModel> allSchemas) {
        var plan = ChoiceResolution.Resolve(schema.OneOf, allSchemas);

        var tag = 0;
        var fallback = (ChoiceResolution.Branch?)null;

        foreach (var branch in plan.Branches) {
            if (!branch.Proved) {
                continue;
            }

            // Accepts everything a narrower branch of its kind does, so a test for it would claim
            // that branch's payloads too. Emitted last, after those have had their turn.
            if (branch.IsWiderFallback) {
                fallback = branch;
                continue;
            }

            var type = TypeMapper.QualifiedName(
                modelsNamespace, ChoiceResolution.CSharpType(branch.Model), false);

            if (branch.ValueKind != null) {
                // Boolean is two kinds in System.Text.Json and one type here.
                var test = branch.ValueKind == "Boolean"
                    ? "element.ValueKind is global::System.Text.Json.JsonValueKind.True or " +
                      "global::System.Text.Json.JsonValueKind.False"
                    : $"element.ValueKind == global::System.Text.Json.JsonValueKind.{branch.ValueKind}";

                lines.Add($"if ({test})");
            } else if (branch.ValueSet != null) {
                // A membership test rather than a trial read: the values are known here, so this
                // decides the branch outright instead of leaning on a failed parse.
                var values = new List<string>();

                foreach (var value in branch.ValueSet) {
                    values.Add($"\"{Escape(value)}\"");
                }

                lines.Add($"if (element.GetString() is {string.Join(" or ", values)})");
            } else if (branch.ConstProperty != null) {
                // Numbered, because several branches in one method each declare one and C# scopes
                // an out variable to the whole method body rather than to its if.
                var name = "tag" + tag++.ToString(System.Globalization.CultureInfo.InvariantCulture);

                lines.Add(
                    $"if (element.TryGetProperty(\"{Escape(branch.ConstProperty)}\", out var {name}) && " +
                    $"{name}.ValueEquals(\"{Escape(branch.ConstValue!)}\"))");
            } else {
                lines.Add(
                    $"if (element.TryGetProperty(\"{Escape(branch.DistinctProperty!)}\", out _))");
            }

            lines.Add("{");
            lines.Add($"    return new(Read<{type}>(element, options));");
            lines.Add("}");
            lines.Add("");
        }

        if (fallback != null) {
            var type = TypeMapper.QualifiedName(
                modelsNamespace, ChoiceResolution.CSharpType(fallback.Model), false);

            lines.Add($"return new(Read<{type}>(element, options));");

            return;
        }

        if (plan.Overlapping.Count == 0) {
            lines.Add("throw new global::System.Text.Json.JsonException(");
            lines.Add(
                $"    \"The payload matched none of the {plan.Branches.Count} permitted types.\");");

            return;
        }

        // The branches nothing separates on paper. A payload usually settles it - two schemas whose
        // properties are all optional overlap until one arrives carrying a property only one of them
        // declares - so each is tried and the count decides.
        //
        // Accumulated as the wrapper rather than as object, and wrapped inside the try where the
        // branch type is still known. `new(matched!)` over an object needs a constructor taking one,
        // which is the shape this type is moving away from: one constructor per branch says the same
        // thing to the compiler instead of to a run-time check, and is what the language declares
        // for a union. Read<T> throws rather than returning null, so a caught branch never stored
        // one, and `matches` is what says whether `matched` was ever assigned.
        var wrapper = TypeMapper.QualifiedName(
            modelsNamespace, NamingHelper.ToPascalCase(schema.Name), false);

        lines.Add($"{wrapper} matched = default;");
        lines.Add("var matches = 0;");
        lines.Add("");

        foreach (var branch in plan.Overlapping) {
            var type = TypeMapper.QualifiedName(
                modelsNamespace, ChoiceResolution.CSharpType(branch.Model), false);

            lines.Add("try");
            lines.Add("{");
            lines.Add($"    matched = new(Read<{type}>(element, options));");
            lines.Add("    matches++;");
            lines.Add("}");
            lines.Add("catch (global::System.Text.Json.JsonException)");
            lines.Add("{");
            lines.Add("}");
            lines.Add("");
        }

        lines.Add("if (matches == 1)");
        lines.Add("{");
        lines.Add("    return matched;");
        lines.Add("}");
        lines.Add("");

        // Both failures are worth telling apart: nothing matched is a payload the schema does not
        // describe, and several matched is a payload the schema describes two ways.
        lines.Add("throw new global::System.Text.Json.JsonException(");
        lines.Add("    matches == 0");
        lines.Add(
            $"        ? \"The payload matched none of the {plan.Branches.Count} permitted types.\"");
        lines.Add(
            "        : \"The payload matched \" + matches + \" permitted types at once, so which " +
            "one it is cannot be decided.\");");
    }

    private static void EmitWrite(
        ClassDefinition converter, SchemaModel schema, ITypeDefinition wrapper,
        List<string> branches, string modelsNamespace) {
        var method = converter.AddMethod("Write");

        method.Modifiers |= ComponentModifier.Public | ComponentModifier.Override;
        method.AddParameter(Writer, "writer");
        method.AddParameter(wrapper, "value");
        method.AddParameter(Options, "options");

        var discriminator = Discriminators(schema, modelsNamespace);
        var lines = new List<string> { "switch (value.Value)", "{" };

        foreach (var branch in branches) {
            lines.Add($"    case {branch} branch:");

            if (schema.DiscriminatorPropertyName != null &&
                discriminator.TryGetValue(branch, out var value)) {
                lines.Add(
                    "        Write(writer, branch, options, " +
                    $"\"{Escape(schema.DiscriminatorPropertyName)}\", \"{Escape(value)}\");");
            } else {
                lines.Add("        Write(writer, branch, options, null, null);");
            }

            lines.Add("        break;");
        }

        // Reachable only through default(T), which nothing here produces - but writing null for it
        // would put a payload on the wire that the schema does not permit.
        lines.Add("    default:");
        lines.Add("        throw new global::System.Text.Json.JsonException(");
        lines.Add("            \"The value is not one of the permitted types.\");");
        lines.Add("}");

        Write(method, lines);
    }

    /// <summary>The discriminator value for each branch, by the branch's qualified name.</summary>
    private static Dictionary<string, string> Discriminators(
        SchemaModel schema, string modelsNamespace) {
        var values = new Dictionary<string, string>(System.StringComparer.Ordinal);

        foreach (var mapping in schema.DiscriminatorMapping) {
            var branch = TypeMapper.QualifiedName(
                modelsNamespace, NamingHelper.ToPascalCase(TypeMapper.GetRefName(mapping.Ref)), false);

            // First wins: a document may map two values onto one schema, and the payload can only
            // carry one of them.
            if (!values.ContainsKey(branch)) {
                values[branch] = mapping.Value;
            }
        }

        return values;
    }

    /// <summary>
    /// Reading and writing one branch, through the resolver rather than the reflection overloads.
    /// </summary>
    private static void EmitHelpers(ClassDefinition converter) {
        var read = converter.AddMethod("Read");

        read.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;
        read.AddGenericParameter(Generic);
        read.SetReturnType(Generic);
        read.AddParameter(TypeDefinition.Get("System.Text.Json", "JsonElement"), "element");
        read.AddParameter(Options, "options");

        Write(read, new List<string> {
            "var typeInfo = (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)",
            "    options.GetTypeInfo(typeof(T));",
            "",
            "var value = global::System.Text.Json.JsonSerializer.Deserialize(element, typeInfo);",
            "",
            "if (value is null)",
            "{",
            "    throw new global::System.Text.Json.JsonException(",
            "        \"Expected \" + typeof(T).Name + \", found null.\");",
            "}",
            "",
            "return value;"
        });

        var write = converter.AddMethod("Write");

        write.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;
        write.AddGenericParameter(Generic);
        write.AddParameter(Writer, "writer");
        write.AddParameter(Generic, "value");
        write.AddParameter(Options, "options");
        write.AddParameter(TypeDefinition.Get(typeof(string)).MakeNullable(), "discriminator");
        write.AddParameter(TypeDefinition.Get(typeof(string)).MakeNullable(), "kind");

        Write(write, new List<string> {
            "var typeInfo = (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)",
            "    options.GetTypeInfo(typeof(T));",
            "",
            "if (discriminator is null || kind is null)",
            "{",
            "    global::System.Text.Json.JsonSerializer.Serialize(writer, value, typeInfo);",
            "    return;",
            "}",
            "",
            "var element = global::System.Text.Json.JsonSerializer.SerializeToElement(value, typeInfo);",
            "",
            "// A branch that is not an object has nowhere to carry a discriminator; the document",
            "// declaring one on it is a contradiction this cannot resolve, so the value goes out",
            "// as it is rather than being wrapped in something the schema does not describe.",
            "if (element.ValueKind != global::System.Text.Json.JsonValueKind.Object)",
            "{",
            "    element.WriteTo(writer);",
            "    return;",
            "}",
            "",
            "writer.WriteStartObject();",
            "writer.WriteString(discriminator, kind);",
            "",
            "foreach (var property in element.EnumerateObject())",
            "{",
            "    // Skipped rather than written twice - the value above is the authority.",
            "    if (property.NameEquals(discriminator))",
            "    {",
            "        continue;",
            "    }",
            "",
            "    property.WriteTo(writer);",
            "}",
            "",
            "writer.WriteEndObject();"
        });
    }

    /// <summary>
    /// Bodies are added as lines rather than statements: <c>AddIndentedStatement</c> appends a
    /// <c>;</c> per component, which turns a brace into <c>{;</c> and an <c>if</c> into a statement
    /// that guards nothing - code that compiles and does the opposite of what it says.
    /// </summary>
    private static void Write(MethodDefinition method, List<string> lines) {
        foreach (var line in lines) {
            method.Add(new CodeOutputComponent(line) { Indented = true });
        }
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
