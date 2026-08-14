using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CSharpAuthor;
using Hardened.OpenApi.SourceGenerator.Models;

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

    public static IReadOnlyList<Model> ForParameter(ParameterModel parameter, PatternRegistry patterns) =>
        Build(
            required: parameter.ConstrainedAsRequired,
            minLength: parameter.MinLength,
            maxLength: parameter.MaxLength,
            minimum: parameter.Minimum,
            maximum: parameter.Maximum,
            exclusiveMinimum: parameter.ExclusiveMinimum,
            exclusiveMaximum: parameter.ExclusiveMaximum,
            pattern: parameter.Pattern,
            minItems: parameter.MinItems,
            maxItems: parameter.MaxItems,
            enumValues: parameter.EnumValues,
            patterns: patterns);

    public static IReadOnlyList<Model> ForProperty(
        PropertyModel property, bool required, PatternRegistry patterns) =>
        Build(
            required: required,
            minLength: property.MinLength,
            maxLength: property.MaxLength,
            minimum: property.Minimum,
            maximum: property.Maximum,
            exclusiveMinimum: property.ExclusiveMinimum,
            exclusiveMaximum: property.ExclusiveMaximum,
            pattern: property.Pattern,
            minItems: property.MinItems,
            maxItems: property.MaxItems,
            enumValues: property.EnumValues,
            patterns: patterns);

    private static IReadOnlyList<Model> Build(
        bool required,
        int? minLength,
        int? maxLength,
        decimal? minimum,
        decimal? maximum,
        bool exclusiveMinimum,
        bool exclusiveMaximum,
        string? pattern,
        int? minItems,
        int? maxItems,
        List<string>? enumValues,
        PatternRegistry patterns) {
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

        if (enumValues is { Count: > 0 }) {
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
