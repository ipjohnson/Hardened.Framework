using System.Reflection;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.Web.Testing;

[AttributeUsage(AttributeTargets.Assembly)]
public class WebTestingAttribute : Attribute, IHardenedParameterProviderAttribute, IHardenedTestStartupAttribute {
    public void RegisterDependencies(AttributeCollection attributeCollection, MethodInfo methodInfo,
        ParameterInfo? parameterInfo,
        IEnvironment environment, IServiceCollection serviceCollection) { }

    public object? ProvideParameterValue(MethodInfo methodInfo,
        ParameterInfo parameterInfo,
        IApplicationRoot applicationRoot) {
        if (parameterInfo.ParameterType == typeof(ITestWebApp)) {
            var logger = (ILogger)applicationRoot.Provider.GetService(
                typeof(ILogger<>).MakeGenericType(methodInfo.DeclaringType!))!;

            return new TestWebApp(applicationRoot, logger);
        }

        return null;
    }

    public Task Startup(AttributeCollection attributeCollection, MethodInfo methodInfo, IEnvironment environment,
        IServiceProvider serviceProvider) {
        var entryPoint = attributeCollection.GetAttribute<HardenedTestEntryPointAttribute>()!;

        if (!typeof(IApplicationRoot).IsAssignableFrom(entryPoint.EntryPoint)) {
            var handler = serviceProvider.GetRequiredService<IWebExecutionHandlerService>();
            var middleware = serviceProvider.GetRequiredService<IMiddlewareService>();
            middleware.Use(_ => handler);
        }

        return Task.CompletedTask;
    }
}