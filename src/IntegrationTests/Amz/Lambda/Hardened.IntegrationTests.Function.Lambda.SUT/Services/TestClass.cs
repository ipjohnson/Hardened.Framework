using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Function.Lambda.SUT.Services;

public interface ITestClass
{

}
[Expose]
[ForEnvironment("Test")]
public class TestClass : ITestClass
{
}