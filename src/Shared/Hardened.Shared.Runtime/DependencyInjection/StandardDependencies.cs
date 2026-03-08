using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Runtime.DependencyInjection;

public sealed class StandardDependencies {
    public static void ProcessModuleProviders(
        IHardenedEnvironment environment,
        IServiceCollection serviceCollection,
        params object[] otherModules) {
        
        foreach (var objectModule in otherModules) {
            if (objectModule is IApplicationModuleProvider provider) {
                foreach (var applicationModule in provider.ProvideModules()) {
                    applicationModule.ConfigureModule(environment, serviceCollection);
                }
            } else if (objectModule is IDependencyModuleProvider dependencyModuleProvider) {
                dependencyModuleProvider.GetModule().PopulateServiceCollection(serviceCollection);
            } else if (objectModule is IDependencyModule dependencyModule) {
                dependencyModule.PopulateServiceCollection(serviceCollection);
            }
        }
    }
    
    public static void ProcessModules(
        IHardenedEnvironment environment, 
        IServiceCollection serviceCollection,
        IEnumerable<IApplicationModule> applicationModules) {
        
        foreach (var applicationModule in applicationModules) {
            applicationModule.ConfigureModule(environment, serviceCollection);
        }
    }
}