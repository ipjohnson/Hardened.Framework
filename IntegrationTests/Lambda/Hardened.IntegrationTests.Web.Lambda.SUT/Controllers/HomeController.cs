using Hardened.IntegrationTests.Web.Lambda.SUT.Filters;
using Hardened.IntegrationTests.Web.Lambda.SUT.Models;
using Hardened.IntegrationTests.Web.Lambda.SUT.Services;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Controllers
{
    [TestingFilter(TestValue = 10, OtherValue = 50)]
    public class HomeController
    {
        private readonly IMathService _mathService;

        public HomeController(IMathService mathService)
        {
            _mathService = mathService;
        }

        [Get("/Home")]
        public HomeModel GetMethod() =>  
            new() { Id = 10, Name = "Blah Test " + _mathService.Add(2,2)};

        [Get("/Header")]
        public HomeModel HeaderTest([FromHeader] string headerString) => new() { Id = 20, Name = headerString };
    }
}
