using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Configuration
{
    public class SimpleConfigurationPackage : IConfigurationPackage
    {
        private readonly IReadOnlyList<IConfigurationValueProvider> _valueProviders;
        private readonly IReadOnlyCollection<IConfigurationValueAmender > _amenders;

        public SimpleConfigurationPackage(IReadOnlyList<IConfigurationValueProvider> valueProviders, IReadOnlyCollection<IConfigurationValueAmender> amenders)
        {
            _valueProviders = valueProviders;
            _amenders = amenders;
        }

        public IEnumerable<IConfigurationValueProvider> ConfigurationValueProviders()
        {
            return _valueProviders;
        }

        public IEnumerable<IConfigurationValueAmender> Amenders()
        {
            return _amenders;
        }
    }
}
