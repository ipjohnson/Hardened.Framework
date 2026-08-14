using Hardened.IntegrationTests.OpenApi.SUT;
using ValidationModules;

namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// Validation for models nobody generated.
/// </summary>
/// <remarks>
/// <para>
/// The claim the whole arrangement rests on: that a model a developer wrote reaches the same
/// machinery as one the OpenAPI task emitted. Nothing in this project wires these types up - no
/// attribute pointing at them, no registration call. They are ordinary classes carrying ordinary
/// attributes, in a project that happens to reference the generator.
/// </para>
/// <para>
/// The validator arrives as a test parameter, resolved from the application's container rather than
/// named directly, because being registered is half of what is under test. A validator that exists
/// and is not registered is a validator that never runs.
/// </para>
/// </remarks>
public class HandWrittenModelValidationTests {

    [HardenedTest]
    public void AHandWrittenModelGetsARegisteredValidator(IValidatorFor<HandWrittenOrder> validator) {
        Assert.NotNull(validator);
    }

    [HardenedTest]
    public void ItReportsTheConstraintsThatFailed(IValidatorFor<HandWrittenOrder> validator) {
        var result = validator.Validate(new HandWrittenOrder { Reference = "ab", Quantity = 900 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "reference" && e.Code == "string_length");
        Assert.Contains(result.Errors, e => e.Field == "quantity" && e.Code == "range");
    }

    [HardenedTest]
    public void ItPassesWhatSatisfiesThem(IValidatorFor<HandWrittenOrder> validator) {
        Assert.True(validator.Validate(new HandWrittenOrder { Reference = "abc", Quantity = 5 }).IsValid);
    }

    /// <summary>
    /// DataAnnotations on their own, with no ValidationModules attribute anywhere on the type.
    /// </summary>
    /// <remarks>
    /// The interesting case is that this alone is enough to produce a validator - not that it works
    /// once something else has already triggered one.
    /// </remarks>
    [HardenedTest]
    public void DataAnnotationsAloneProduceAValidator(IValidatorFor<DataAnnotatedOrder> validator) {
        var result = validator.Validate(new DataAnnotatedOrder { Reference = "ab", Quantity = 900 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "reference");
        Assert.Contains(result.Errors, e => e.Field == "quantity");
    }

    /// <summary>
    /// Field names are wire names. A property called <c>Reference</c> reports as <c>reference</c>,
    /// which is what a caller sent and what they can act on.
    /// </summary>
    [HardenedTest]
    public void ErrorsCarryWireNamesRatherThanClrNames(IValidatorFor<HandWrittenOrder> validator) {
        var result = validator.Validate(new HandWrittenOrder { Reference = "", Quantity = 5 });

        Assert.Contains(result.Errors, e => e.Field == "reference");
        Assert.DoesNotContain(result.Errors, e => e.Field == "Reference");
    }
}
