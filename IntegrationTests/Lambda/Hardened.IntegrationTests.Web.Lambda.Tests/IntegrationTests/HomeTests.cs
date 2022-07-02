using Hardened.IntegrationTests.Web.Lambda.SUT.Models;
using Hardened.IntegrationTests.Web.Lambda.SUT.Services;
using Hardened.Shared.Testing;
using Hardened.Web.Testing;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.Web.Lambda.Tests.IntegrationTests
{
    public class HomeTests
    {
        [Theory]
        [WebIntegration]
        public async Task HomeTest(ITestWebApp app, 
            [Mock] IMathService mathService)
        {
            mathService.Add(2, 2).Returns(5);

            var response = await app.Get("/home");
            
            response.Assert.Ok();

            var homeModel = response.Deserialize<HomeModel>();

            Assert.Equal(10, homeModel.Id);
            Assert.Equal("Blah 5", homeModel.Name);
        }
    }
}
