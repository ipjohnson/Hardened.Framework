using Hardened.IntegrationTests.Web.Lambda.SUT.Controllers;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Hardened.IntegrationTests.Web.Lambda.Tests.IntegrationTests;

public class RoutingTests
{
    private const string CompanyName = "company-name";
    
    [HardenedTest]
    public async Task CompanyGetAllTest(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Get("/company");
        
        response.Assert.Ok();
        
        var deserializedResponse = response.Deserialize<RoutingTestController.Response>();
        
        Assert.Equal(nameof(RoutingTestController.CompanyGetAll), deserializedResponse.Method);
    }

    [HardenedTest]
    public async Task CompanyIdTest(ITestWebApp testWebApp)
    {
        var webResponse = await testWebApp.Get($"/company/{CompanyName}");
        
        webResponse.Assert.Ok();
        var response = webResponse.Deserialize<RoutingTestController.Response>();
        Assert.Equal(CompanyName, response.Company);
        Assert.Equal(nameof(RoutingTestController.CompanyGet), response.Method);
    }

    [HardenedTest]
    public async Task CompanySubscriptionTest(ITestWebApp testWebApp)
    {
        var webResponse = await testWebApp.Get($"/company/{CompanyName}/subscription");
        
        webResponse.Assert.Ok();
        var response = webResponse.Deserialize<RoutingTestController.Response>();
        Assert.Equal(CompanyName, response.Company);
        Assert.Equal(nameof(RoutingTestController.CompanyGetSubscription), response.Method);
    }

    [HardenedTest]
    public async Task CompanySubscriptionIDTest(ITestWebApp testWebApp)
    {
        var webResponse = await testWebApp.Get($"/company/{CompanyName}/subscription/some-id");
        
        webResponse.Assert.Ok();
        var response = webResponse.Deserialize<RoutingTestController.Response>();
        Assert.Equal(CompanyName, response.Company);
        Assert.Equal(nameof(RoutingTestController.CompanyGetSubscriptionWithQuery), response.Method);
        Assert.Equal("some-id", response.Id);
        Assert.Equal("unknown", response.QueryParam);
    }
    
    [HardenedTest]
    public async Task CompanySubscriptionIDWithQueryTest(ITestWebApp testWebApp)
    {
        var webResponse = await testWebApp.Get($"/company/{CompanyName}/subscription/some-id?queryParam=testing");
        
        webResponse.Assert.Ok();
        var response = webResponse.Deserialize<RoutingTestController.Response>();
        Assert.Equal(CompanyName, response.Company);
        Assert.Equal(nameof(RoutingTestController.CompanyGetSubscriptionWithQuery), response.Method);
        Assert.Equal("some-id", response.Id);
        Assert.Equal("testing", response.QueryParam);
    }
    
    [HardenedTest]
    public async Task CompaniesTest(ITestWebApp testWebApp)
    {
        var webResponse = await testWebApp.Get($"/companies/{CompanyName}/some-id");
        
        webResponse.Assert.Ok();
        var response = webResponse.Deserialize<RoutingTestController.Response>();
        Assert.Equal(CompanyName, response.Company);
        Assert.Equal(nameof(RoutingTestController.Companies), response.Method);
        Assert.Equal("some-id", response.Id);
        Assert.Null(response.QueryParam);
    }
}