namespace Hardened.Idl.Models;

/// <summary>
/// The constraint keywords a schema can carry, wherever it was declared.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PropertyModel"/> and <see cref="ParameterModel"/> hold an identical set of these and
/// share nothing else, so the translation to attributes used to take them apart into a dozen loose
/// arguments and both callers passed every one by name. Adding a keyword meant a parameter, two
/// call-site lines, and the code that did the work - and forgetting one of the call-site lines
/// compiled silently, leaving the constraint enforced on request bodies and ignored on query
/// strings.
/// </para>
/// <para>
/// Requiredness is deliberately not here. A parameter's comes from its own model, but a property's
/// comes from its caller, which also knows whether the C# type makes <c>[Required]</c> unfailable -
/// a non-nullable value type can never be absent, and constraining one is VM0004.
/// </para>
/// </remarks>
internal interface IConstraintFacets {
    int? MinLength { get; }

    int? MaxLength { get; }

    decimal? Minimum { get; }

    decimal? Maximum { get; }

    bool ExclusiveMinimum { get; }

    bool ExclusiveMaximum { get; }

    string? Pattern { get; }

    int? MinItems { get; }

    int? MaxItems { get; }

    List<string>? EnumValues { get; }
}
