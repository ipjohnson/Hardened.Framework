namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

public class SomeControllerTests {

    [HardenedTest]
    public async Task Test(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/web-library/string-methods/concat/hello/world");
        
        response.Assert.Ok();

        var value = response.Deserialize<string>();
        
        Assert.Equal("hello-world", value);
    }
}