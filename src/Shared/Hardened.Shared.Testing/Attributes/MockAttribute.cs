using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Testing.Attributes;

[AttributeUsage(AttributeTargets.Parameter)]
public class MockAttribute : Attribute, ITestParameterValueProvider {
    public void SetupServiceCollection(ITestMethodContext testCaseContext, IServiceCollection serviceCollection,
        ParameterInfo parameter) {
        var mock = NSubstitute.Substitute.For(new[] { parameter.ParameterType }, Array.Empty<object>());
        serviceCollection.AddSingleton(parameter.ParameterType, _ => mock);
    }

    public Task<object?> GetParameterValueAsync(ITestMethodContext context, IServiceProvider serviceProvider,
        ParameterInfo parameter) {
        return Task.FromResult(serviceProvider.GetService(parameter.ParameterType));
    }
}
