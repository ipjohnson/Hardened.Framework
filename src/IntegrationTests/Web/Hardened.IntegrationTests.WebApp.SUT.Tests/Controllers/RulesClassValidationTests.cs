using Hardened.Requests.Runtime.Validation;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// A body whose rules live in a rules class, end to end.
/// </summary>
/// <remarks>
/// <c>ShipmentModel</c> carries no constraint attribute, and nothing in <c>RegistrationController</c>
/// mentions validation. The validator comes from <c>ShipmentRules</c>, so the handler has to learn
/// that the model has one from the rules class alone.
/// </remarks>
public class RulesClassValidationTests
{
    [ModuleTest]
    public async Task ABodyBreakingItsRulesIsRefused(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post("""{"weight":500}""", "/registration/shipment");

        response.Assert.BadRequest();

        var errors = response.Deserialize<RequestValidationError>().Errors;

        Assert.Contains(errors, e => e.Field == "shipment.carrier" && e.Code == "required");
        Assert.Contains(errors, e => e.Field == "shipment.weight" && e.Code == "range");
    }

    [ModuleTest]
    public async Task ABodyKeepingItsRulesIsAccepted(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post(
            """{"carrier":"post","weight":5}""",
            "/registration/shipment"
        );

        response.Assert.Ok();
        Assert.Equal("post", response.Deserialize<string>());
    }

    [ModuleTest]
    public async Task ANestedBodyIsHeldToItsRulesClass(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post(
            """{"reference":"c-1","shipment":{"carrier":"post","weight":500}}""",
            "/registration/consignment"
        );

        response.Assert.BadRequest();

        var error = Assert.Single(response.Deserialize<RequestValidationError>().Errors);

        Assert.Equal("consignment.shipment.weight", error.Field);
        Assert.Equal("range", error.Code);
    }
}
