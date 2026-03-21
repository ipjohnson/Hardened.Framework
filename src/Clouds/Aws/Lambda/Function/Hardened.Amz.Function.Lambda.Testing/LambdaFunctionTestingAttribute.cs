using DependencyModules.xUnit.Attributes.Interfaces;
using Hardened.Amz.Function.Lambda.Runtime.Impl;
using Hardened.Requests.Abstract.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Xunit.v3;

namespace Hardened.Amz.Function.Lambda.Testing;

[AttributeUsage(AttributeTargets.Assembly)]
public class LambdaFunctionTestingAttribute : Attribute, ITestStartupAttribute {
    public void SetupServiceCollection(IXunitTestMethod testMethod, IServiceCollection serviceCollection) {
        serviceCollection.AddTransient<LambdaTestApp>();
        serviceCollection.AddSingleton<ILambdaInvokeFilterProvider, TestLambdaInvokeFilterProvider>();
    }

    public Task StartupAsync(IXunitTestMethod testMethod, IServiceProvider serviceProvider) {
        var middleware = serviceProvider.GetRequiredService<IMiddlewareService>();
        var filterProvider = serviceProvider.GetRequiredService<ILambdaInvokeFilterProvider>();
        middleware.Use(_ => filterProvider.ProvideFilter(serviceProvider));

        return Task.CompletedTask;
    }
}
