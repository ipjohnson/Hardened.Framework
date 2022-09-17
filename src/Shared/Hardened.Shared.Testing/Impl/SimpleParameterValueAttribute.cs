
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Testing.Impl
{
    public class SimpleParameterValueAttribute : Attribute, IHardenedParameterProviderAttribute
    {
        private readonly object _value;

        public SimpleParameterValueAttribute(object value)
        {
            _value = value;
        }

        public void RegisterDependencies(AttributeCollection attributeCollection, MethodInfo methodInfo, ParameterInfo? parameterInfo,
            IEnvironment environment, IServiceCollection serviceCollection)
        {

        }

        public object? ProvideParameterValue(ParameterInfo parameterInfo, IApplicationRoot applicationRoot)
        {
            return _value;
        }
    }
}
