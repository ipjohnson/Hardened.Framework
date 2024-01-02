using Hardened.IntegrationTests.WebApp.SUT.Services;
using NSubstitute;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

public class MathControllerTests {

    [HardenedTest]
    public async Task MathIntAdd(ITestWebApp testWebApp) {
        var model = new MathAddModel { Values = new List<int> { 10, 20, 30 } };
        var response = await testWebApp.Post(model, "/int/add");
        
        response.Assert.Ok();

        var value = response.Deserialize<int>();
        
        Assert.Equal(60, value);
    }
    
    
    [HardenedTest]
    public async Task MathIntAddMock(
        ITestWebApp testWebApp, [Mock]IMathService<int> mockService) {
        mockService.Add(Arg.Any<int[]>()).Returns(100);
        
        var model = new MathAddModel { Values = new List<int> { 10, 20, 30 } };
        var response = await testWebApp.Post(model, "/int/add");
        
        response.Assert.Ok();

        var value = response.Deserialize<int>();
        
        Assert.Equal(100, value);
    }
}