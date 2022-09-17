using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Testing.Attributes
{
    public interface IHardenedTestDependencyRegistrationAttribute
    {
        void RegisterDependencies(
            AttributeCollection attributeCollection, 
            MethodInfo methodInfo, 
            IEnvironment environment,
            IServiceCollection serviceCollection);
    }
}
