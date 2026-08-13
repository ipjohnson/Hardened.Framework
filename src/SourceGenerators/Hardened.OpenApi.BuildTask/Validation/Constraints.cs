using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hardened.OpenApi.SourceGenerator.Models;
using ValidationModules.SourceGenerator.Impl;
using ValidationModules.SourceGenerator.Impl.Models;

namespace Hardened.OpenApi.BuildTask.Validation;

/// <summary>
/// OpenAPI's constraint keywords, as ValidationModules constraints.
/// </summary>
/// <remarks>
/// Parameters and schema properties carry the same keywords in two shapes, so the mapping is written
/// once and fed from both.
/// </remarks>
internal static class Constraints {

    public static ConstraintModel[] ForParameter(ParameterModel parameter, PatternRegistry patterns) =>
        Build(
            required: parameter.IsRequired,
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

    public static ConstraintModel[] ForProperty(
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

    private static ConstraintModel[] Build(
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
        var constraints = new List<ConstraintModel>();

        // Required first: it suppresses the rest on the same field, so a missing value reports once
        // rather than once per constraint it also fails.
        if (required) {
            constraints.Add(new ConstraintModel(ConstraintKind.Required));
        }

        if (minLength.HasValue || maxLength.HasValue) {
            constraints.Add(new ConstraintModel(
                ConstraintKind.StringLength,
                Min: minLength?.ToString(CultureInfo.InvariantCulture),
                Max: maxLength?.ToString(CultureInfo.InvariantCulture)));
        }

        if (minimum.HasValue || maximum.HasValue) {
            constraints.Add(new ConstraintModel(
                ConstraintKind.Range,
                Min: minimum?.ToString(CultureInfo.InvariantCulture),
                Max: maximum?.ToString(CultureInfo.InvariantCulture),
                ExclusiveMin: exclusiveMinimum,
                ExclusiveMax: exclusiveMaximum));
        }

        if (!string.IsNullOrEmpty(pattern)) {
            // Unanchored, because OpenAPI patterns are JSON Schema patterns and those match
            // anywhere in the value. DataAnnotations' [RegularExpression] is the anchored one, and
            // the two are states of one constraint rather than two kinds - API-SURFACE.md §18.3.
            constraints.Add(new ConstraintModel(
                ConstraintKind.Pattern,
                Pattern: pattern,
                Anchored: false,
                RegexAccessor: patterns.Accessor(pattern!)));
        }

        if (minItems.HasValue || maxItems.HasValue) {
            constraints.Add(new ConstraintModel(
                ConstraintKind.ItemCount,
                Min: minItems?.ToString(CultureInfo.InvariantCulture),
                Max: maxItems?.ToString(CultureInfo.InvariantCulture)));
        }

        if (enumValues is { Count: > 0 }) {
            constraints.Add(new ConstraintModel(
                ConstraintKind.AllowedValues,
                Values: enumValues.Select(Quote).ToEquatableArray()));
        }

        return constraints.ToArray();
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
