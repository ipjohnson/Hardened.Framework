using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.AspNetCore.Runtime.Impl;
using Hardened.Web.Runtime.Handlers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.Web.AspNetCore.Runtime;

public static class AspNetCoreExtensions
{
    /// <summary>The seconds <see cref="UseHardened"/> gives startup services to finish.</summary>
    private const int StartupTimeoutInSeconds = 15;

    /// <summary>Named after ASP.NET Core's <c>Microsoft.Hosting.Lifetime</c>.</summary>
    private const string EnvironmentLogCategory = "Hardened.Hosting.Lifetime";

    /// <summary>
    /// Inserts the Hardened middleware into the ASP.NET pipeline, runs the registered startup
    /// services, and puts the routing and handler filter at the end of the Hardened chain.
    ///
    /// <para>
    /// The order matters, the same way it does in the Kestrel host. Startup services append their
    /// own filters - authentication, CORS - and the handler filter is terminal, so a chain built
    /// the other way round leaves every one of them unreachable.
    /// </para>
    /// </summary>
    public static IApplicationBuilder UseHardened(this IApplicationBuilder builder)
    {
        LogEnvironment(builder.ApplicationServices);

        builder.Use(HardenedMiddleware);
        var service = builder.ApplicationServices.GetRequiredService<IMiddlewareService>();
        var webFilter =
            builder.ApplicationServices.GetRequiredService<IWebExecutionHandlerService>();

        ApplicationLogic.StartWithWait(builder.ApplicationServices, null, StartupTimeoutInSeconds);

        service.Use(context => webFilter);

        return builder;
    }

    /// <summary>
    /// ASP.NET Core logs its own hosting environment, which <c>HARDENED_ENVIRONMENT</c> does not
    /// set. Without this line an application running as <c>development</c> logs only
    /// "Hosting environment: Production".
    /// </summary>
    private static void LogEnvironment(IServiceProvider services)
    {
        var environment = services.GetService<IHardenedEnvironment>();
        var loggerFactory = services.GetService<ILoggerFactory>();

        if (environment == null || loggerFactory == null)
        {
            return;
        }

        loggerFactory
            .CreateLogger(EnvironmentLogCategory)
            .LogInformation("Hardened environment: {EnvironmentName}", environment.Name);
    }

    public static Task HardenedMiddleware(HttpContext context, RequestDelegate next)
    {
        var handler = context.RequestServices.GetRequiredService<IAspNetCoreRequestHandler>();

        return handler.HandleRequest(context, next);
    }
}
