using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration;

public class SimpleConfigurationPackage : IConfigurationPackage {
    private readonly IReadOnlyList<IConfigurationValueProvider> _valueProviders;
    private readonly IReadOnlyCollection<IConfigurationValueAmender> _amenders;

    public SimpleConfigurationPackage(IReadOnlyList<IConfigurationValueProvider> valueProviders) {
        _valueProviders = valueProviders;
        _amenders = Array.Empty<IConfigurationValueAmender>();
    }


    public SimpleConfigurationPackage(IReadOnlyList<IConfigurationValueProvider> valueProviders,
        IReadOnlyCollection<IConfigurationValueAmender> amenders) {
        _valueProviders = valueProviders;
        _amenders = amenders;
    }

    public IEnumerable<IConfigurationValueProvider> ConfigurationValueProviders(IEnvironment env) {
        return _valueProviders;
    }

    public IEnumerable<IConfigurationValueAmender> ConfigurationValueAmenders(IEnvironment env) {
        return _amenders;
    }
}