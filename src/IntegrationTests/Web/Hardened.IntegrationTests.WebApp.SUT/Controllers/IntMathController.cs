using Hardened.IntegrationTests.WebApp.SUT.Models;
using Hardened.IntegrationTests.WebApp.SUT.Services;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

public class IntMathController {
    
    [Post("/int/add")]
    public int Add(IMathService<int> mathService, MathAddModel model) {
        return mathService.Add(model.Values?.ToArray() ?? Array.Empty<int>());
    }
    
    
    [Post("/int/subtract")]
    public int Subtract(IMathService<int> mathService, MathAddModel model) {
        return mathService.Add(model.Values?.ToArray() ?? Array.Empty<int>());
    }
    
    
    [Post("/int/subtract-test")]
    public int SubtractTest(IMathService<int> mathService, MathAddModel model) {
        return mathService.Add(model.Values?.ToArray() ?? Array.Empty<int>());
    }
    
    
    [Post("/int/subtract-test1")]
    public int SubtractTest1(IMathService<int> mathService, MathAddModel model) {
        return mathService.Add(model.Values?.ToArray() ?? Array.Empty<int>());
    }
    
    
    [Post("/int/subtract-test2")]
    public int SubtractTest2(IMathService<int> mathService, MathAddModel model) {
        return mathService.Add(model.Values?.ToArray() ?? Array.Empty<int>());
    }
}