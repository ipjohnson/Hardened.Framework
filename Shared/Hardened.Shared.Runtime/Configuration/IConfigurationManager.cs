using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Configuration
{
    public interface IConfigurationManager
    {
        T GetConfiguration<T>() where T : class;
    }
}
