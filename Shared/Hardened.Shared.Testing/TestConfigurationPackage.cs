using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;

namespace Hardened.Shared.Testing
{
    public class TestConfigurationPackage : IConfigurationPackage, IConfigurationValueAmender
    {
        private List<AppConfigAmendAttribute> _amendAttributes;

        public TestConfigurationPackage(List<AppConfigAmendAttribute> amendAttributes)
        {
            _amendAttributes = amendAttributes;
        }

        public IEnumerable<IConfigurationValueProvider> ConfigurationValueProviders(IEnvironment env)
        {
            yield break;
        }

        public IEnumerable<IConfigurationValueAmender> ConfigurationValueAmenders(IEnvironment env)
        {
            yield return this;
        }
        
        public object ApplyConfiguration(IEnvironment environment, object configurationValue)
        {
            foreach (var appConfigAmendAttribute in _amendAttributes)
            {
                if (appConfigAmendAttribute.ConfigType == configurationValue.GetType())
                {
                    var propertyInfo = GetPropertyInfo(configurationValue, appConfigAmendAttribute.PropertyName);

                    if (propertyInfo == null)
                    {
                        throw new Exception(
                        $"Could not find property {appConfigAmendAttribute.PropertyName} on {appConfigAmendAttribute.ConfigType.Name}");

                    }

                    if (!propertyInfo.CanWrite || 
                        propertyInfo.SetMethod == null)
                    {
                        throw new Exception(
                            $"Property {appConfigAmendAttribute.PropertyName} on {appConfigAmendAttribute.ConfigType} is not modifiable");
                    }

                    propertyInfo.SetMethod.Invoke(configurationValue, new [] { appConfigAmendAttribute.Value });
                }
            }

            return configurationValue;
        }

        private PropertyInfo? GetPropertyInfo(object configurationValue, string propertyName)
        {
            return configurationValue.GetType().GetProperty(propertyName);
        }
    }
}
