using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Errors;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit.v3;

namespace Hardened.Web.Testing;

[AttributeUsage(AttributeTargets.Assembly)]
public class WebTestingAttribute : Attribute, ITestServiceSetupAttribute, ITestStartupAttribute {
    public void SetupServiceCollection(ITestMethodContext testMethod, IServiceCollection serviceCollection) {
        // The harness is a terminal host: there is nothing behind it to hand an unmatched request
        // to, so a path with no route is a 404 here, exactly as it is on Kestrel and on Lambda.
        //
        // It has to be stated rather than inherited, because the application under test names its
        // deployment runtime and that runtime's policy arrives with it. An application carrying
        // [AspNetCoreRuntime] registers AspNetResourceNotFoundHandler, which deliberately leaves
        // the status unset so UseHardened() can defer to the rest of the ASP.NET pipeline. Correct
        // there; wrong here, where deferring means answering nothing at all.
        //
        // Registration attributes run after the application's modules, which is what lets this win.
        serviceCollection.RemoveAll<IResourceNotFoundHandler>();
        serviceCollection.AddSingleton<IResourceNotFoundHandler, ResourceNotFoundHandler>();

        var declaringType = testMethod.Method.DeclaringType!;
        serviceCollection.AddTransient<ITestWebApp>(sp => {
            var loggerType = typeof(ILogger<>).MakeGenericType(declaringType);
            var logger = (ILogger)sp.GetRequiredService(loggerType);
            var appRoot = sp.GetRequiredService<IApplicationRoot>();
            return new TestWebApp(appRoot, logger);
        });
    }

    public async Task StartupAsync(ITestMethodContext testMethod, IServiceProvider serviceProvider) {
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
