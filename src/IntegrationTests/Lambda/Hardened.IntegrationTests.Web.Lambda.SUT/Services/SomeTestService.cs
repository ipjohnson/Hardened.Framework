using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Services
{
    public interface ISomeTestService
    {
        string TestMethod();
    }

    [Expose]
    internal class SomeTestService : ISomeTestService
    {
        public string TestMethod()
        {
            return "test string";
        }
    }
}
