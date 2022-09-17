using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Testing.Attributes
{
    [AttributeUsage(AttributeTargets.Parameter)]
    public class MockAttribute : Attribute, IHardenedParameterProviderAttribute
    {
        private object? _parameterValue;

        public void RegisterDependencies(AttributeCollection attributeCollection, MethodInfo methodInfo,
            ParameterInfo? parameterInfo, IEnvironment environment,
            IServiceCollection serviceCollection)
        {
            if (parameterInfo != null)
            {
                var mock = NSubstitute.Substitute.For(new[] { parameterInfo.ParameterType }, Array.Empty<object>());

                serviceCollection.AddSingleton(parameterInfo.ParameterType, mock);
            }
        }

        public object? ProvideParameterValue(ParameterInfo parameterInfo, IApplicationRoot applicationRoot)
        {
            return _parameterValue;
        }
    }
}
