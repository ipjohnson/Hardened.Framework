using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Aws.Lambda.Runtime.Streaming;

public static class LambdaResponseModeServiceCollectionExtensions {

    /// <summary>
    /// Amends the response mode the environment was read into.
    /// </summary>
    /// <example>
    /// <code>
    /// public void ConfigureServices(IServiceCollection services) {
    ///     services.ConfigureLambdaResponseMode(mode => mode.Mode = LambdaResponseMode.Stream);
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// An amender rather than a replacement value, so it runs after
    /// <c>HARDENED_LAMBDA_RESPONSE_MODE</c> has been read and wins over it. That order is what makes
    /// this useful: an application that knows it is only ever deployed behind a streaming function
    /// URL can say so in code and stop depending on a variable being set correctly.
    /// </remarks>
    public static IServiceCollection ConfigureLambdaResponseMode(
        this IServiceCollection services, Action<LambdaResponseModeConfiguration> configure) {
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(
                Array.Empty<IConfigurationValueProvider>(),
                new IConfigurationValueAmender[] {
                    new SimpleConfigurationValueAmender<LambdaResponseModeConfiguration>(
                        (_, configuration) => {
                            configure(configuration);

                            return configuration;
                        })
                }));

        return services;
    }
}
