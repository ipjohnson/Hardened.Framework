using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration
{
    public class AppConfig : IAppConfig, IConfigurationPackage
    {
        private List<IConfigurationValueProvider> _providers = new();
        private List<(IConfigurationValueAmender amend,string env)> _amenders = new();

        public IAppConfig ProvideValue<TInterface, TImpl>(Func<IEnvironment, TImpl> valueProvider) where TImpl : class, TInterface
        {
            _providers.Add(new FuncConfigurationValueProvider<TInterface, TImpl>(valueProvider));

            return this;
        }

        public IAppConfig Amend<TImpl>(Action<TImpl> amendAction, string environment = "") where TImpl : class
        {
            var amender = new SimpleConfigurationValueAmender<TImpl>(((_, impl) =>
            {
                amendAction(impl);
                return impl;
            }));

            _amenders.Add((amender, environment));

            return this;
        }

        public IAppConfig Amend<TImpl>(Func<IEnvironment, TImpl, TImpl> amendFunc) where TImpl : class
        {
            _amenders.Add((new SimpleConfigurationValueAmender<TImpl>(amendFunc), ""));

            return this;
        }

        IEnumerable<IConfigurationValueProvider> IConfigurationPackage.ConfigurationValueProviders(IEnvironment env)
        {
            return _providers;
        }

        IEnumerable<IConfigurationValueAmender> IConfigurationPackage.ConfigurationValueAmenders(IEnvironment env)
        {
            foreach (var tuple in _amenders)
            {
                if (string.IsNullOrEmpty(tuple.env) || tuple.env == env.Name)
                {
                    yield return tuple.Item1;
                }
            }
        }
    }
}
