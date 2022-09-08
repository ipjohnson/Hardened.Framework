using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Function.Lambda.Runtime.Impl;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Function.Lambda.Runtime.DependencyInjection
{
    public static class LambdaFunctionDI
    {
        public static void Register(IEnvironment environment, IServiceCollection serviceCollection)
        {
            serviceCollection.TryAddSingleton<ILambdaFunctionImplService, LambdaFunctionImplService>();
        }
    }
}
