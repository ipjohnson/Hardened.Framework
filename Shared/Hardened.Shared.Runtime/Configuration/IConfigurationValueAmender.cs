using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration
{
    public interface IConfigurationValueAmender
    {
        object ApplyConfiguration(IEnvironment environment, object configurationValue);
    }
    
}
