using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration
{
    public class ConfigurationManager : IConfigurationManager, IConfigurationValueAmender
    {
        private readonly IEnvironment _environment;
        private readonly Dictionary<Type, IConfigurationValueProvider> _valueProviders;
        private readonly List<IConfigurationValueAmender> _amenders;

        public ConfigurationManager(IEnvironment environment, IEnumerable<IConfigurationPackage> configurationPackages)
        {
            _environment = environment;
            _valueProviders = new Dictionary<Type, IConfigurationValueProvider>();
            _amenders = new List<IConfigurationValueAmender>();

            foreach (var configurationPackage in configurationPackages)
            {
                foreach (var valueProvider in configurationPackage.ConfigurationValueProviders())
                {
                    _valueProviders[valueProvider.ProvidedType] = valueProvider;
                }

                foreach (var amender in configurationPackage.Amenders())
                {
                    _amenders.Add(amender);
                }
            }
        }

        public T GetConfiguration<T>() where T : class
        {
            if (_valueProviders.TryGetValue(typeof(T), out var valueProvider))
            {
                return CreateConfigurationValue<T>(valueProvider);
            }

            throw new Exception($"{typeof(T).Name} is not a registered configuration type");
        }

        private T CreateConfigurationValue<T>(IConfigurationValueProvider valueProvider) where T : class
        {
            return valueProvider.ProvideValue<T>(_environment, this);
        }

        public T ApplyConfiguration<T>(IEnvironment environment, T configurationValue) where T : class
        {
            var value = configurationValue;

            foreach (var amender in _amenders)
            {
                value = amender.ApplyConfiguration(environment, value);
            }

            return value;
        }
    }
}
