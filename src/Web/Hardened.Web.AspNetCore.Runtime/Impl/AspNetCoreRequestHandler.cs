using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.AspNetCore.Runtime.Impl;

public interface IAspNetCoreRequestHandler {
    Task HandleRequest(HttpContext context, RequestDelegate requestDelegate);
}

[Expose]
public class AspNetCoreRequestHandler : IAspNetCoreRequestHandler {
    private IMetricLoggerProvider _metricLoggerProvider;
    private IMiddlewareService _middlewareService;
    
    public AspNetCoreRequestHandler(IMetricLoggerProvider metricLoggerProvider, IMiddlewareService middlewareService) {
        _metricLoggerProvider = metricLoggerProvider;
        _middlewareService = middlewareService;
    }

    public async Task HandleRequest(HttpContext context, RequestDelegate requestDelegate) {
        
        var executionChain = _middlewareService.GetExecutionChain(GetExecutionContext(context, _metricLoggerProvider));

        await executionChain.Next();

        if (!context.Response.HasStarted) {
            await requestDelegate(context);
        }
    }

    private IExecutionContext GetExecutionContext(
        HttpContext context,
        IMetricLoggerProvider metricLoggerProvider) {
        return new AspNetExecutionContext(context, metricLoggerProvider.CreateLogger("asp-net-session"));
    }
}