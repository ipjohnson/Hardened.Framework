using Hardened.Requests.Abstract.Middleware;
using Hardened.Web.AspNetCore.Runtime.Impl;
using Hardened.Web.Runtime.Handlers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.AspNetCore.Runtime;

public static class AspNetCoreExtensions {
    public static IApplicationBuilder UseHardened(this IApplicationBuilder builder) {
        builder.Use(HardenedMiddleware);
        var service = builder.ApplicationServices.GetRequiredService<IMiddlewareService>();
        var webFilter =
            builder.ApplicationServices.GetRequiredService<IWebExecutionHandlerService>();
        
        service.Use(context => webFilter);
        
        return builder;
    }

    public static Task HardenedMiddleware(HttpContext context, RequestDelegate next) {
        var handler = context.RequestServices.GetRequiredService<IAspNetCoreRequestHandler>();

        return handler.HandleRequest(context, next);
    }
}