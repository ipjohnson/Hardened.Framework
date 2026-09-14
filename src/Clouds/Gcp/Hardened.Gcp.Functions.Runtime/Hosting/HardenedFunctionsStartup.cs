using DependencyModules.Runtime.Interfaces;
using Google.Cloud.Functions.Hosting;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Runtime.Handlers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Gcp.Functions.Runtime.Hosting;

/// <summary>
/// Puts a Hardened application inside the Functions Framework host.
/// </summary>
/// <remarks>
/// <para>
/// The Functions Framework finds this through <c>[FunctionsStartup(typeof(...))]</c> on the
/// assembly or on the <c>FUNCTION_TARGET</c> type, which is why the generator writes the attribute
/// beside the entry type rather than asking anyone to write it. Constructed generic, so the one
/// thing this package cannot name - the application - arrives as a type argument:
/// </para>
/// <code>
/// [FunctionsStartup(typeof(HardenedFunctionsStartup&lt;Application&gt;))]
/// </code>
/// <para>
/// <b>One container, Google's.</b> The application is populated into the host's own service
/// collection rather than built into a container of its own, so a handler resolves the host's
/// logging and configuration the way any service in the process does. The same call
/// <c>UseHardened</c> makes on the Azure worker, for the reason it gives: the Lambda bootstrap
/// builds a container only because Lambda gives it none, and joining a generic host is the
/// ordinary thing.
/// </para>
/// <para>
/// <b>The application names no host module of its own for this.</b> A Cloud Run service and a
/// Cloud Functions deployment run the same <c>[CloudRunRuntime]</c> application: that module
/// brings the web pipeline, the trigger front door and the one dispatch, and none of those are
/// Kestrel. What differs between the two deployments is the entry point and nothing else, which is
/// the property a benchmark comparing them depends on - a second host module would fork the
/// assembly under measurement.
/// </para>
/// </remarks>
/// <typeparam name="TApplication">
/// The application, the same type a <c>Program.cs</c> would call
/// <c>PopulateServiceCollection</c> on.
/// </typeparam>
public class HardenedFunctionsStartup<TApplication> : FunctionsStartup
    where TApplication : IDependencyModule, new()
{
    /// <summary>
    /// Registers the application's services, and the host that runs requests through them.
    /// </summary>
    /// <remarks>
    /// Everything is registered before the first request. The Functions Framework builds the host,
    /// configures the pipeline and then starts the server, so a missing registration fails at
    /// start with the name of what was missing rather than on the first delivery.
    /// </remarks>
    public override void ConfigureServices(
        WebHostBuilderContext context,
        IServiceCollection services
    )
    {
        // The generic host has added logging by the time this runs; a collection built without one,
        // as the registration tests build one, has not. AddLogging registers with TryAdd, so the
        // second call costs nothing.
        services.AddLogging();

        // What the framework reads configuration and the environment through. A deployed function
        // gets its settings from the environment, which is where the process arguments enter.
        services.AddHardenedEnvironment(
            new EnvironmentImpl(arguments: Environment.GetCommandLineArgs())
        );

        new TApplication().PopulateServiceCollection(services);

        services.AddSingleton<CloudFunctionHost>();
    }

    /// <summary>
    /// Runs the registered startup services and appends the dispatch to the middleware chain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two things <c>KestrelServerRunner.StartAsync</c> does before it listens, and for the
    /// same reasons. Startup services populate the filter registry, the CORS configuration and -
    /// on this host family - the trigger front door, so a server that begins answering first can
    /// serve a request against a half-built chain. <c>ApplicationLogic.Start</c> is guarded per
    /// provider, so a process that already ran them runs them once.
    /// </para>
    /// <para>
    /// <c>Configure</c> rather than an <c>IHostedService</c>, which would be a race: the Functions
    /// Framework registers <c>GenericWebHostService</c> while building the host and this startup's
    /// services are added afterwards, so a hosted service of ours would start after the server was
    /// already listening. Pipeline configuration runs inside <c>GenericWebHostService.StartAsync</c>
    /// and strictly before the server is started.
    /// </para>
    /// <para>
    /// <c>IMiddlewareService</c> holds a plain list, so appending the dispatch must happen exactly
    /// once per provider. The Functions Framework calls this once per host.
    /// </para>
    /// </remarks>
    public override void Configure(WebHostBuilderContext context, IApplicationBuilder app)
    {
        var provider = app.ApplicationServices;

        ApplicationLogic.Start(provider, null).GetAwaiter().GetResult();

        var handler = provider.GetRequiredService<IWebExecutionHandlerService>();

        provider.GetRequiredService<IMiddlewareService>().Use(_ => handler);
    }
}
