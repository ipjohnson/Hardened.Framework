using Hardened.IntegrationTests.Web.Lambda.SUT.Filters;
using Hardened.IntegrationTests.Web.Lambda.SUT.Models;
using Hardened.IntegrationTests.Web.Lambda.SUT.Services;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Controllers;

[TestingFilter(TestValue = 10, OtherValue = 50)]
public class HomeController
{

    [Get("/Home")]
    public HomeModel GetMethod(IMathService mathService) =>  
        new() { Id = 10, Name = "Blah Test " + mathService.Add(2,2)};

    [Get("/Header")]
    public HomeModel HeaderTest([FromHeader] string headerString) => new() { Id = 20, Name = headerString };
        
    [Get("/QueryStringPath")]
    public HomeModel QueryStringTest([FromQueryString] string testString) => new() { Id = 30, Name = testString };

}