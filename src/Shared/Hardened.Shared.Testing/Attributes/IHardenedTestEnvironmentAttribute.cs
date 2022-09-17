using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Testing.Attributes
{
    public interface IHardenedTestEnvironmentAttribute
    {
        void ConfigureEnvironment(AttributeCollection attributeCollection, 
            MethodInfo methodInfo, string environmentName, IDictionary<string, object> environment);
    }
}
