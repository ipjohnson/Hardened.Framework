using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Testing.Attributes
{
    public interface IHardenedTestStartupAttribute
    {
        Task Startup(AttributeCollection attributeCollection, MethodInfo methodInfo, IEnvironment environment, IServiceProvider serviceProvider);
    }
}
