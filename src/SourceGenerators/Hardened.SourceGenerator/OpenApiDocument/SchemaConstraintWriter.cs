using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.OpenApiDocument;

/// <summary>
/// ValidationModules constraints as JSON Schema facets.
/// </summary>
/// <remarks>
/// <para>
/// The constraint and the schema facet are the same statement written twice - <c>[Range(1, 5)]</c>
/// and <c>"minimum": 1, "maximum": 5</c> say one thing. Without this the document describes a
/// property as merely "an integer" while the server rejects most integers, so a generated client
/// cannot validate anything before sending it and a reader of the document is told less than the
/// application knows.
/// </para>
/// <para>
/// Only the <c>ValidationModules.Constraints</c> vocabulary. <c>System.ComponentModel
/// .DataAnnotations</c> declares constraints of the same names with different shapes -
/// <c>[StringLength(max)]</c> with a <c>MinimumLength</c> property rather than two arguments,
/// <c>[RegularExpression]</c> rather than <c>[Pattern]</c> - and mapping a second vocabulary is a
/// second set of decisions, not a longer switch. A DataAnnotations constraint is simply not
/// reflected in the schema, which is what happened to every constraint before this existed.
/// </para>
/// <para>
/// Written against OpenAPI 3.0, which the document declares: <c>exclusiveMinimum</c> is a boolean
/// beside <c>minimum</c> there, not a number as in later JSON Schema drafts.
/// </para>
/// </remarks>
internal static class SchemaConstraintWriter {
    private const string ConstraintsNamespace = "ValidationModules.Constraints";

    /// <summary>
    /// <paramref name="schema"/> with the facets <paramref name="property"/>'s constraints imply.
    /// </summary>
    /// <remarks>
    /// A property whose schema is a <c>$ref</c> is returned untouched. OpenAPI 3.0 ignores every
    /// sibling of a <c>$ref</c>, so facets written beside one would be silently dropped by any
    /// reader - which is worse than not writing them, because the document would claim a
    /// constraint it does not communicate.
    /// </remarks>
    public static string Apply(string schema, IPropertySymbol property) {
        if (schema.Length < 2 || schema.IndexOf("\"$ref\"", System.StringComparison.Ordinal) >= 0) {
            return schema;
        }

        var facets = new List<string>();

        foreach (var attribute in property.GetAttributes()) {
            if (attribute.AttributeClass?.ContainingNamespace?.ToDisplayString() != ConstraintsNamespace) {
                continue;
            }

            Facets(attribute, facets);
        }

        if (facets.Count == 0) {
            return schema;
        }

        // "{}" - nothing was known about the type - becomes an object carrying only the facets.
        var body = schema.Substring(1, schema.Length - 2);
        var separator = body.Length > 0 ? "," : "";

        return "{" + body + separator + string.Join(",", facets) + "}";
    }

