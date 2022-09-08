using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Testing
{
    public delegate object? ParameterResolutionFunc(ParameterInfo parameterInfo, IApplicationRoot applicationRoot);

    public static class XUnitHelper
    {
        public static object?[] GetData(MethodInfo methodInfo, Type applicationType, ParameterResolutionFunc parameterResolutionFunc)
        {
            var providerList = new List<ITestServiceProvider>();

            var serviceProviders = methodInfo.GetAttributes<ITestServiceProvider>();

            foreach (var testServiceProvider in serviceProviders)
            {
                testServiceProvider.InitializeProvider(methodInfo);

                providerList.Add(testServiceProvider);
            }

            foreach (var parameterInfo in methodInfo.GetParameters())
            {
                var parameterProviders = parameterInfo.GetAttributes<ITestServiceProvider>();

                foreach (var testServiceProvider in parameterProviders)
                {
                    testServiceProvider.InitializeProvider(parameterInfo);

                    providerList.Add(testServiceProvider);
                }
            }

            var configValueAttributes = methodInfo.GetAttributes<AppConfigAmendAttribute>().ToList();
            var testExposeAttributes = methodInfo.GetAttributes<ITestExposeAttribute>().ToList();

            var applicationInstance = CreateApplicationInstance(
                GetEnvironment(methodInfo),
                applicationType,
                collection =>
                {
                    providerList.ForEach(provider => provider.RegisterService(collection));
                    testExposeAttributes.ForEach(exposeAction => exposeAction.ExposeDependencies(methodInfo, collection));
                    collection.AddSingleton(new TestConfigurationPackage(configValueAttributes));
                    ConfigureStaticConfigMethods(methodInfo, collection);
                });

            if (applicationInstance == null)
            {
                throw new Exception("BootstrapApplication must be IApplicationRoot");
            }

            return GetParameterValues(methodInfo, applicationInstance, parameterResolutionFunc);
        }

        private static void ConfigureStaticConfigMethods(MethodInfo methodInfo, IServiceCollection collection)
        {
            var staticMethods =
                methodInfo.DeclaringType?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ??
                                       Array.Empty<MethodInfo>();

            var registerDependenciesMethod = staticMethods.FirstOrDefault(m => m.Name == "RegisterDependencies");

            if (registerDependenciesMethod != null)
            {
                registerDependenciesMethod.Invoke(null, new object[] { collection });
            }

            var configureMethod = staticMethods.FirstOrDefault(m => m.Name == "Configure");

            if (configureMethod != null)
            {
                var appConfig = new AppConfig();

                configureMethod.Invoke(null, new object[] { appConfig });

                collection.AddSingleton<IConfigurationPackage>(appConfig);
            }
        }

        private static IEnvironment GetEnvironment(MethodInfo methodInfo)
        {
            var name = methodInfo.GetAttribute<EnvironmentNameAttribute>()?.Name ?? "test";

            var variables = new Dictionary<string, object>();

            foreach (var environmentValueAttribute in methodInfo.GetAttributes<EnvironmentValueAttribute>())
            {
                variables[environmentValueAttribute.Variable] = environmentValueAttribute.Value;
            }

            return new TestEnvironment(name, variables);
        }

        private static object?[] GetParameterValues(MethodInfo methodInfo, IApplicationRoot applicationInstance,
            ParameterResolutionFunc parameterResolutionFunc)
        {
            var parameters = methodInfo.GetParameters();
            var returnArray = new object?[parameters.Length];

            for (var i = 0; i < returnArray.Length; i++)
            {
                returnArray[i] = parameterResolutionFunc(parameters[i], applicationInstance);
            }

            return returnArray;
        }

        private static IApplicationRoot? CreateApplicationInstance(IEnvironment environment, Type applicationType, Action<IServiceCollection> applyMethod)
        {
            return Activator.CreateInstance(
                applicationType, environment, applyMethod) as IApplicationRoot;
        }
    }
}
