using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Streaming;

public static class StreamingServiceCollectionExtensions {

    /// <summary>
    /// Amends the streaming configuration.
    /// </summary>
    /// <example>
    /// <code>
    /// public void ConfigureServices(IServiceCollection services) {
    ///     services.ConfigureStreaming(streaming => {
    ///         streaming.HeartbeatInterval = TimeSpan.FromSeconds(5);
    ///     });
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// An amender rather than a replacement value, so it composes with the defaults the request
    /// module registers - the same shape as <c>ConfigureCompression</c>.
    /// </remarks>
    public static IServiceCollection ConfigureStreaming(
        this IServiceCollection services, Action<StreamingConfiguration> configure) {
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(
                Array.Empty<IConfigurationValueProvider>(),
                new IConfigurationValueAmender[] {
                    new SimpleConfigurationValueAmender<StreamingConfiguration>(
                        (_, configuration) => {
                            configure(configuration);

                            return configuration;
                        })
                }));

        return services;
    }
}
