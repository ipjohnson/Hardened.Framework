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
        public void SimpleTest(MathService mathService)
        {
            var value = mathService.Add(1, 2);
            Assert.Equal(3, value);
        }

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
            Assert.Equal("Blah Test 5", homeModel.Name);
        }

        [Theory]
        [WebIntegration]
        public async Task HeaderTest(ITestWebApp app)
        {
            var response = await app.Get("/Header", request => request.Headers.Set("headerString", "testing 123"));

            response.Assert.Ok();
            
            var homeModel = response.Deserialize<HomeModel>();

            Assert.Equal(20, homeModel.Id);
            Assert.Equal("testing 123", homeModel.Name);

        }

        [Theory]
        [WebIntegration]
        public async Task QueryStringPathTest(ITestWebApp app)
        {
            var response = await app.Get("/QueryStringPath?testString=SomeValue");

            response.Assert.Ok();

            var homeModel = response.Deserialize<HomeModel>();

            Assert.Equal(30, homeModel.Id);
            Assert.Equal("SomeValue", homeModel.Name);

        }
    }
}