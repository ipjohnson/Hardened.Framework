using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Hardened.Shared.Testing.Attributes
{
    public interface IHardenedParameterProviderAttribute
    {
        void RegisterDependencies(
            AttributeCollection attributeCollection,
            MethodInfo methodInfo, 
            ParameterInfo? parameterInfo, 
            IEnvironment environment, 
            IServiceCollection serviceCollection);

        object? ProvideParameterValue(
            ParameterInfo parameterInfo, IApplicationRoot applicationRoot);
    }
}
