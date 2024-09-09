using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration;

public class FuncConfigurationValueProvider<TInterface, TImpl> : IConfigurationValueProvider where TImpl : class {
    private readonly Func<IHardenedEnvironment, TImpl> _provider;

    public FuncConfigurationValueProvider(Func<IHardenedEnvironment, TImpl> provider) {
        _provider = provider;
    }

    public object ProvideValue(IHardenedEnvironment environment, Action<IHardenedEnvironment, object> amender) {
        var tValue = _provider(environment);

        amender(environment, tValue);

        return tValue;
    }

    public Type InterfaceType => typeof(TInterface);

    public Type ImplementationType => typeof(TImpl);
}