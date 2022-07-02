using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Runtime.DependencyInjection
{
    /// <summary>
    /// This class serves as a common registration point for all modules
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class DependenciesFor<T>
    {
        private static Dictionary<string, Action<IServiceCollection>> _registrations = new ();

        public static void Initialize(IServiceCollection serviceCollection)
        {
            foreach (var registrationKvp in _registrations)
            {
                registrationKvp.Value(serviceCollection);
            }
        }
        
        public static string Register(string registrationId, Action<IServiceCollection> registerFunction)
        {
            _registrations[registrationId] = registerFunction;

            return registrationId;
        }
    }
}
