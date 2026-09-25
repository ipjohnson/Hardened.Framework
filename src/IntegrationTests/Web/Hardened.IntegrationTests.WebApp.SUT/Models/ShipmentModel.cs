using ValidationModules;
using ValidationModules.Constraints;

namespace Hardened.IntegrationTests.WebApp.SUT.Models;

/// <summary>
/// A body model with no constraint attribute on it. Its rules are in <see cref="ShipmentRules"/>.
/// </summary>
public sealed class ShipmentModel
{
    public string? Carrier { get; set; }

    public int Weight { get; set; }
}

/// <summary>The rules for <see cref="ShipmentModel"/>, kept off the model.</summary>
public class ShipmentRules : IValidationRulesFor<ShipmentModel>
{
    public static void Describe(ValidationRules<ShipmentModel> rules, ShipmentModel x)
    {
        rules.Require(x.Carrier);
        rules.Range(x.Weight, 1, 100);
    }
}

/// <summary>
/// An attributed model that descends into <see cref="ShipmentModel"/>, whose validator comes from
/// a rules class.
/// </summary>
public class ConsignmentModel
{
    [Required]
    public string? Reference { get; set; }

    [ValidateNested]
    public ShipmentModel? Shipment { get; set; }
}
