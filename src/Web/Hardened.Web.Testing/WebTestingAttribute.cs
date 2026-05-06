using DependencyModules.xUnit.Attributes.Interfaces;
using DependencyModules.xUnit.Impl;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.v3;

namespace Hardened.Web.Testing;

[AttributeUsage(AttributeTargets.Assembly)]
public class WebTestingAttribute : Attribute, ITestStartupAttribute {
    public void SetupServiceCollection(IXunitTestMethod testMethod, IServiceCollection serviceCollection) {
        var declaringType = testMethod.Method.DeclaringType!;
        serviceCollection.AddTransient<ITestWebApp>(sp => {
            var loggerType = typeof(ILogger<>).MakeGenericType(declaringType);
            var logger = (ILogger)sp.GetRequiredService(loggerType);
            var appRoot = sp.GetRequiredService<IApplicationRoot>();
            return new TestWebApp(appRoot, logger);
        });
    }

    public async Task StartupAsync(IXunitTestMethod testMethod, IServiceProvider serviceProvider) {
        var entryPoint = testMethod.Method.GetTestAttribute<HardenedTestEntryPointAttribute>();

        // Run registered startup services (CORS, filters, etc.)
        foreach (var startupService in serviceProvider.GetServices<IStartupService>()) {
            await startupService.Startup(serviceProvider);
        }

        if (entryPoint != null && !typeof(IApplicationRoot).IsAssignableFrom(entryPoint.EntryPoint)) {
            var handler = serviceProvider.GetRequiredService<IWebExecutionHandlerService>();
            var middleware = serviceProvider.GetRequiredService<IMiddlewareService>();
            middleware.Use(_ => handler);
        }
    }
}
