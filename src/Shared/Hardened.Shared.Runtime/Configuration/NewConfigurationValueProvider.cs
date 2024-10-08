﻿using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration;

public class NewConfigurationValueProvider<TInterface, TImpl> : IConfigurationValueProvider
    where TImpl : class, TInterface, new() {
    private readonly Action<IHardenedEnvironment, TImpl>? _initAction;

    public NewConfigurationValueProvider(Action<IHardenedEnvironment, TImpl>? initAction) {
        _initAction = initAction;
    }

    public object ProvideValue(IHardenedEnvironment environment, Action<IHardenedEnvironment, object> amender) {
        var tValue = new TImpl();

        _initAction?.Invoke(environment, tValue);

        amender(environment, tValue);

        return tValue;
    }

    public Type InterfaceType => typeof(TInterface);

    public Type ImplementationType => typeof(TImpl);
}