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
        private static readonly ConcurrentDictionary<string, DependencyRegistrationFunc> _registrations = new ();

        public static string Register(DependencyRegistrationFunc func, string token)
        {
            _registrations[token] = func;

            return token;
        }

        public static IEnumerable<DependencyRegistrationFunc> GetAllRegistrationFunc()
        {
            return _registrations.Values;
        }

        public delegate void DependencyRegistrationFunc(IServiceCollection serviceCollection);

    }
}
