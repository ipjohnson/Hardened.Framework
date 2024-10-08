﻿using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration;

public class SimpleConfigurationValueAmender<T> : IConfigurationValueAmender where T : class {
    private readonly Func<IHardenedEnvironment, T, T> _amender;

    public SimpleConfigurationValueAmender(Func<IHardenedEnvironment, T, T> amender) {
        _amender = amender;
    }

    public object ApplyConfiguration(IHardenedEnvironment environment, object configurationValue) {
        if (configurationValue is T tValue) {
            return _amender(environment, tValue);
        }

        return configurationValue;
    }

    public Type ConfigurationType => typeof(T);
}