    /// <summary>Whether the property is required because a constraint says so.</summary>
    /// <remarks>
    /// Separate from <see cref="Apply"/> because <c>required</c> is a list on the enclosing object
    /// rather than a facet on the property, which is where a schema says it.
    /// </remarks>
    public static bool IsRequired(IPropertySymbol property) =>
        property.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.Name == "RequiredAttribute" &&
            attribute.AttributeClass.ContainingNamespace?.ToDisplayString() == ConstraintsNamespace);

    private static void Facets(AttributeData attribute, List<string> facets) {
        switch (attribute.AttributeClass!.Name) {
            case "RangeAttribute":
                Range(attribute, facets);
                break;

            case "StringLengthAttribute":
                Bounds(attribute, facets, "minLength", "maxLength");
                break;

            case "ItemCountAttribute":
                Bounds(attribute, facets, "minItems", "maxItems");
                break;

            case "PatternAttribute":
                Pattern(attribute, facets);
                break;

            case "AllowedValuesAttribute":
                AllowedValues(attribute, facets);
                break;

            case "MultipleOfAttribute":
                Single(attribute, facets, "multipleOf");
                break;

            case "UniqueItemsAttribute":
                facets.Add("\"uniqueItems\":true");
                break;
        }
    }

    /// <summary>
    /// <c>[Range(0, 30)]</c>, <c>[Range(Min = 1)]</c> and <c>[Range(0.0, 1.0, ExclusiveMax = true)]</c>
    /// are all the same constraint written three ways, so both argument forms are read and the
    /// named one wins where they overlap.
    /// </summary>
    private static void Range(AttributeData attribute, List<string> facets) {
        var min = attribute.ConstructorArguments.Length == 2
            ? Literal(attribute.ConstructorArguments[0])
            : null;

        var max = attribute.ConstructorArguments.Length == 2
            ? Literal(attribute.ConstructorArguments[1])
            : null;

        foreach (var named in attribute.NamedArguments) {
            switch (named.Key) {
                case "Min":
                    min = Literal(named.Value);
                    break;

                case "Max":
                    max = Literal(named.Value);
                    break;
            }
        }

        Add(facets, "minimum", min);
        Add(facets, "maximum", max);

        Flag(attribute, facets, "ExclusiveMin", "exclusiveMinimum");
        Flag(attribute, facets, "ExclusiveMax", "exclusiveMaximum");
    }

    /// <summary>
    /// The shape <c>StringLength</c> and <c>ItemCount</c> share: two constructor arguments or two
    /// named ones, with <c>Max</c> defaulting to <c>int.MaxValue</c> to mean "no upper bound".
    /// Writing that default out would document a limit nobody set.
    /// </summary>
    private static void Bounds(
        AttributeData attribute, List<string> facets, string minFacet, string maxFacet) {
        int? min = null;
        int? max = null;

        if (attribute.ConstructorArguments.Length == 2) {
            min = attribute.ConstructorArguments[0].Value as int?;
            max = attribute.ConstructorArguments[1].Value as int?;
        }

        foreach (var named in attribute.NamedArguments) {
            switch (named.Key) {
                case "Min":
                    min = named.Value.Value as int?;
                    break;

                case "Max":
                    max = named.Value.Value as int?;
                    break;
            }
        }

        if (min is > 0) {
            facets.Add("\"" + minFacet + "\":" + min.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (max is > 0 && max.Value != int.MaxValue) {
            facets.Add("\"" + maxFacet + "\":" + max.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Only the literal form. <c>[Pattern(typeof(Skus), nameof(Skus.Sku))]</c> points at a
    /// <c>[GeneratedRegex]</c> member whose expression lives in an attribute on another type - the
    /// AOT form - and there is no literal here to write. Writing the member's name into
    /// <c>pattern</c> would put something in the document that is not a regular expression.
    /// </summary>
    private static void Pattern(AttributeData attribute, List<string> facets) {
        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not string pattern) {
            return;
        }

        facets.Add("\"pattern\":\"" + JsonSchemaWriter.Escape(pattern) + "\"");
    }

    private static void AllowedValues(AttributeData attribute, List<string> facets) {
        if (attribute.ConstructorArguments.Length != 1) {
            return;
        }

        var values = attribute.ConstructorArguments[0].Values
            .Select(Literal)
            .Where(value => value != null)
            .ToArray();

        if (values.Length > 0) {
            facets.Add("\"enum\":[" + string.Join(",", values) + "]");
        }
    }

    private static void Single(AttributeData attribute, List<string> facets, string facet) {
        if (attribute.ConstructorArguments.Length == 1) {
            Add(facets, facet, Literal(attribute.ConstructorArguments[0]));
        }
    }

    private static void Flag(
        AttributeData attribute, List<string> facets, string argument, string facet) {
        foreach (var named in attribute.NamedArguments) {
            if (named.Key == argument && named.Value.Value is true) {
                facets.Add("\"" + facet + "\":true");
            }
        }
    }

    private static void Add(List<string> facets, string facet, string? value) {
        if (value != null) {
            facets.Add("\"" + facet + "\":" + value);
        }
    }

    /// <summary>
    /// A constant as JSON. <c>Range</c> declares its bounds as <c>object?</c> so a date can be
    /// expressed as a string, which is why this cannot assume a number.
    /// </summary>
    private static string? Literal(TypedConstant constant) {
        if (constant.IsNull) {
            return null;
        }

        // A boxed constant inside an object[] arrives wrapped, carrying the underlying value.
        var value = constant.Kind == TypedConstantKind.Array ? null : constant.Value;

        switch (value) {
            case null:
                return null;

            case string text:
                return "\"" + JsonSchemaWriter.Escape(text) + "\"";

            case char character:
                return "\"" + JsonSchemaWriter.Escape(character.ToString()) + "\"";

            case bool flag:
                return flag ? "true" : "false";

            case double number:
                return number.ToString("R", CultureInfo.InvariantCulture);

            case float number:
                return number.ToString("R", CultureInfo.InvariantCulture);

            case decimal number:
                return number.ToString(CultureInfo.InvariantCulture);

            default:
                return System.Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
