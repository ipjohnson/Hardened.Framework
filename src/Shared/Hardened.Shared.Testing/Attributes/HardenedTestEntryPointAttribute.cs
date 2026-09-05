using System.Reflection;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Testing.Impl;
using Hardened.Shared.Testing.Utilties;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Hardened.Shared.Testing.Attributes;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public class HardenedTestEntryPointAttribute
    : Attribute, IDependencyModuleProvider, IModuleEnvironmentProvider,
      ITestServiceSetupAttribute, ITestStartupAttribute {
    public HardenedTestEntryPointAttribute(Type entryPoint) {
        EntryPoint = entryPoint;
    }

    public Type EntryPoint { get; }

    public IDependencyModule GetModule() {
        return (IDependencyModule)Activator.CreateInstance(EntryPoint)!;
    }

    /// <summary>
    /// The environment the runner registers before it applies any module.
    /// </summary>
    /// <remarks>
    /// This is what puts an <c>[IfEnvironment]</c>-gated registration within a test's reach.
    /// <see cref="SetupServiceCollection"/> registers the same environment, but the runner calls
    /// it after the modules have been applied - by design, so a test registration beats an
    /// application one - which is after every module condition has already been decided. The
    /// runner asks this first, so the conditions are decided against the environment the test
    /// declares rather than a process default.
    /// </remarks>
    public IModuleEnvironment? ProvideEnvironment(MethodInfo testMethod) {
        return BuildEnvironment(AttributeCollection.FromMethodInfo(testMethod), testMethod);
    }

    public void SetupServiceCollection(ITestMethodContext testMethod, IServiceCollection serviceCollection) {
        var methodInfo = testMethod.Method;
        var attributeCollection = AttributeCollection.FromMethodInfo(methodInfo);

        // The instance the runner seeded for the module pass, when it did - the same one, so the
        // environment a module condition read and the one a service resolves are one object.
        var environment = SeededEnvironment(serviceCollection)
                          ?? BuildEnvironment(attributeCollection, methodInfo);

        serviceCollection.AddLogging();

        // AddHardenedEnvironment rather than AddSingleton, which registers only IHardenedEnvironment
        // and leaves IModuleEnvironment unanswered - so the module system read Production however the
        // test was annotated, and [IfEnvironment], a template-default feature, could not be exercised
        // from a test at all. TestApplication has always done it this way; this is the path that had
        // not caught up.
        serviceCollection.AddHardenedEnvironment(environment);
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

        // The runner package's provider, which writes where the runner shows a test's output. A
        // container built with no runner package loaded - this attribute driven directly from a
        // test of its own - keeps no provider, rather than a console one nobody reads.
        if (CurrentTest.Provider is { } runner) {
            serviceCollection.AddSingleton<ILoggerProvider>(_ => runner.CreateLoggerProvider());
        }
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

    /// <summary>
    /// The environment <see cref="ProvideEnvironment"/> already handed the runner, or null on a
    /// path that never called it - the setup pipeline driven directly, or an older runner.
    /// </summary>
    private static IHardenedEnvironment? SeededEnvironment(IServiceCollection serviceCollection) {
        foreach (var descriptor in serviceCollection) {
            if (descriptor.ServiceType == typeof(IModuleEnvironment) &&
                descriptor.ImplementationInstance is IHardenedEnvironment seeded) {
                return seeded;
            }
        }

        return null;
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
