using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Testing
{
    public interface ITestExposeAttribute
    {
        void ExposeDependencies(MethodInfo method, IServiceCollection services);
    }
}
