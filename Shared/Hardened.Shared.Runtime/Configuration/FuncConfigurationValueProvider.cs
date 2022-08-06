using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration
{
    public class FuncConfigurationValueProvider<TInterface, TImpl> : IConfigurationValueProvider where TImpl : class
    {
        private readonly Func<IEnvironment, TImpl> _provider;

        public FuncConfigurationValueProvider(Func<IEnvironment, TImpl> provider)
        {
            _provider = provider;
        }

        public object ProvideValue(IEnvironment environment, Action<IEnvironment, object> amender)
        {
            var tValue = _provider(environment);

            amender(environment, tValue);

            return tValue;
        }

        public Type InterfaceType => typeof(TInterface);

        public Type ImplementationType => typeof(TImpl);
    }
}
