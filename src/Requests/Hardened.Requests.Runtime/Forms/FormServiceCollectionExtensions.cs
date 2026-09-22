using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Forms;

public static class FormServiceCollectionExtensions
{
    /// <summary>
    /// Amends the form configuration.
    /// </summary>
    /// <example>
    /// <code>
    /// public void ConfigureServices(IServiceCollection services) {
    ///     services.ConfigureForms(forms => forms.MaxBodyBytes = 5_000_000);
    /// }
    /// </code>
    /// </example>
    public static IServiceCollection ConfigureForms(
        this IServiceCollection services,
        Action<FormConfiguration> configure
    )
    {
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(
                Array.Empty<IConfigurationValueProvider>(),
                new IConfigurationValueAmender[]
                {
                    new SimpleConfigurationValueAmender<FormConfiguration>(
                        (_, configuration) =>
                        {
                            configure(configuration);

                            return configuration;
                        }
                    ),
                }
            )
        );

        return services;
    }
}
