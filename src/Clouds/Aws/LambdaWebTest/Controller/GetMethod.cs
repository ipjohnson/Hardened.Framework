using Hardened.Web.Runtime.Attributes;

namespace LambdaWebTest.Controller;

public class GetMethod {
    
    [Get("/{author}/{name}")]
    public Task<object> Get(string author, string name) {
        return Task.FromResult<object>(new {});
    }
}