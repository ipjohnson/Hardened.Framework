using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Configuration
{
    public interface IConfigurationPackage
    {
        IEnumerable<IConfigurationValueProvider> ConfigurationValueProviders();

        IEnumerable<IConfigurationValueAmender> Amenders();
    }
}
