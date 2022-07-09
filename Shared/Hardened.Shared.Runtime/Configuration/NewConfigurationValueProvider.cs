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
        public Type ProvidedType => typeof(TInterface);

        public TV ProvideValue<TV>(IEnvironment environment, IConfigurationValueAmender amender) where TV : class
        {
            return amender.ApplyConfiguration(environment, (TV)(object)new TImpl());
        }
    }
}
