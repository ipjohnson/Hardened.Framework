using Hardened.IntegrationTests.WebApp.SUT.Filters;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

public class HomeController {
    
    [Get("/")]
    public string HelloWorld() {
        return "Hello World";
    }

    [Post("/hello")]
    public Task HelloWorldAsync() {
        return Task.CompletedTask;
    }

    [Get("/test")]
    public Task<string> TestValue([TestFilter("somevalue")] string testValue) {
        return Task.FromResult(testValue);
    }
}