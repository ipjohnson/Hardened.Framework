using System.Collections.Generic;
using CSharpAuthor;
using Hardened.Idl;
using Hardened.Generation;
using Hardened.Generation.Models;

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
        EmitTryParseWire(converter, schema, enumType, qualified);

        return converter;
    }

    /// <summary>
    /// The same vocabulary, for a value that arrived as text rather than as JSON.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A query or path parameter is a string off the wire, not a JSON document, and the binder used
    /// to call <c>Enum.Parse</c> on it - against the C# member name. So <c>Genre</c> declaring
    /// <c>science-fiction</c> answered 400 for <c>?genre=science-fiction</c>, the document's own
    /// value, and 200 for <c>?genre=ScienceFiction</c>, a name appearing nowhere in the document.
    /// Single-word values survived only because that parse is case-insensitive.
    /// </para>
    /// <para>
    /// A static method rather than routing the binder through <c>Read</c>: quoting the value into
    /// JSON to parse it back out would mean the binder deciding whether to quote, which is exactly
    /// the wire-type question the converter already answers. It also allocates nothing per request.
    /// </para>
    /// </remarks>
    private static void EmitTryParseWire(
        ClassDefinition converter, SchemaModel schema, ITypeDefinition enumType, string qualified) {
        var method = converter.AddMethod("TryParseWire");

        method.Modifiers |= ComponentModifier.Public | ComponentModifier.Static;
        method.SetReturnType(typeof(bool));
        method.AddParameter(typeof(string), "value");
        method.AddParameter(enumType, "parsed").Modifier = ParameterModifier.Out;

        method.Comment =
            $"Parses one of {NamingHelper.ToPascalCase(schema.Name)}'s declared values from text, " +
            "as a parameter carries it.";

        var numeric = EnumWireForm.IsNumeric(schema);
        var lines = new List<string>();

        if (numeric) {
            lines.Add("if (!long.TryParse(");
            lines.Add("        value,");
            lines.Add("        global::System.Globalization.NumberStyles.Integer,");
            lines.Add("        global::System.Globalization.CultureInfo.InvariantCulture,");
            lines.Add("        out var number)) {");
            lines.Add($"    parsed = default({qualified});");
            lines.Add("    return false;");
            lines.Add("}");
            lines.Add("");
            lines.Add("switch (number)");
        }
        else {
            lines.Add("switch (value)");
        }

        lines.Add("{");

        for (var index = 0; index < schema.EnumValues.Count; index++) {
            lines.Add($"    case {EnumWireForm.Literal(schema, index)}:");
            lines.Add($"        parsed = {qualified}.{Member(schema, index)};");
            lines.Add("        return true;");
        }

        lines.Add("    default:");
        lines.Add($"        parsed = default({qualified});");
        lines.Add("        return false;");
        lines.Add("}");

        Write(method, lines);
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

        var numeric = EnumWireForm.IsNumeric(schema);

        // The wire type is the description's. A string enum reads a string and an integer enum reads
        // a number; reading the wrong one throws before the switch is reached, which is the same
        // JsonException an undeclared value produces and lands as a 400 either way.
        var lines = new List<string> {
            numeric
                ? "var value = reader.GetInt64();"
                : "var value = reader.GetString();",
            "",
            "return value switch",
            "{"
        };

        for (var index = 0; index < schema.EnumValues.Count; index++) {
            lines.Add($"    {EnumWireForm.Literal(schema, index)} => {qualified}.{Member(schema, index)},");
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

        var numeric = EnumWireForm.IsNumeric(schema);

        // Assigned to a typed local rather than passed straight in, so the switch has a natural type
        // and the Write*Value overload is unambiguous - CS0121 otherwise.
        //
        // This used to carry a note that a document may declare an enum with no values at all,
        // which was that defect wearing a workaround: an integer enum reached here with every member
        // filtered away by the parser. It cannot now - a members-less enum is not emitted.
        var lines = new List<string> {
            numeric ? "long wire = value switch" : "string wire = value switch", "{" };

        for (var index = 0; index < schema.EnumValues.Count; index++) {
            lines.Add($"    {qualified}.{Member(schema, index)} => {EnumWireForm.Literal(schema, index)},");
        }

        // Reachable by casting an undeclared number to the enum, which writes a value the contract
        // does not describe - worth refusing rather than putting on the wire.
        lines.Add("    _ => throw new global::System.Text.Json.JsonException(");
        lines.Add(
            $"        \"The value is not one {NamingHelper.ToPascalCase(schema.Name)} declares.\")");
        lines.Add("};");
        lines.Add("");
        lines.Add(numeric ? "writer.WriteNumberValue(wire);" : "writer.WriteStringValue(wire);");

        Write(method, lines);
    }

    /// <summary>
    /// The C# member for a value, which the allocator decided and which is not the value itself.
    /// </summary>
    private static string Member(SchemaModel schema, int index) =>
        EnumWireForm.MemberNames(schema)[index];

    private static void Write(MethodDefinition method, List<string> lines) {
        foreach (var line in lines) {
            method.Add(new CodeOutputComponent(line) { Indented = true });
        }
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
