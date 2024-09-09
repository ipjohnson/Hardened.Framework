using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration;

public interface IAppConfig {
    IAppConfig ProvideValue<TInterface, TImpl>(Func<IHardenedEnvironment, TImpl> valueProvider) where TImpl : class, TInterface;

    IAppConfig Amend<TImpl>(Action<TImpl> amendAction, string environment = "") where TImpl : class;

    IAppConfig Amend<TImpl>(Func<IHardenedEnvironment, TImpl, TImpl> amendFunc) where TImpl : class;
}