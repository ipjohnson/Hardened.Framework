using System.Collections.Generic;
using System.Linq;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;

namespace Hardened.SourceGenerator.Web;

/// <summary>
/// A converter per enum the application puts on the wire, carrying the vocabulary the document
/// declares.
/// </summary>
/// <remarks>
/// <para>
/// The code-first counterpart of what a description already gives a contract-first application.
/// System.Text.Json writes an enum as its ordinal unless something says otherwise, and the setting
/// that changes that - <c>UseStringEnumConverter</c> - writes the C# member name, which is an
/// identifier rather than a wire value. Neither is a vocabulary anyone chose.
/// </para>
/// <para>
/// Generated rather than attributed because the enum belongs to the application: nothing can add
/// <c>[JsonConverter]</c> to a type someone else declared, and an enum is not partial. A converter
/// reached through a <c>JsonTypeInfo</c> attaches to the type from outside, which is the same route
/// a source-generated context uses and works identically under Native AOT.
/// </para>
/// <para>
/// <c>TryParseWire</c> exists for the binder rather than the serializer. A path or query value
/// arrives as text and never passes through a JSON converter, so without it
/// <c>?priority=in-progress</c> is answered 400 by an application whose body accepts exactly that -
/// and any declared value that is not a valid C# identifier is unreachable as a parameter.
/// </para>
/// </remarks>
internal static class EnumWireConverterEmitter {

    public const string ContainerName = "JsonEnums";
    private const string ResolverName = "Resolver";

    /// <summary>
    /// Every enum reachable from a handler's request, response or declared response set.
    /// </summary>
    /// <remarks>
    /// Deduplicated by type name. One enum reached from two handlers resolves to the same
    /// vocabulary - it is a property of the type, not of the route - and emitting it twice would
    /// not compile.
    /// </remarks>
    public static IReadOnlyList<EnumVocabulary> Collect(IReadOnlyList<RequestHandlerModel> handlers) =>
        EnumVocabularies.Collect(handlers);

    public static void Emit(ClassDefinition appClass, IReadOnlyList<EnumVocabulary> enums) {
        if (enums.Count == 0) {
            return;
        }

        var container = appClass.AddClass(ContainerName);

        container.Modifiers |= ComponentModifier.Public | ComponentModifier.Static;
        container.Comment =
            "The wire vocabulary of every enum this application serializes. See [JsonEnumNaming].";

        foreach (var vocabulary in enums) {
            EmitConverter(container, vocabulary);
        }

        EmitResolver(container, enums);
        EmitStringConverters(container, enums);
    }

    private static string ConverterName(EnumVocabulary vocabulary) => vocabulary.Name + "WireConverter";

    private static void EmitConverter(ClassDefinition container, EnumVocabulary vocabulary) {
        var enumType = TypeDefinition.Get("", vocabulary.QualifiedName);
        var converter = container.AddClass(ConverterName(vocabulary));

        converter.Modifiers |= ComponentModifier.Public | ComponentModifier.Sealed;
        converter.AddBaseType(new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition,
            "System.Text.Json.Serialization",
            "JsonConverter",
            new[] { enumType }));

        converter.Comment =
            $"Reads and writes {vocabulary.Name} as the values the document declares " +
            $"({vocabulary.Naming}).";

        var instance = converter.AddField(
            TypeDefinition.Get("", ConverterName(vocabulary)), "Instance");

        instance.Modifiers |=
            ComponentModifier.Public | ComponentModifier.Static | ComponentModifier.Readonly;
        instance.InitializeValue = new CodeOutputComponent("new()") { Indented = false };

