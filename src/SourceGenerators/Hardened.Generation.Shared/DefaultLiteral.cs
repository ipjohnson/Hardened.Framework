using System.Globalization;

namespace Hardened.Generation;

/// <summary>
/// A specification's <c>default</c>, as a C# literal.
/// </summary>
/// <remarks>
/// <para>
/// Only types with a constant form are rendered. C# requires an optional parameter's default and a
/// <c>const</c> to be compile-time constants, so <c>DateTime</c>, <c>DateOnly</c>, <c>TimeOnly</c>,
/// <c>TimeSpan</c> and <c>Guid</c> have no representation here whatever the spec says - a
/// constructor call is not a constant. Those fall back to the type's own default, which is why the
/// caller reports what it could not honour rather than passing over it in silence.
/// </para>
/// <para>
/// <c>decimal</c> is rendered, unlike the others: it does have a literal form.
/// </para>
/// </remarks>
internal static class DefaultLiteral {

    /// <summary>
    /// The literal, or null where the value has no constant form in this type.
    /// </summary>
    public static string? Format(string? value, string csType) {
        if (value == null) {
            return null;
        }

        switch (csType) {
            case "string":
                return "\"" + Escape(value) + "\"";

            case "bool":
                // The spec's booleans are already lowercase; a hand-written True would not be.
                return value.Trim().ToLowerInvariant() is "true" or "false"
                    ? value.Trim().ToLowerInvariant()
                    : null;

            case "int":
            case "long":
            case "uint":
                return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    ? value.Trim()
                    : null;

            case "float":
                return Number(value, "f");

            case "double":
                return Number(value, "");

            case "decimal":
                return Number(value, "m");

            default:
                return null;
        }
    }

    private static string? Number(string value, string suffix) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            ? value.Trim() + suffix
            : null;

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
