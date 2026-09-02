namespace Hardened.Smithy.BuildTask.Parsing;

/// <summary>
/// The <c>smithy.api#</c> shapes every model targets without declaring them.
/// </summary>
/// <remarks>
/// <para>
/// A JSON AST does not contain the prelude. <c>smithy ast</c> has no flag to include it - the
/// <c>--include-prelude</c> option that plan documents once named does not exist on that command -
/// and it is better this way: a model that carried the prelude as shapes would generate a record for
/// every one of them.
/// </para>
/// <para>
/// So the prelude is a table, and it maps onto the same <c>(type, format)</c> vocabulary the IR
/// already uses, because <see cref="Hardened.Generation.TypeMapper"/> is what turns that pair into a C#
/// type. Nothing here needs the type mapper to learn Smithy.
/// </para>
/// </remarks>
internal static class SmithyPrelude {

    internal const string Namespace = "smithy.api";

    /// <summary>The shape meaning "no value" - an operation with no input, an enum member's target.</summary>
    internal const string Unit = "smithy.api#Unit";

    /// <summary>
    /// What a prelude shape is, as the IR spells types.
    /// </summary>
    /// <remarks>
    /// <c>Byte</c> and <c>Short</c> widen to <c>int</c> rather than mapping to <c>sbyte</c> and
    /// <c>short</c>. Both are lossless over JSON numbers, and the narrow types buy nothing a
    /// range constraint does not already say - where <c>@range</c> is declared it becomes an
    /// attribute, and where it is not, a narrower C# type would reject payloads the model calls
    /// valid.
    /// </remarks>
    internal static bool TryMap(string shapeId, out string? type, out string? format) {
        switch (shapeId) {
            case "smithy.api#Blob":
                type = "string"; format = "byte"; return true;

            case "smithy.api#Boolean":
            case "smithy.api#PrimitiveBoolean":
                type = "boolean"; format = null; return true;

            case "smithy.api#String":
                type = "string"; format = null; return true;

            case "smithy.api#Byte":
            case "smithy.api#PrimitiveByte":
            case "smithy.api#Short":
            case "smithy.api#PrimitiveShort":
            case "smithy.api#Integer":
            case "smithy.api#PrimitiveInteger":
                type = "integer"; format = null; return true;

            case "smithy.api#Long":
            case "smithy.api#PrimitiveLong":
                type = "integer"; format = "int64"; return true;

            case "smithy.api#Float":
            case "smithy.api#PrimitiveFloat":
                type = "number"; format = "float"; return true;

            case "smithy.api#Double":
            case "smithy.api#PrimitiveDouble":
                type = "number"; format = "double"; return true;

            // Neither has a C# type that holds every value the model calls valid, so both are
            // mapped to the closest one that does and reported - silently narrowing is the failure
            // worth naming.
            case "smithy.api#BigInteger":
                type = "integer"; format = "int64"; return true;

            // decimal rather than double. Both narrow, and they narrow differently: double loses
            // the value's exactness at any magnitude, so 19.99 is not 19.99 and money stops
            // adding up, while decimal is exact and runs out at 28 significant digits. A model
            // reaching for BigDecimal has almost always reached for exactness rather than for
            // range, and that is the half decimal keeps.
            case "smithy.api#BigDecimal":
                type = "number"; format = "decimal"; return true;

            // RFC 3339 by default, which is what DateTimeOffset holds. @timestampFormat can say
            // otherwise and is read by the parser, not here.
            case "smithy.api#Timestamp":
                type = "string"; format = "date-time"; return true;

            // Arbitrary JSON, which is exactly JsonElement - the type mapper's fallback for a pair
            // it does not recognise.
            case "smithy.api#Document":
                type = null; format = null; return true;

            default:
                type = null; format = null; return false;
        }
    }

    /// <summary>Whether the shape loses precision on the way to C#, for a diagnostic.</summary>
    internal static bool IsLossy(string shapeId) =>
        shapeId is "smithy.api#BigInteger" or "smithy.api#BigDecimal";

    /// <summary>
    /// What the narrowing costs, for the message. Null where the shape narrows nothing.
    /// </summary>
    /// <remarks>
    /// Names the C# type and the limit rather than saying "no exact type", because the two shapes
    /// lose different things and an author's next move differs: a BigDecimal that needed more than
    /// 28 digits has nowhere to go, and one that needed exactness has arrived.
    /// </remarks>
    internal static string? LossDescription(string shapeId) => shapeId switch {
        "smithy.api#BigInteger" =>
            "becomes long, so a value outside 64 bits does not round-trip",
        "smithy.api#BigDecimal" =>
            "becomes decimal, which is exact but holds 28 significant digits rather than " +
            "arbitrarily many",
        _ => null
    };

    /// <summary>Whether the id names a prelude shape at all.</summary>
    internal static bool IsPrelude(string shapeId) =>
        shapeId.StartsWith(Namespace + "#", StringComparison.Ordinal);

    /// <summary>
    /// The local name of a shape id - <c>com.example#Pet</c> becomes <c>Pet</c>.
    /// </summary>
    /// <remarks>
    /// Normalised at the reader so the IR never holds a shape id. <c>TypeMapper.GetRefName</c> and
    /// every pass that rewrites a reference keep working on names, which is what lets the spine stay
    /// shared - and its own doc comment names this case as the reason it is tolerant of the form a
    /// reference arrived in.
    /// </remarks>
    internal static string LocalName(string shapeId) {
        var hash = shapeId.LastIndexOf('#');

        return hash >= 0 ? shapeId.Substring(hash + 1) : shapeId;
    }
}
