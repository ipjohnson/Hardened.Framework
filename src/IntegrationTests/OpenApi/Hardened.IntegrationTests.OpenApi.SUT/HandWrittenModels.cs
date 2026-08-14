
namespace Hardened.IntegrationTests.OpenApi.SUT;

/// <summary>
/// Models nobody generated, carrying constraints from both attribute vocabularies.
/// </summary>
/// <remarks>
/// These exist to prove the claim the whole arrangement rests on: that a model a developer wrote and
/// a model the OpenAPI task emitted are the same thing to
/// <c>Hardened.Validation.SourceGenerator</c>. It scans the compilation for constraint attributes;
/// nothing about these types is special, and nothing had to be wired for them.
/// </remarks>
public class HandWrittenOrder {
    [ValidationModules.Constraints.Required]
    [ValidationModules.Constraints.StringLength(Min = 3, Max = 20)]
    public string Reference { get; init; } = "";

    [ValidationModules.Constraints.Range(1, 500)]
    public int Quantity { get; init; }
}

/// <summary>
/// The same shape declared with System.ComponentModel.DataAnnotations instead.
/// </summary>
/// <remarks>
/// ValidationModules reads both vocabularies through one front-end, so these reach the same
/// generated code as the constraints above. Worth having its own type rather than mixing the two on
/// one, because the interesting question is whether DataAnnotations alone is enough to produce a
/// validator - not whether it works when something else already triggered one.
/// </remarks>
public class DataAnnotatedOrder {
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(20, MinimumLength = 3)]
    public string Reference { get; init; } = "";

    [System.ComponentModel.DataAnnotations.Range(1, 500)]
    public int Quantity { get; init; }
}
