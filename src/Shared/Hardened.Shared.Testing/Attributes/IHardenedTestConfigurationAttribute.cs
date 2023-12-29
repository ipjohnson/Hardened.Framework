using System.Reflection;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;

namespace Hardened.Shared.Testing.Attributes;

public interface IHardenedTestConfigurationAttribute : IHardenedOrderedAttribute {
    void Configure(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        IEnvironment environment,
        IAppConfig appConfig);
}