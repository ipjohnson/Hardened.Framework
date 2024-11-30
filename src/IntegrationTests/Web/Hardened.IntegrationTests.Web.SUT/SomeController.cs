using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.Web.SUT;

[BasePath("/string-methods")]
public class SomeController {
    [Get("/concat/{a}/{b}")]
    public string Concat(string a, string b) => $"{a}-{b}";
}