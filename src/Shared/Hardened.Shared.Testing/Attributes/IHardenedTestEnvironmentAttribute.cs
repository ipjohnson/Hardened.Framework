using System.Reflection;

namespace Hardened.Shared.Testing.Attributes;

public interface IHardenedTestEnvironmentAttribute
{
    void ConfigureEnvironment(AttributeCollection attributeCollection, 
        MethodInfo methodInfo, string environmentName, IDictionary<string, object> environment);
}