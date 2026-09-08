using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Application;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Azure.Functions.Runtime.Hosting;

/// <summary>
/// Puts a Hardened application inside the isolated worker.
/// </summary>
/// <remarks>
/// <para>
/// The whole of an application's entry point:
/// </para>
/// <code>
/// var host = new HostBuilder()
///     .ConfigureFunctionsWorkerDefaults(worker => worker.UseHardened&lt;Application&gt;())
///     .Build();
///
/// host.Run();
/// </code>
/// <para>
/// <b>One container, the worker's.</b> The application is populated into the worker's own service
/// collection rather than built into a container of its own, so a handler resolves the worker's
/// logging and configuration the way any service in the process does, and the worker resolves the
/// invocation handler the shims call. The Lambda bootstrap builds its own because Lambda gives it
/// no container to join; the worker is a generic host, and joining it is the ordinary thing.
/// </para>
/// <para>
/// <b>Everything is registered before the first invocation.</b> The worker builds the host, then
/// answers the host's metadata request, then loads functions, so a missing registration fails at
/// start with the name of what was missing rather than on the first message of the day.
/// </para>
/// </remarks>
public static class HardenedFunctionsWorkerBuilderExtensions {
    /// <summary>
    /// Registers <typeparamref name="TApplication"/>, its modules and its generated function
    /// shims with the worker.
    /// </summary>
    /// <param name="builder">The worker builder <c>ConfigureFunctionsWorkerDefaults</c> hands over.</param>
    /// <param name="environment">
    /// The application's environment, or the process's own: <c>HARDENED_ENVIRONMENT</c> and the
    /// command line the host started the worker with.
    /// </param>
    /// <remarks>
    /// The constraint on <see cref="IHardenedFunctionsApplication"/> is what the generator
    /// satisfies. An application that does not compile against it has no shims, which means no
    /// Azure runtime package bound a trigger it declares - and that is reported at the generator
    /// as HRDF001, before this is reached.
    /// </remarks>
    public static IFunctionsWorkerApplicationBuilder UseHardened<TApplication>(
        this IFunctionsWorkerApplicationBuilder builder, IHardenedEnvironment? environment = null)
        where TApplication : class, IDependencyModule, IHardenedFunctionsApplication, new() {
        var application = new TApplication();

        // The generic host has already added logging by the time ConfigureFunctionsWorkerDefaults
        // runs this; a worker collection built without a host, as the registration tests build
        // one, has not. AddLogging registers with TryAdd, so the second call costs nothing.
        builder.Services.AddLogging();

        builder.Services.AddHardenedEnvironment(
            environment ?? new EnvironmentImpl(arguments: Environment.GetCommandLineArgs()));

        application.PopulateServiceCollection(builder.Services);
        application.ConfigureFunctionsWorker(builder.Services);

        return builder;
    }
}
