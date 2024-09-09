using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Hardened.Shared.Testing.Attributes;

public interface IHardenedTestDependencyRegistrationAttribute : IHardenedOrderedAttribute {
    void RegisterDependencies(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        IHardenedEnvironment environment,
        IServiceCollection serviceCollection);
}