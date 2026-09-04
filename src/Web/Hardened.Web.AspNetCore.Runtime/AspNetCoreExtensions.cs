using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.AspNetCore.Runtime.Impl;
using Hardened.Web.Runtime.Handlers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.AspNetCore.Runtime;

public static class AspNetCoreExtensions {
    /// <summary>The seconds <see cref="UseHardened"/> gives startup services to finish.</summary>
    private const int StartupTimeoutInSeconds = 15;

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
    public static IApplicationBuilder UseHardened(this IApplicationBuilder builder) {
        builder.Use(HardenedMiddleware);
        var service = builder.ApplicationServices.GetRequiredService<IMiddlewareService>();
        var webFilter =
            builder.ApplicationServices.GetRequiredService<IWebExecutionHandlerService>();

        ApplicationLogic.StartWithWait(builder.ApplicationServices, null, StartupTimeoutInSeconds);

        service.Use(context => webFilter);

        return builder;
    }

    public static Task HardenedMiddleware(HttpContext context, RequestDelegate next) {
        var handler = context.RequestServices.GetRequiredService<IAspNetCoreRequestHandler>();

        return handler.HandleRequest(context, next);
    }
}