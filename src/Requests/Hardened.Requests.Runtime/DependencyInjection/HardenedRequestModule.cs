using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Links;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Runtime.DependencyInjection;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Serializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Runtime.DependencyInjection;

[DependencyModule]
[HardenedCoreModule]
public partial class HardenedRequestModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        RegisterReflectionSerializers(services);

        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(new IConfigurationValueProvider[] {
                new NewConfigurationValueProvider<IResponseHeaderConfiguration, ResponseHeaderConfiguration>(null),
                new NewConfigurationValueProvider<IJsonSerializerConfiguration, JsonSerializerConfiguration>(null),
                new NewConfigurationValueProvider<ILinkConfiguration, LinkConfiguration>(null),
                new NewConfigurationValueProvider<IAuthorizationConfiguration, AuthorizationConfiguration>(null)
            }));
        services.AddSingleton(
            s => Options.Create(s.GetRequiredService<IConfigurationManager>()
                .GetConfiguration<IResponseHeaderConfiguration>()));

        services.AddSingleton(
            s => Options.Create(s.GetRequiredService<IConfigurationManager>()
                .GetConfiguration<IJsonSerializerConfiguration>()));

        services.AddSingleton(
            s => Options.Create(s.GetRequiredService<IConfigurationManager>()
                .GetConfiguration<ILinkConfiguration>()));

        services.AddSingleton(
            s => Options.Create(s.GetRequiredService<IConfigurationManager>()
                .GetConfiguration<IAuthorizationConfiguration>()));

        // Always installed. It costs one call per handler at startup and returns null for a handler
        // that carries no authorization attribute, so an application that has not opted in to
        // anything pays nothing per request.
        services.AddSingleton<IStartupService, AuthorizationStartupService>();
    }

    /// <summary>
    /// The reflection-based JSON serializers, registered only where reflection can work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These used to carry <c>[SingletonService]</c>, so the generated <c>ModuleDependencies</c>
    /// registered them unconditionally. That is right for every application that runs on a JIT and
    /// wrong for one publishing Native AOT, which cannot use them and does not want them referenced:
    /// their constructors are annotated <c>RequiresUnreferencedCode</c>, so ILC reported two
    /// warnings against generated code no one could annotate or suppress.
    /// </para>
    /// <para>
    /// <c>JsonSerializer.IsReflectionEnabledByDefault</c> is the fix rather than a suppression, and
    /// it is the right switch rather than the nearest one: it is System.Text.Json's own, false under
    /// <c>PublishTrimmed</c> as well as under AOT, where <c>IsDynamicCodeSupported</c> would still be
    /// true for a trimmed application running on a JIT - the configuration where reflective
    /// serialization is most likely to fail quietly. The trimmer folds it to a constant and removes
    /// the branch, so a trimmed or AOT publish contains no reference to these types and the warnings
    /// are gone because the code is. Everything else registers them exactly as before.
    /// </para>
    /// <para>
    /// An AOT application therefore has to import <c>[AotSerializerModule]</c>, which is what it
    /// would do anyway; without it nothing registers a serializer and startup says so, which is a
    /// better failure than reflecting successfully until something is trimmed away.
    /// </para>
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Guarded on JsonSerializer.IsReflectionEnabledByDefault, which the trimmer " +
                        "folds to false and removes the branch for a trimmed or AOT publish.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Guarded on JsonSerializer.IsReflectionEnabledByDefault, which the trimmer " +
                        "folds to false and removes the branch for a trimmed or AOT publish.")]
    private static void RegisterReflectionSerializers(IServiceCollection services) {
        if (!JsonSerializer.IsReflectionEnabledByDefault) {
            return;
        }

        services.TryAddSingleton<IRequestDeserializer, SystemTextJsonRequestDeserializer>();
        services.AddSingleton<IResponseSerializer, SystemTextJsonResponseSerializer>();
    }
}
