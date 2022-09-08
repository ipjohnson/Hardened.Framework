using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration
{
    public class NewConfigurationValueProvider<TInterface, TImpl> : IConfigurationValueProvider where TImpl : class, TInterface, new()
    {
        public object ProvideValue(IEnvironment environment, Action<IEnvironment, object> amender)
        {
            var tValue = new TImpl();

            amender(environment, tValue);

            return tValue;
        }

        public Type InterfaceType => typeof(TInterface);

        public Type ImplementationType => typeof(TImpl);
    }
}
