using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Turning on the default-deny posture from a service collection.
/// </summary>
public static class AuthorizationServiceCollectionExtensions {
    /// <summary>
    /// Denies any handler that carries neither an authorization attribute nor
    /// <c>[AllowAnonymous]</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What <c>[RequireAuthorization]</c> emits. It is public because generated code cannot call
    /// something it cannot name, and useful on its own for an application that would rather write
    /// the line than the attribute - but the attribute is the better tool, because it is also what
    /// the generator reads to report the handlers that are about to start refusing.
    /// </para>
    /// <para>
    /// Registered as an amender rather than by replacing the configuration provider, so it does not
    /// depend on running after the module that registers the default. Amenders accumulate from every
    /// package and are applied when the value is first built, so this holds whatever order the
    /// registrations happen in - and it leaves any other amendment of the same configuration, from
    /// appsettings or elsewhere, still applied.
    /// </para>
    /// </remarks>
    public static IServiceCollection RequireAuthorization(this IServiceCollection services) {
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(
                Array.Empty<IConfigurationValueProvider>(),
                new IConfigurationValueAmender[] { new RequireAuthorizationAmender() }));

        return services;
    }

    /// <summary>
    /// Sets <see cref="AuthorizationConfiguration.RequireAuthorization"/> on the way past.
    /// </summary>
    /// <remarks>
    /// An amender is handed every configuration value the application builds, so it has to check
    /// what it was given. Anything else is returned untouched.
    /// </remarks>
    private class RequireAuthorizationAmender : IConfigurationValueAmender {
        public object ApplyConfiguration(IHardenedEnvironment environment, object configurationValue) {
            if (configurationValue is AuthorizationConfiguration configuration) {
                configuration.RequireAuthorization = true;
            }

            return configurationValue;
        }
    }
}
