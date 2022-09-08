using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration
{
    public interface IConfigurationPackage
    {
        IEnumerable<IConfigurationValueProvider> ConfigurationValueProviders(IEnvironment env);

        IEnumerable<IConfigurationValueAmender> ConfigurationValueAmenders(IEnvironment env);
    }
}
