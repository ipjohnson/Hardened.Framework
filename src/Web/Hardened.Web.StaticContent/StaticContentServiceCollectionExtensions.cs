using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.StaticContent;

public static class StaticContentServiceCollectionExtensions {

    /// <summary>
    /// Configures the static content mount.
    /// </summary>
    /// <example>
    /// <code>
    /// public void ConfigureServices(IServiceCollection services) {
    ///     services.ConfigureStaticContent(content => {
    ///         content.CacheMaxAge = 31536000;
    ///         content.Immutable = true;
    ///         content.Requirement = Requirement.Grant("files:read");
    ///     });
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// <b>Everything that is not a string is set here rather than on
    /// <c>[HardenedStaticContent]</c>, and that is a constraint rather than a preference.</b>
    /// DependencyModules generates the module attribute by unwrapping <c>Nullable&lt;T&gt;</c>, so a
    /// <c>bool?</c> on the module becomes a <c>bool</c> on the attribute and the guard it emits is
    /// <c>if (value != null)</c> - always true for a value type, which the generator knows, because
    /// it emits <c>#pragma warning disable CS0472</c> above it.
    /// </para>
    /// <para>
    /// The consequence is that a value-typed setting is copied onto the module whether or not the
    /// author wrote it, carrying <c>default(T)</c> when they did not. <c>[HardenedStaticContent]</c>
    /// with no arguments would have turned off validators, compression, caching and ranges, all
    /// silently. A <c>string</c> is a reference type and the guard is real, which is why
    /// <c>Path</c> and <c>FallBackFile</c> can stay there.
    /// </para>
    /// <para>
    /// An amender rather than a replacement value, so this composes with the module's own settings
    /// and with the environment's - it runs over whatever they left.
    /// </para>
    /// </remarks>
    public static IServiceCollection ConfigureStaticContent(
        this IServiceCollection services, Action<StaticContentConfiguration> configure) {
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(
                Array.Empty<IConfigurationValueProvider>(),
                new IConfigurationValueAmender[] {
                    new SimpleConfigurationValueAmender<StaticContentConfiguration>(
                        (_, configuration) => {
                            configure(configuration);

                            return configuration;
                        })
                }));

        return services;
    }
}
