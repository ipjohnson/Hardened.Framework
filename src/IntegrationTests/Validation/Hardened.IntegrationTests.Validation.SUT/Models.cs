using ValidationModules;
using ValidationModules.Constraints;

namespace Hardened.IntegrationTests.Validation.SUT;

/// <summary>
/// Constraints declared as attributes, which is the vocabulary both generators read.
/// </summary>
public class Order
{
    [Required]
    [StringLength(Min = 3, Max = 20)]
    public string Reference { get; init; } = "";

    [Range(1, 500)]
    public int Quantity { get; init; }
}

/// <summary>
/// The same shape declared in <c>System.ComponentModel.DataAnnotations</c> instead.
/// </summary>
/// <remarks>
/// Worth its own type rather than mixing the two vocabularies on one: the question is whether
/// DataAnnotations alone produces a registered validator, not whether it works once something else
/// has already triggered one.
/// </remarks>
public class Delivery
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(20, MinimumLength = 3)]
    public string Address { get; init; } = "";
}

/// <summary>
/// A model whose rules are declared in a rules class rather than on its members.
/// </summary>
/// <remarks>
/// The half <c>Hardened.Validation.SourceGenerator</c> never reached.
/// <c>HardenedValidationGenerator</c> constructs <c>AttributeFrontEnd</c> and nothing else, so a
/// rules class produced no validator and no diagnostic; <c>RulesFrontEnd</c> was compiled into the
/// same assembly and never called. ValidationModules' generator reads both, which is the second
/// reason this project takes it.
/// </remarks>
public class Shipment
{
    public string? Carrier { get; init; }

    public int Weight { get; init; }
}

public class ShipmentRules : IValidationRulesFor<Shipment>
{
    public static void Describe(ValidationRules<Shipment> rules, Shipment x)
    {
        rules.Require(x.Carrier);
        rules.Range(x.Weight, 1, 100);
    }
}
