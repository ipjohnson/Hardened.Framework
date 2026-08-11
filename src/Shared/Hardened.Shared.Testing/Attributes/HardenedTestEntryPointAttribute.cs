using System.Reflection;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Testing.Impl;
using Hardened.Shared.Testing.Logging;
using Hardened.Shared.Testing.Utilties;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit.v3;

namespace Hardened.Shared.Testing.Attributes;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public class HardenedTestEntryPointAttribute
    : Attribute, IDependencyModuleProvider, ITestServiceSetupAttribute, ITestStartupAttribute {
    public HardenedTestEntryPointAttribute(Type entryPoint) {
        EntryPoint = entryPoint;
    }

    public Type EntryPoint { get; }

    public IDependencyModule GetModule() {
        return (IDependencyModule)Activator.CreateInstance(EntryPoint)!;
    }

    public void SetupServiceCollection(ITestMethodContext testMethod, IServiceCollection serviceCollection) {
        var methodInfo = testMethod.Method;
        var attributeCollection = AttributeCollection.FromMethodInfo(methodInfo);
        var environment = BuildEnvironment(attributeCollection, methodInfo);

        serviceCollection.AddLogging();
        serviceCollection.AddSingleton<IHardenedEnvironment>(environment);
        serviceCollection.AddSingleton<IApplicationRoot>(sp => new ServiceProviderApplicationRoot(sp));
        serviceCollection.AddSingleton<ITestContext>(sp => {
            var loggerType = typeof(ILogger<>).MakeGenericType(methodInfo.DeclaringType!);
            var logger = (ILogger)sp.GetRequiredService(loggerType);
            return new TestContext(
                sp.GetRequiredService<TestCancellationToken>().Token,
                logger);
        });
        serviceCollection.AddSingleton(new TestCancellationToken(CancellationToken.None));

        foreach (var registrationAttribute in attributeCollection.GetAttributes<IHardenedTestDependencyRegistrationAttribute>()) {
            registrationAttribute.RegisterDependencies(attributeCollection, methodInfo, environment, serviceCollection);
        }

        foreach (var parameterProviderAttribute in attributeCollection.GetAttributes<IHardenedParameterProviderAttribute>()) {
            parameterProviderAttribute.RegisterDependencies(attributeCollection, methodInfo, null, environment, serviceCollection);
        }

        var appConfig = new AppConfig();
        foreach (var configAttribute in attributeCollection.GetAttributes<IHardenedTestConfigurationAttribute>()) {
            configAttribute.Configure(attributeCollection, methodInfo, environment, appConfig);
        }
        serviceCollection.AddSingleton<IConfigurationPackage>(appConfig);

        serviceCollection.RemoveAll<ILoggerProvider>();
        serviceCollection.AddSingleton<ILoggerProvider, XunitLoggerProvider>();
    }

    public async Task StartupAsync(ITestMethodContext testMethod, IServiceProvider serviceProvider) {
        ApplicationLogic.StartWithWait(serviceProvider, null, 15);

        var methodInfo = testMethod.Method;
        var attributeCollection = AttributeCollection.FromMethodInfo(methodInfo);

        foreach (var startupAttribute in attributeCollection.GetAttributes<IHardenedTestStartupAttribute>().OrderBy(a => a.Order)) {
            await startupAttribute.Startup(attributeCollection, methodInfo,
                serviceProvider.GetRequiredService<IHardenedEnvironment>(), serviceProvider);
        }
    }

    private static IHardenedEnvironment BuildEnvironment(AttributeCollection attributeCollection, MethodInfo methodInfo) {
        var environmentName = attributeCollection.GetAttribute<EnvironmentNameAttribute>()?.Name ?? "test";
        var environmentValueAttributes = attributeCollection.GetAttributes<EnvironmentValueAttribute>();
        var configAttributes = attributeCollection.GetAttributes<IHardenedTestEnvironmentAttribute>();

        var environmentDictionary = new Dictionary<string, object>();

        foreach (var attr in environmentValueAttributes) {
            environmentDictionary[attr.Variable] = attr.Value;
        }

        foreach (var configAttribute in configAttributes) {
            configAttribute.ConfigureEnvironment(attributeCollection, methodInfo, environmentName, environmentDictionary);
        }

        return new TestEnvironment(environmentName, environmentDictionary);
    }
}
