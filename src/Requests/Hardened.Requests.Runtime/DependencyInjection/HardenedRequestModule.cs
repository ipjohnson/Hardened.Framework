using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Runtime.DependencyInjection;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Abstract.Timeouts;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Streaming;
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
                new NewConfigurationValueProvider<IAuthorizationConfiguration, AuthorizationConfiguration>(null),
                new NewConfigurationValueProvider<IStreamingConfiguration, StreamingConfiguration>(null)
            }));
        services.AddSingleton(
            s => Options.Create(s.GetRequiredService<IConfigurationManager>()
                .GetConfiguration<IResponseHeaderConfiguration>()));

        services.AddSingleton(
            s => Options.Create(s.GetRequiredService<IConfigurationManager>()
                .GetConfiguration<IJsonSerializerConfiguration>()));

        services.AddSingleton(
            s => Options.Create(s.GetRequiredService<IConfigurationManager>()
                .GetConfiguration<IAuthorizationConfiguration>()));

        services.AddSingleton(
            s => Options.Create(s.GetRequiredService<IConfigurationManager>()
                .GetConfiguration<IStreamingConfiguration>()));

        // The caller, as a service a handler can take. Scoped rather than resolved from the
        // context, because the container has no per-request instance of the context to build one
        // from. Two registrations rather than one: the middleware fills the holder and everything
        // else reads the interface, which is what keeps the setter off the contract.
        services.AddScoped<CurrentCaller>();
        services.AddScoped<ICurrentCaller>(provider => provider.GetRequiredService<CurrentCaller>());

        // The deadline, as a service a handler can take. Singleton rather than scoped, because a
        // described handler can only reach this through its constructor - its signature is the
        // contract's - and may itself be a singleton, which a scoped registration would let it
        // capture. One registration rather than the two above: the per-request value lives in a
        // static AsyncLocal that TimeoutFilter writes, so nothing needs a second registration to
        // fill an instance with.
        services.AddSingleton<IRequestDeadline, RequestDeadline>();

        // Always installed. It costs one call per handler at startup and returns null for a handler
        // that carries no authorization attribute, so an application that has not opted in to
        // anything pays nothing per request.
        //
        // TryAddEnumerable for the reason the CORS one is: a startup service registered twice runs
        // twice, and this one installs a filter provider.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IStartupService, AuthorizationStartupService>());

        // Request decompression is not registered here any more. Content-Encoding is an HTTP
        // request header and a Lambda invocation carries none, so installing the filter for every
        // host put a provider in a queue function that no payload could ever trigger.
        // HardenedWebModule installs it, which is every HTTP host and nothing else.

        // Same footing: one resolve at startup, and no middleware at all unless something
        // registered an IPrincipalSource. Constructed over this collection rather than resolved
        // from the provider, because a source registered as IPrincipalSource<TScheme> is reachable
        // only by its closed service type and a built provider cannot be asked what those are.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IStartupService>(new AuthenticationStartupService(services)));
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
