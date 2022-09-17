using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;

namespace Hardened.Shared.Testing.Attributes
{
    public interface IHardenedTestConfigurationAttribute
    {
        void Configure(
            AttributeCollection attributeCollection,
            MethodInfo methodInfo,
            IEnvironment environment,
            IAppConfig appConfig);
    }
}
