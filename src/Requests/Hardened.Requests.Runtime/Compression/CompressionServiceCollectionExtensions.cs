using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Compression;

public static class CompressionServiceCollectionExtensions {

    /// <summary>
    /// Amends the compression configuration.
    /// </summary>
    /// <example>
    /// <code>
    /// public void ConfigureServices(IServiceCollection services) {
    ///     services.ConfigureCompression(compression => {
    ///         compression.Encodings = ["br", "gzip"];
    ///         compression.MediaTypes.Add("application/wasm");
    ///         compression.MaxDecompressedRequestBytes = 8_000_000;
    ///     });
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// An amender rather than a replacement value, so this composes with the defaults the request
    /// module registers and runs whether or not <c>[Enable&lt;HardenedCompression&gt;]</c> is written:
    /// the request-side cap applies to every application.
    /// </remarks>
    public static IServiceCollection ConfigureCompression(
        this IServiceCollection services, Action<CompressionConfiguration> configure) {
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(
                Array.Empty<IConfigurationValueProvider>(),
                new IConfigurationValueAmender[] {
                    new SimpleConfigurationValueAmender<CompressionConfiguration>(
                        (_, configuration) => {
                            configure(configuration);

                            return configuration;
                        })
                }));

        return services;
    }
}
