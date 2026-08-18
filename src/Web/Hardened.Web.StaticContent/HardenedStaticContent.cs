using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Web.StaticContent;

/// <summary>
/// Serves a directory of files.
///
/// <code>
/// [HardenedModule]
/// [HardenedWebModule]
/// [HardenedStaticContent(FallBackFile = "/index.html")]
/// [AspNetCoreRuntime]
/// public partial class Application { }
/// </code>
///
/// <para>
/// <b>A package rather than a flag, and that is the opt-in.</b> Static content used to be part of
/// <c>Hardened.Web.Runtime</c> and registered unconditionally, so a directory called
/// <c>wwwroot</c> beside the binary was on the public internet with nobody having asked - and every
/// application carried the code whether or not it served a file, because a DI registration is
/// exactly what a trimmer cannot remove. Most services have no static content at all. An
/// application that does not reference this package now carries none of it, and cannot serve a file
/// by accident.
/// </para>
///
/// <para>
/// <b>Install it more than once for more than one directory.</b> <see cref="Equals"/> is keyed on
/// <see cref="Path"/>, so two installs over different directories both load and two over the same
/// one collapse into one.
/// </para>
///
/// <para>
/// <b>There is deliberately no <c>[AllowAnonymous]</c> on what this registers.</b> That is the one
/// thing an authorization convention cannot narrow. Without it a mount inherits the application's
/// posture: public where no authorization is configured, denied under <c>[RequireAuthorization]</c>,
/// and gate-able by convention everywhere else. A mount wanting a policy of its own sets
/// <c>IStaticContentConfiguration.Requirement</c>, which cannot be an attribute argument and so is
/// set through configuration rather than here.
/// </para>
/// </summary>
[DependencyModule]
public partial class HardenedStaticContent : IServiceCollectionConfiguration {

    public const string DefaultPath = "wwwroot";

    /// <summary>The environment that does not cache, so an edit is visible on reload.</summary>
    public const string DevelopmentEnvironment = "development";

    /// <summary>
    /// The directory served, relative to the working directory or the deployment directory.
    /// </summary>
    /// <remarks>
    /// Nullable, and so is every property here, because that is what makes a default survive.
    /// DependencyModules generates the module attribute with every property defaulting to
    /// <c>default(T)</c> and copies each onto the module, guarded by a null check <em>only for a
    /// nullable one</em>. A non-nullable <c>string</c> would therefore be assigned null by
    /// <c>[HardenedStaticContent]</c> written with no arguments, and the initializer here never
    /// seen.
    /// </remarks>
    public string? Path { get; set; } = DefaultPath;

    /// <summary>
    /// The file that answers a path with nothing behind it, for a single-page application.
    /// </summary>
    public string? FallBackFile { get; set; }

    /// <summary>Seconds of <c>max-age</c>, or null to send no <c>Cache-Control</c> at all.</summary>
    public int? CacheMaxAge { get; set; }

    /// <summary>Appends <c>immutable</c>, for a fingerprinted asset that never changes.</summary>
    public bool? Immutable { get; set; }

    /// <summary>Whether a validator is sent, and conditional requests answered.</summary>
    public bool? EnableETag { get; set; }

    /// <summary>Whether text content is compressed on the way into the cache.</summary>
    public bool? CompressTextContent { get; set; }

    /// <summary>
    /// Whether a file, once read, is kept. Defaults to off in <c>development</c>, so an edit is
    /// visible on reload without a watcher or any invalidation machinery.
    /// </summary>
    public bool? CacheContent { get; set; }

    /// <summary>Whether byte ranges are served, and advertised. On unless a mount says otherwise.</summary>
    public bool? EnableRangeRequests { get; set; }

    public void ConfigureServices(IServiceCollection services) {
        // An init action rather than a prebuilt instance, so an IConfigurationValueAmender still
        // runs afterwards: NewConfigurationValueProvider applies amenders to what this leaves. That
        // is how the two settings an attribute argument cannot carry - OnPrepareResponse, which is
        // a delegate, and Requirement, which is a tree - are still reachable.
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(
                new IConfigurationValueProvider[] {
                    new NewConfigurationValueProvider<IStaticContentConfiguration, StaticContentConfiguration>(
                        (environment, configuration) => {
                            configuration.Path = Path ?? DefaultPath;
                            configuration.FallBackFile = FallBackFile;
                            configuration.CacheMaxAge = CacheMaxAge;
                            configuration.Immutable = Immutable ?? false;
                            configuration.EnableETag = EnableETag ?? true;
                            configuration.CompressTextContent = CompressTextContent ?? true;

                            // Defaulted from the environment rather than fixed, so the inner loop
                            // needs no configuration and a deployed build needs no thought. Stated
                            // explicitly on the module it wins either way.
                            configuration.CacheContent =
                                CacheContent ?? !environment.Matches(DevelopmentEnvironment);
                            configuration.EnableRangeRequests = EnableRangeRequests ?? true;
                        })
                },
                Array.Empty<IConfigurationValueAmender>()));

        services.TryAddSingleton(
            serviceProvider => Microsoft.Extensions.Options.Options.Create(
                serviceProvider.GetRequiredService<IConfigurationManager>()
                    .GetConfiguration<IStaticContentConfiguration>()));

        services.TryAddSingleton<StaticContentController>();

        // The manifest when the build produced one, the directory otherwise. Chosen here rather
        // than by two registrations racing, because which one an application gets is a property of
        // whether it declared <HardenedStaticContent> items - and that is only knowable once the
        // container is built and the generated manifest, if any, has registered itself.
        services.TryAddSingleton<IStaticContentSource>(serviceProvider =>
            serviceProvider.GetService<IStaticContentManifest>() != null
                ? ActivatorUtilities.CreateInstance<ManifestContentSource>(serviceProvider)
                : ActivatorUtilities.CreateInstance<FileSystemContentSource>(serviceProvider));

        // As a fallback, so it is asked after every ordinary provider whatever order the modules
        // were listed in. A directory of files can shadow any path at all; see
        // IFallbackRequestHandlerProvider.
        services.AddSingleton<IFallbackRequestHandlerProvider>(
            serviceProvider => new StaticContentMountProvider(serviceProvider));
    }

    /// <summary>
    /// Keyed on <see cref="Path"/>, so two installs over different directories both load.
    /// </summary>
    public override bool Equals(object? obj) =>
        obj is HardenedStaticContent other &&
        string.Equals(Path, other.Path, StringComparison.Ordinal);

    public override int GetHashCode() => Path?.GetHashCode() ?? 0;
}
