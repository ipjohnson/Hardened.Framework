namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

public class HomeControllerTests {

    [HardenedTest]
    public async Task Test(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/test");
        
        response.Assert.Ok();

        var value = response.Deserialize<string>();
        
        Assert.Equal("somevalue", value);
    }
}