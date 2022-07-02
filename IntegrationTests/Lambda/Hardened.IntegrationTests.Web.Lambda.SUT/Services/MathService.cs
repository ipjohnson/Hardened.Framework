using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Services
{
    public interface IMathService
    {
        int Add(int x, int y);
    }

    [Expose]
    public class MathService : IMathService
    {
        public int Add(int x, int y)
        {
            return x + y;
        }
    }
}
