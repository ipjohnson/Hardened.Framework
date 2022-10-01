using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Web.SUT;

public interface ISomeTestService
{

}

[Expose]
internal class SomeTestService : ISomeTestService
{
}