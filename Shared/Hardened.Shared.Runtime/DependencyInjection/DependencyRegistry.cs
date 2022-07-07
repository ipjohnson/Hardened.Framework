using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Runtime.DependencyInjection
{
    public class DependencyRegistry<T>
    {
        private static readonly List<DependencyRegistrationFunc> _registrations = new ();

        public static int Register(DependencyRegistrationFunc func)
        {
            _registrations.Add(func);

            return 1;
        }

        public static void ApplyRegistration(IServiceCollection serviceCollection, T entryPoint)
        {
            foreach (var registrationFunc in _registrations)
            {
                registrationFunc(serviceCollection, entryPoint);
            }
        }

        public delegate void DependencyRegistrationFunc(IServiceCollection serviceCollection, T entryPoint);
    }
}
