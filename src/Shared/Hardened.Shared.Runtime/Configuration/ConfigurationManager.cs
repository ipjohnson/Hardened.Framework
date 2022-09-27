using System.Collections.Concurrent;
using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration
{
    public class ConfigurationManager : IConfigurationManager
    {
        private readonly IEnvironment _environment;
        private readonly ConcurrentDictionary<Type, object> _configuration;
        private readonly Dictionary<Type, IConfigurationValueProvider> _valueProviders;
        private readonly List<IConfigurationValueAmender> _amenders;

        public ConfigurationManager(IEnvironment environment, IEnumerable<IConfigurationPackage> configurationPackages)
        {
            _environment = environment;
            _configuration = new ConcurrentDictionary<Type, object>();
            _valueProviders = new Dictionary<Type, IConfigurationValueProvider>();
            _amenders = new List<IConfigurationValueAmender>();

            foreach (var configurationPackage in configurationPackages)
            {
                foreach (var valueProvider in configurationPackage.ConfigurationValueProviders(environment))
                {
                    _valueProviders[valueProvider.InterfaceType] = valueProvider;
                }

                foreach (var amender in configurationPackage.ConfigurationValueAmenders(environment))
                {
                    _amenders.Add(amender);
                }
            }
        }

        public T GetConfiguration<T>() where T : class
        {
            if (_configuration.TryGetValue(typeof(T), out var value))
            {
                return (T)value;
            }

            if (_valueProviders.TryGetValue(typeof(T), out var valueProvider))
            {
                value = CreateConfigurationValue<T>(valueProvider);

                _configuration.TryAdd(typeof(T), value);

                return (T)value;
            }

            throw new Exception($"{typeof(T).Name} is not a registered configuration type");
        }

        private T CreateConfigurationValue<T>(IConfigurationValueProvider valueProvider) where T : class
        {
            return (T)valueProvider.ProvideValue(_environment, ApplyAmenders);
        }

        private void ApplyAmenders(IEnvironment env, object value)
        {
            for (var i = 0; i < _amenders.Count; i++)
            {
                value = _amenders[i].ApplyConfiguration(env, value);
            }
        }
    }
}
