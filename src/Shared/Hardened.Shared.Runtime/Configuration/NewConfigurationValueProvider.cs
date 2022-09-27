using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Runtime.Configuration
{
    public class NewConfigurationValueProvider<TInterface, TImpl> : IConfigurationValueProvider where TImpl : class, TInterface, new()
    {
        private Action<IEnvironment, TImpl>? _initAction;

        public NewConfigurationValueProvider(Action<IEnvironment, TImpl>? initAction)
        {
            _initAction = initAction;
        }

        public object ProvideValue(IEnvironment environment, Action<IEnvironment, object> amender)
        {
            var tValue = new TImpl();

            _initAction?.Invoke(environment, tValue);

            amender(environment, tValue);

            return tValue;
        }

        public Type InterfaceType => typeof(TInterface);

        public Type ImplementationType => typeof(TImpl);
    }
}
