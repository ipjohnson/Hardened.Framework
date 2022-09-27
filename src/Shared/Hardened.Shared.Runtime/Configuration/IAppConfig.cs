using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration
{
    public interface IAppConfig
    {
        IAppConfig ProvideValue<TInterface, TImpl>(Func<IEnvironment, TImpl> valueProvider) where TImpl : class, TInterface;

        IAppConfig Amend<TImpl>(Action<TImpl> amendAction, string environment = "") where TImpl : class;

        IAppConfig Amend<TImpl>(Func<IEnvironment, TImpl, TImpl> amendFunc) where TImpl : class;
    }
}
