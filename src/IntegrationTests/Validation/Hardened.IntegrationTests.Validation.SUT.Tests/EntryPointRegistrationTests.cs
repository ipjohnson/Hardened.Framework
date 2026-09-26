using Hardened.IntegrationTests.Validation.SUT;
using ValidationModules;

namespace Hardened.IntegrationTests.Validation.SUT.Tests;

/// <summary>
/// Validators emitted by <c>ValidationModules.SourceGenerator</c>, resolved from the application's
/// own container.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in the SUT wires these up. There is no registration call, no module composed, and no
/// attribute pointing at a model - only <c>[HardenedModule]</c> on the entry point and ordinary
/// types carrying ordinary constraints. The validator arrives as a test parameter, resolved from
/// the container the entry point builds, because being registered is half of what is under test: a
/// validator that exists and is not registered is a validator that never runs.
/// </para>
/// <para>
/// This is the end ValidationModules' own integration tests cannot hold. They declare
/// <c>HardenedModuleAttribute</c> themselves, since Hardened depends on ValidationModules and not
/// the other way round; here the attribute is the real one from
/// <c>Hardened.Shared.Runtime</c>, and the module around it is the one
/// <c>HardenedSourceGenerator</c> emits.
/// </para>
/// </remarks>
public class EntryPointRegistrationTests
{
    [ModuleTest]
    public void AModelWithConstraintsGetsARegisteredValidator(IValidatorFor<Order> validator)
    {
        Assert.NotNull(validator);
    }

    [ModuleTest]
    public void ItReportsTheConstraintsThatFailed(IValidatorFor<Order> validator)
    {
        var result = validator.Validate(new Order { Reference = "ab", Quantity = 900 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "reference" && e.Code == "string_length");
        Assert.Contains(result.Errors, e => e.Field == "quantity" && e.Code == "range");
    }

    [ModuleTest]
    public void ItPassesWhatSatisfiesThem(IValidatorFor<Order> validator)
    {
        Assert.True(validator.Validate(new Order { Reference = "abc", Quantity = 5 }).IsValid);
    }

    [ModuleTest]
    public void DataAnnotationsAloneProduceARegisteredValidator(IValidatorFor<Delivery> validator)
    {
        var result = validator.Validate(new Delivery { Address = "ab" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "address");
    }

    /// <summary>
    /// A model with no constraint attributes, whose rules come from a rules class.
    /// </summary>
    [ModuleTest]
    public void ARulesClassProducesARegisteredValidator(IValidatorFor<Shipment> validator)
    {
        var result = validator.Validate(new Shipment { Carrier = null, Weight = 500 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "carrier" && e.Code == "required");
        Assert.Contains(result.Errors, e => e.Field == "weight" && e.Code == "range");
    }

    /// <summary>
    /// The runner, so the registration is the whole extension rather than the validators alone.
    /// </summary>
    [ModuleTest]
    public void TheValidationRunnerIsRegisteredToo(ValidationRunner<Order> runner)
    {
        Assert.True(runner.Validate(new Order { Reference = "abc", Quantity = 5 }).IsValid);
    }
}
