using System.Reflection;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Testing.Attributes;

[AttributeUsage(AttributeTargets.Parameter)]
public class MockAttribute : Attribute, IHardenedParameterProviderAttribute {
    private object? _parameterValue;

    public void RegisterDependencies(AttributeCollection attributeCollection, MethodInfo methodInfo,
        ParameterInfo? parameterInfo, IEnvironment environment,
        IServiceCollection serviceCollection) {
        if (parameterInfo != null) {
            var mock = NSubstitute.Substitute.For(new[] { parameterInfo.ParameterType }, Array.Empty<object>());

            serviceCollection.AddSingleton(parameterInfo.ParameterType, mock);
        }
    }

    public object? ProvideParameterValue(MethodInfo methodInfo, ParameterInfo parameterInfo,
        IApplicationRoot applicationRoot) {
        return _parameterValue;
    }
}