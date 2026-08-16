using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CSharpAuthor;
using Hardened.Idl.Models;
using Hardened.Idl;

namespace Hardened.OpenApi.BuildTask.Validation;

/// <summary>
/// OpenAPI's constraint keywords, as ValidationModules attributes to put on emitted members.
/// </summary>
/// <remarks>
/// <para>
/// The task writes attributes rather than validators. The validator is emitted by
/// <c>Hardened.Validation.SourceGenerator</c>, which reads these out of the compilation exactly as
/// it reads attributes a developer wrote - so a spec-declared <c>maxLength</c> and a hand-written
/// <c>[StringLength]</c> are one path rather than two that agree.
/// </para>
/// <para>
/// Patterns use the reference form, <c>[Pattern(typeof(X), nameof(X.Y))]</c>, pointing at a
/// <c>[GeneratedRegex]</c> member the task also emits. The inline form makes the generator declare a
/// <c>Regex</c> itself, which roots the parser and interpreter - 448 KB on an AOT publish against
/// 33 KB - and is what VM0017 rejects in an AOT-facing project.
/// </para>
/// </remarks>
internal static class ConstraintAttributes {

    private const string Constraints = "ValidationModules.Constraints";

    /// <param name="Type">The attribute type.</param>
    /// <param name="Arguments">Already-rendered arguments, positional then named.</param>
    internal sealed record Model(ITypeDefinition Type, IReadOnlyList<string> Arguments);

    /// <param name="required">
    /// From the caller, for the same reason as <see cref="ForProperty"/>. Computing it here from
    /// the model alone put <c>[Required]</c> on non-nullable value types, and the validation
    /// generator answers that with <c>value.petId is null</c> against a <c>long</c> - CS0037, in a
    /// generated file, from a spec that is not wrong.
    /// </param>
    /// <param name="allowedValues">
    /// False where the C# type already admits only the permitted values. A parameter whose schema
    /// is a <c>$ref</c> to an enum is typed as the generated enum, and <c>[AllowedValues]</c> then
    /// compares that enum against string literals - CS0019, once per member.
    /// </param>
    public static IReadOnlyList<Model> ForParameter(
        ParameterModel parameter, bool required, bool allowedValues, PatternRegistry patterns) =>
        Build(parameter, required, patterns, allowedValues);

    /// <param name="required">
    /// From the caller rather than the model: it also knows whether the C# type makes
    /// <c>[Required]</c> unfailable - see <c>TypeMapper.IsNonNullableValueType</c>.
    /// </param>
    /// <param name="allowedValues">
    /// False where the C# type already admits only the permitted values - see the same parameter on
    /// <see cref="ForParameter"/>.
    /// </param>
    public static IReadOnlyList<Model> ForProperty(
        PropertyModel property, bool required, PatternRegistry patterns,
        bool allowedValues = true) =>
        Build(property, required, patterns, allowedValues);

    private static IReadOnlyList<Model> Build(
        IConstraintFacets facets, bool required, PatternRegistry patterns,
        bool allowedValues = true) {
        var minLength = facets.MinLength;
        var maxLength = facets.MaxLength;
        var minimum = facets.Minimum;
        var maximum = facets.Maximum;
        var exclusiveMinimum = facets.ExclusiveMinimum;
        var exclusiveMaximum = facets.ExclusiveMaximum;
        var pattern = facets.Pattern;
        var minItems = facets.MinItems;
        var maxItems = facets.MaxItems;
        var enumValues = facets.EnumValues;

        var attributes = new List<Model>();

        if (required) {
            attributes.Add(new Model(Attribute("RequiredAttribute"), System.Array.Empty<string>()));
        }

        // Named arguments, because a spec may set one bound and not the other while the positional
        // constructors take both. Min and Max default to unbounded.
        if (minLength.HasValue || maxLength.HasValue) {
            attributes.Add(new Model(Attribute("StringLengthAttribute"), Bounds(minLength, maxLength)));
        }

        if (minimum.HasValue || maximum.HasValue) {
            // Range has no partially-bounded constructor, and its double overload takes the widest
            // set of spec values - so an absent bound becomes the extreme rather than being omitted.
            var arguments = new List<string> {
                Literal(minimum, decimal.MinValue),
                Literal(maximum, decimal.MaxValue),
            };

            if (exclusiveMinimum) {
                arguments.Add("ExclusiveMin = true");
            }

            if (exclusiveMaximum) {
                arguments.Add("ExclusiveMax = true");
            }

            attributes.Add(new Model(Attribute("RangeAttribute"), arguments));
        }

        if (!string.IsNullOrEmpty(pattern)) {
            attributes.Add(new Model(Attribute("PatternAttribute"), patterns.AttributeArguments(pattern!)));
        }

        if (minItems.HasValue || maxItems.HasValue) {
            attributes.Add(new Model(Attribute("ItemCountAttribute"), Bounds(minItems, maxItems)));
        }

        if (allowedValues && enumValues is { Count: > 0 }) {
            attributes.Add(new Model(
                Attribute("AllowedValuesAttribute"), enumValues.Select(Quote).ToList()));
        }

        return attributes;
    }

    public static ITypeDefinition ValidateNested() => Attribute("ValidateNestedAttribute");

    private static ITypeDefinition Attribute(string name) => TypeDefinition.Get(Constraints, name);

    /// <summary>Named Min/Max arguments, omitting whichever the spec left unbounded.</summary>
    private static IReadOnlyList<string> Bounds(int? min, int? max) {
        var parts = new List<string>();

        if (min.HasValue) {
            parts.Add($"Min = {min.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (max.HasValue) {
            parts.Add($"Max = {max.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return parts;
    }

    private static string Literal(decimal? value, decimal fallback) =>
        ((double)(value ?? fallback)).ToString("R", CultureInfo.InvariantCulture);

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
