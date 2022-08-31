using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Application
{
    public interface IApplicationModule
    {
        void ConfigureModule(IEnvironment environment, IServiceCollection serviceCollection);
    }
}
