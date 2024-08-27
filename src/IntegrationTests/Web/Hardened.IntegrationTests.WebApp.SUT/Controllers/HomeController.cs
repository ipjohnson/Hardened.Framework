using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

public class HomeController {
    
    [Get("/")]
    public string HelloWorld() {
        return "Hello World";
    }

    [Post("/hello")]
    public async Task HelloWorldAsync() {
        
    }
}