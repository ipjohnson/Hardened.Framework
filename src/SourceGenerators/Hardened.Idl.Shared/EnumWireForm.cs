using System.Collections.Generic;
using System.Globalization;
using Hardened.Idl.Models;

namespace Hardened.Idl;

/// <summary>
/// What an enum looks like on the wire, which is whatever the description declared.
/// </summary>
/// <remarks>
/// <para>
/// A string enum carries its declared strings - <c>"science-fiction"</c> - and an integer enum
/// carries numbers. Neither is the C# member name. Every emitter that writes an enum, and the
/// parameter binder that reads one, has to agree about which, so the answer is derived here rather
/// than in each of them.
/// </para>
/// <para>
/// Integer enums produced an empty C# enum until 2026-08-20: <c>ParseSchemaKind</c> recognised
/// <c>enum:</c> whatever its members were, and the member reader dropped everything that was not a
/// string. The generated converter's switch was left with only its throwing arm, so every value of
/// such an enum failed at run time on a build that was clean.
/// </para>
/// </remarks>
internal static class EnumWireForm {

    /// <summary>Whether the declared members are numbers rather than strings.</summary>
    public static bool IsNumeric(SchemaModel schema) =>
        schema.Type == "integer" || schema.Type == "number" ||
        schema.Type == "int" || schema.Type == "long";

    /// <summary>
    /// The C# literal for a member's declared value - a quoted string, or the number itself.
    /// </summary>
    public static string Literal(SchemaModel schema, int index) {
        var value = schema.EnumValues[index];

        return IsNumeric(schema) ? value : "\"" + Escape(value) + "\"";
    }

    /// <summary>
    /// The underlying type a numeric enum needs, or null to leave the C# default of <c>int</c>.
    /// </summary>
    /// <remarks>
    /// A document is free to declare values outside <c>int</c>, and an enum whose members do not fit
    /// its underlying type does not compile. Widened rather than reported, because the value is
    /// legitimate and the type is an implementation detail of carrying it.
    /// </remarks>
    public static string? UnderlyingType(SchemaModel schema) {
        if (!IsNumeric(schema)) {
            return null;
        }

        foreach (var value in schema.EnumValues) {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) {
                return "long";
            }
        }

        return null;
    }

    /// <summary>
    /// The C# member name for a declared value, when the document did not name one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An integer enum declares values and no names, so there is nothing to PascalCase. Documents
    /// that care say so with <c>x-enum-varnames</c> or <c>x-enumNames</c>, which both NSwag and
    /// openapi-generator read and which the parser records as member names; this is the fallback
    /// when neither is present.
    /// </para>
    /// <para>
    /// <c>Value3</c> rather than <c>_3</c>, and <c>ValueMinus1</c> for a negative, because a member
    /// name appears at every call site and has to be typeable.
    /// </para>
    /// </remarks>
    public static string SynthesizedName(string value) {
        var trimmed = value.Trim();

        return trimmed.StartsWith("-")
            ? "ValueMinus" + Sanitize(trimmed.Substring(1))
            : "Value" + Sanitize(trimmed);
    }

    private static string Sanitize(string value) {
        var builder = new System.Text.StringBuilder(value.Length);

        foreach (var character in value) {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.Length == 0 ? "0" : builder.ToString();
    }

    /// <summary>The member names to emit, honouring the document's own where it gave them.</summary>
    public static IReadOnlyList<string> MemberNames(SchemaModel schema) {
        if (schema.EnumMemberNames.Count == schema.EnumValues.Count) {
            return schema.EnumMemberNames;
        }

        var names = new List<string>(schema.EnumValues.Count);

        foreach (var value in schema.EnumValues) {
            names.Add(IsNumeric(schema)
                ? SynthesizedName(value)
                : NamingHelper.ToPascalCase(value));
        }

        return names;
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
