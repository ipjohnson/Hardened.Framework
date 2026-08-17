using System.Collections.Generic;
using CSharpAuthor;
using Hardened.Idl;
using Hardened.Idl.Models;

namespace Hardened.Idl.Emitters;

/// <summary>
/// Reads and writes an enum as the values the description declares.
/// </summary>
/// <remarks>
/// <para>
/// The resolver used to hand an enum to <c>JsonMetadataServices.GetEnumConverter</c>, which is the
/// numeric converter: a property typed <c>Currency</c> wrote <c>1</c> where the API expects
/// <c>"USD"</c>, and could not read <c>"USD"</c> back at all. Every enum-typed property of every
/// generated model, in every description - and none of it visible from a build, because integers
/// are perfectly good JSON.
/// </para>
/// <para>
/// A string converter alone would not have fixed it either. The C# member is a sanitized form of
/// the wire value - <c>SELLER_CANCELED</c> reaches C# as <c>SELLERCANCELED</c>, <c>+1</c> as
/// <c>Plus1</c>, the empty value as <c>Empty</c> - so matching by member name is matching against
/// a name the wire never carries. The mapping has to be written out, and this writes it.
/// </para>
/// <para>
/// Not <c>[JsonStringEnumMemberName]</c>, which would express exactly this: it arrived in .NET 9
/// and the generated code has to compile for consumers on net8.0. When that is the floor this
/// becomes an attribute on the member and the converter goes away.
/// </para>
/// </remarks>
internal static class EnumConverterEmitter {

    /// <summary>The converter's type name, which the allocator reserves alongside the type.</summary>
    public static string ConverterName(string schemaName) =>
        NamingHelper.ToPascalCase(schemaName) + "Converter";

    public static ClassDefinition Emit(
        IConstructContainer container, SchemaModel schema, string modelsNamespace) {
        var name = NamingHelper.ToPascalCase(schema.Name);
        var converterName = ConverterName(schema.Name);
        var enumType = TypeDefinition.Get(modelsNamespace, name);
        var qualified = TypeMapper.QualifiedName(modelsNamespace, name, false);

        var converter = container.AddClass(converterName);

        converter.Modifiers |= ComponentModifier.Public | ComponentModifier.Sealed;
        converter.AddBaseType(
            new GenericTypeDefinition(
                TypeDefinitionEnum.ClassDefinition,
                "System.Text.Json.Serialization",
                "JsonConverter",
                new[] { enumType }));

        converter.Comment = $"Reads and writes {name} as the values the description declares.";

        var instance = converter.AddField(
            TypeDefinition.Get(modelsNamespace, converterName), "Instance");

        instance.Modifiers |=
            ComponentModifier.Public | ComponentModifier.Static | ComponentModifier.Readonly;
        instance.InitializeValue = new CodeOutputComponent("new()") { Indented = false };

        EmitRead(converter, schema, enumType, qualified);
        EmitWrite(converter, schema, enumType, qualified);

        return converter;
    }

    private static void EmitRead(
        ClassDefinition converter, SchemaModel schema, ITypeDefinition enumType, string qualified) {
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

        for (var index = 0; index < schema.EnumValues.Count; index++) {
            lines.Add($"    \"{Escape(schema.EnumValues[index])}\" => {qualified}.{Member(schema, index)},");
        }

        // A value the description does not declare is the server saying something the contract does
        // not allow, and guessing at it would put an arbitrary member into the model.
        lines.Add("    _ => throw new global::System.Text.Json.JsonException(");
        lines.Add(
            $"        \"'\" + value + \"' is not a value {NamingHelper.ToPascalCase(schema.Name)} declares.\")");
        lines.Add("};");

        Write(method, lines);
    }

    private static void EmitWrite(
        ClassDefinition converter, SchemaModel schema, ITypeDefinition enumType, string qualified) {
        var method = converter.AddMethod("Write");

        method.Modifiers |= ComponentModifier.Public | ComponentModifier.Override;
        method.AddParameter(
            TypeDefinition.Get("System.Text.Json", "Utf8JsonWriter"), "writer");
        method.AddParameter(enumType, "value");
        method.AddParameter(
            TypeDefinition.Get("System.Text.Json", "JsonSerializerOptions"), "options");

        var lines = new List<string> { "writer.WriteStringValue(value switch", "{" };

        for (var index = 0; index < schema.EnumValues.Count; index++) {
            lines.Add($"    {qualified}.{Member(schema, index)} => \"{Escape(schema.EnumValues[index])}\",");
        }

        // Reachable by casting an undeclared number to the enum, which writes a value the contract
        // does not describe - worth refusing rather than putting on the wire.
        lines.Add("    _ => throw new global::System.Text.Json.JsonException(");
        lines.Add(
            $"        \"The value is not one {NamingHelper.ToPascalCase(schema.Name)} declares.\")");
        lines.Add("});");

        Write(method, lines);
    }

    /// <summary>
    /// The C# member for a value, which the allocator decided and which is not the value itself.
    /// </summary>
    private static string Member(SchemaModel schema, int index) =>
        index < schema.EnumMembers.Count
            ? schema.EnumMembers[index]
            : NamingHelper.ToPascalCase(schema.EnumValues[index]);

    private static void Write(MethodDefinition method, List<string> lines) {
        foreach (var line in lines) {
            method.Add(new CodeOutputComponent(line) { Indented = true });
        }
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
