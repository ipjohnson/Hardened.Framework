using System.Collections.Generic;
using System.Linq;
using Hardened.Idl;
using Hardened.Idl.Emitters;
using Hardened.Idl.Models;

namespace Hardened.Idl.Validation;

/// <summary>
/// Which members a generated validator descends into, and whether the validator it would descend
/// into exists.
/// </summary>
/// <remarks>
/// <para>
/// <b>One definition, because a wrong answer does not fail here.</b> <c>[ValidateNested]</c> naming
/// a validator the validation generator declined to emit is <c>CS0234</c> in a generated file, and
/// the two sides derive their answer from different inputs - this task decides what to mark, and
/// <c>ValidationModules.SourceGenerator</c> decides what to generate. <c>OperationParameters</c>
/// already carried this rule inline for an operation's body and said so: "Both answers have to come
/// from one place to stay in step." This is that place.
/// </para>
/// <para>
/// <b>Per property, not by traversal.</b> A nested model's own nested members are marked when that
/// schema is emitted, by this same rule - so descent composes without this file walking anything,
/// and a schema that refers to itself needs no cycle guard because there is no walk to cycle.
/// </para>
/// <para>
/// The descent itself is <c>ValidationModules</c>': <c>ValidateNestedAttribute</c> validates an
/// object, each element of a collection, and each value of a dictionary, pathing the last as
/// <c>map[key]</c>. Nothing here has to know which of the three it marked.
/// </para>
/// </remarks>
internal static class NestedValidation {

    /// <summary>
    /// Whether this property's value is worth descending into.
    /// </summary>
    public static bool Descends(
        PropertyModel property, IReadOnlyList<SchemaModel>? allSchemas, PatternRegistry patterns) {
        if (!property.Constrained) {
            return false;
        }

        var nested = Nested(property, allSchemas);

        return nested != null && HasGeneratedValidator(nested, allSchemas, patterns);
    }

    /// <summary>
    /// The object schema this property's value is made of, where there is one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three shapes a generated model can appear in: the property itself, an array's elements,
    /// or a dictionary's values.
    /// </para>
    /// <para>
    /// A <c>oneOf</c> is excluded. It emits a generated union rather than a record, and whether that
    /// carries a validator is a different question from this one - marking it would be the CS0234
    /// this file exists to prevent.
    /// </para>
    /// <para>
    /// An element type that could not be named is excluded for free: it degrades to
    /// <c>JsonElement</c> and leaves no ref behind to resolve.
    /// </para>
    /// </remarks>
    public static SchemaModel? Nested(
        PropertyModel property, IReadOnlyList<SchemaModel>? allSchemas) {
        if (property.OneOf.Count > 0) {
            return null;
        }

        var reference = property.Ref ?? property.ArrayItemsRef ?? property.DictionaryValueRef;

        if (reference == null) {
            return null;
        }

        var name = NamingHelper.ToPascalCase(TypeMapper.GetRefName(reference));

        return allSchemas?.FirstOrDefault(
            candidate => candidate.Kind == SchemaKind.Object &&
                         NamingHelper.ToPascalCase(candidate.Name) == name);
    }

    /// <summary>
    /// Whether the validation generator will emit a validator for this schema.
    /// </summary>
    /// <remarks>
    /// Asked by building the attributes rather than by re-deriving the rule that produces them. A
    /// schema whose only constraint was <c>required</c> on a non-nullable value type gets no
    /// attributes at all, so no validator is generated for it - and that outcome is reachable only
    /// by asking <see cref="ConstraintAttributes"/> the same question the emitter asks.
    ///
    /// Declared members only. An inherited property is the base's to check, and the validation
    /// generator sees it that way too - counting them here named a validator it had already
    /// declined to generate.
    /// </remarks>
    public static bool HasGeneratedValidator(
        SchemaModel schema, IReadOnlyList<SchemaModel>? allSchemas, PatternRegistry patterns) =>
        SchemaShape.Declared(schema, allSchemas).Any(
            property => property.Constrained &&
                        Attributes(property, allSchemas, patterns).Count > 0);

    /// <summary>
    /// The constraint attributes a property would carry, by the same rules the model emitter
    /// applies.
    /// </summary>
    public static IReadOnlyList<ConstraintAttributes.Model> Attributes(
        PropertyModel property, IReadOnlyList<SchemaModel>? allSchemas, PatternRegistry patterns) {
        var csType = TypeMapper.MapPropertyToCSharpType(property);

        return ConstraintAttributes.ForProperty(
            property,
            property.ConstrainedAsRequired && !TypeMapper.IsNonNullableValueType(csType, allSchemas),
            patterns,
            csType);
    }
}
