using Hardened.Web.Runtime.Attributes;

namespace LambdaWebTest.Controller;

public class GetMethod {
    
    [Get("/{author}/{name}")]
    public async Task<object> Get(string author, string name) {
        return new {};
    }
}