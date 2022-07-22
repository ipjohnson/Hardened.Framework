using Hardened.IntegrationTests.Web.Lambda.SUT.Filters;
using Hardened.IntegrationTests.Web.Lambda.SUT.Models;
using Hardened.IntegrationTests.Web.Lambda.SUT.Services;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Controllers
{
    public class HomeController
    {
        private readonly IMathService _mathService;

        public HomeController(IMathService mathService)
        {
            _mathService = mathService;
        }

        [Get("/Home")]
        [TestingFilter(TestValue = 10, OtherValue = 50)]
        public HomeModel GetMethod() =>  
            new() { Id = 10, Name = "Blah " + _mathService.Add(2,2)};
    }
}