        EmitRead(converter, vocabulary, enumType);
        EmitWrite(converter, vocabulary, enumType);
        EmitTryParseWire(converter, vocabulary, enumType);
    }

    private static void EmitRead(
        ClassDefinition converter, EnumVocabulary vocabulary, ITypeDefinition enumType) {
        var method = converter.AddMethod("Read");

        method.Modifiers |= ComponentModifier.Public | ComponentModifier.Override;
        method.SetReturnType(enumType);
        method.AddParameter(
            TypeDefinition.Get("System.Text.Json", "Utf8JsonReader"), "reader").Modifier =
            ParameterModifier.Ref;
        method.AddParameter(TypeDefinition.Get(typeof(System.Type)), "typeToConvert");
        method.AddParameter(
            TypeDefinition.Get("System.Text.Json", "JsonSerializerOptions"), "options");

        var lines = new List<string> {
            "var value = reader.GetString();",
            "",
            "return value switch",
            "{"
        };

        foreach (var value in vocabulary.Values) {
            lines.Add($"    \"{Escape(value.Wire)}\" => {vocabulary.QualifiedName}.{value.Member},");
        }

        // A value the application does not declare is not guessed at. The JsonException lands as a
        // 400, which is the same answer a malformed body gets and the right one for a value that is
        // not in the document.
        lines.Add("    _ => throw new global::System.Text.Json.JsonException(");
        lines.Add($"        \"'\" + value + \"' is not a value {Escape(vocabulary.Name)} declares.\")");
        lines.Add("};");

        Write(method, lines);
    }

    private static void EmitWrite(
        ClassDefinition converter, EnumVocabulary vocabulary, ITypeDefinition enumType) {
        var method = converter.AddMethod("Write");

        method.Modifiers |= ComponentModifier.Public | ComponentModifier.Override;
        method.AddParameter(TypeDefinition.Get("System.Text.Json", "Utf8JsonWriter"), "writer");
        method.AddParameter(enumType, "value");
        method.AddParameter(
            TypeDefinition.Get("System.Text.Json", "JsonSerializerOptions"), "options");

        var lines = new List<string> { "string wire = value switch", "{" };

        foreach (var value in vocabulary.Values) {
            lines.Add($"    {vocabulary.QualifiedName}.{value.Member} => \"{Escape(value.Wire)}\",");
        }

        // Reachable by casting an undeclared number to the enum. Refused rather than written, since
        // the document does not describe it and a client has no way to read it.
        lines.Add("    _ => throw new global::System.Text.Json.JsonException(");
        lines.Add($"        \"The value is not one {Escape(vocabulary.Name)} declares.\")");
        lines.Add("};");
        lines.Add("");
        lines.Add("writer.WriteStringValue(wire);");

        Write(method, lines);
    }

    private static void EmitTryParseWire(
        ClassDefinition converter, EnumVocabulary vocabulary, ITypeDefinition enumType) {
        var method = converter.AddMethod("TryParseWire");

        method.Modifiers |= ComponentModifier.Public | ComponentModifier.Static;
        method.SetReturnType(typeof(bool));
        method.AddParameter(typeof(string), "value");
        method.AddParameter(enumType, "parsed").Modifier = ParameterModifier.Out;

        method.Comment =
            $"Parses one of {vocabulary.Name}'s declared values from text, as a parameter carries it.";

        var lines = new List<string> { "switch (value)", "{" };

        foreach (var value in vocabulary.Values) {
            lines.Add($"    case \"{Escape(value.Wire)}\":");
            lines.Add($"        parsed = {vocabulary.QualifiedName}.{value.Member};");
            lines.Add("        return true;");
        }

        lines.Add("    default:");
        lines.Add($"        parsed = default({vocabulary.QualifiedName});");
        lines.Add("        return false;");
        lines.Add("}");

        Write(method, lines);
    }

    /// <summary>
    /// The resolver that attaches each converter to its type.
    /// </summary>
    /// <remarks>
    /// A resolver rather than an entry in <c>Options.Converters</c>. An options-level converter
    /// outranks a <c>[JsonConverter]</c> attribute on a type, so putting these there would override
    /// any converter a model declared for itself; a <c>JsonTypeInfo</c> answers only for the type it
    /// is asked about. <c>JsonMetadataServices.CreateValueInfo</c> is the AOT-safe construction.
    /// </remarks>
    private static void EmitResolver(ClassDefinition container, IReadOnlyList<EnumVocabulary> enums) {
        var resolver = container.AddClass(ResolverName);

        resolver.Modifiers |= ComponentModifier.Public | ComponentModifier.Sealed;
        resolver.AddBaseType(
            TypeDefinition.Get("System.Text.Json.Serialization.Metadata", "IJsonTypeInfoResolver"));
        resolver.Comment = "Metadata for this application's enums, ahead of reflection.";

        var instance = resolver.AddField(TypeDefinition.Get("", ResolverName), "Instance");

        instance.Modifiers |=
            ComponentModifier.Public | ComponentModifier.Static | ComponentModifier.Readonly;
        instance.InitializeValue = new CodeOutputComponent("new()") { Indented = false };

        var method = resolver.AddMethod("GetTypeInfo");

        method.Modifiers |= ComponentModifier.Public;
        method.SetReturnType(
            TypeDefinition.Get("System.Text.Json.Serialization.Metadata", "JsonTypeInfo")
                .MakeNullable());
        method.AddParameter(TypeDefinition.Get(typeof(System.Type)), "type");
        method.AddParameter(TypeDefinition.Get("System.Text.Json", "JsonSerializerOptions"), "options");

        var lines = new List<string>();

        foreach (var vocabulary in enums) {
            lines.Add($"if (type == typeof({vocabulary.QualifiedName})) {{");
            lines.Add(
                "    return global::System.Text.Json.Serialization.Metadata.JsonMetadataServices" +
                $".CreateValueInfo<{vocabulary.QualifiedName}>(");
            lines.Add($"        options, {ConverterName(vocabulary)}.Instance);");
            lines.Add("}");
            lines.Add("");
        }

        // Null rather than a guess, so the rest of the chain answers for everything else.
        lines.Add("return null;");

        Write(method, lines);
    }

    /// <summary>
    /// The same vocabularies as the parameter binder consumes them.
    /// </summary>
    private static void EmitStringConverters(
        ClassDefinition container, IReadOnlyList<EnumVocabulary> enums) {
        var converters = enums
            .Select(vocabulary =>
                "new global::Hardened.Requests.Abstract.Serializer.DelegatingStringConverter<" +
                $"{vocabulary.QualifiedName}>({ConverterName(vocabulary)}.TryParseWire, " +
                $"\"{Escape(vocabulary.Name)}\")")
            .ToList();

        var field = container.AddField(
            TypeDefinition.Get("Hardened.Requests.Abstract.Serializer", "IStringConverter").MakeArray(),
            "StringConverters");

        field.Modifiers |=
            ComponentModifier.Public | ComponentModifier.Static | ComponentModifier.Readonly;
        field.InitializeValue =
            new CodeOutputComponent("{ " + string.Join(", ", converters) + " }") { Indented = false };
    }

    private static void Write(MethodDefinition method, List<string> lines) {
        foreach (var line in lines) {
            method.Add(new CodeOutputComponent(line) { Indented = true });
        }
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
