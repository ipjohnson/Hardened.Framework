using System.Reflection;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Sdk;

namespace Hardened.Web.Testing
{
    public class WebIntegrationAttribute : DataAttribute
    {
        public override IEnumerable<object[]> GetData(MethodInfo testMethod)
        {
            var bootstrap = testMethod.GetAttribute<TestApplicationAttribute>();

            if (bootstrap != null)
            {
                yield return GetMethodArguments(testMethod, bootstrap);
            }

            var libraryAttribute = testMethod.GetAttribute<TestLibraryAttribute>();

            if (libraryAttribute != null)
            {
                yield return GetMethodArguments(testMethod, libraryAttribute);
            }
        }


        private static object[] GetMethodArguments(MethodInfo methodInfo, TestLibraryAttribute testLibraryAttribute)
        {
            var providerList = GetTestServiceProviders(methodInfo);

            var configValueAttributes = methodInfo.GetAttributes<AppConfigAmendAttribute>().ToList();
            var testExposeAttributes = methodInfo.GetAttributes<ITestExposeAttribute>().ToList();


            var applicationInstance = CreateTestApplicationInstance(
                GetEnvironment(methodInfo),
                testLibraryAttribute.LibraryType,
                collection =>
                {
                    providerList.ForEach(provider => provider.RegisterService(collection));
                    testExposeAttributes.ForEach(exposeAction => exposeAction.ExposeDependencies(methodInfo, collection));
                    collection.AddSingleton(new TestConfigurationPackage(configValueAttributes));
                    ConfigureStaticConfigMethods(methodInfo, collection);
                });

            return GetParameterValues(methodInfo, applicationInstance);
        }

        private static object[] GetMethodArguments(MethodInfo methodInfo, TestApplicationAttribute bootstrap)
        {
            var providerList = GetTestServiceProviders(methodInfo);

            var configValueAttributes = methodInfo.GetAttributes<AppConfigAmendAttribute>().ToList();
            var testExposeAttributes = methodInfo.GetAttributes<ITestExposeAttribute>().ToList();
            
            var applicationInstance = CreateApplicationInstance(
                GetEnvironment(methodInfo),
                bootstrap.Application,
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

            return GetParameterValues(methodInfo, applicationInstance);
        }

        private static List<ITestServiceProvider> GetTestServiceProviders(MethodInfo methodInfo)
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

            return providerList;
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

                configureMethod.Invoke(null, new object[]{ appConfig });

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

        private static object[] GetParameterValues(MethodInfo methodInfo, IApplicationRoot applicationInstance)
        {
            var parameters = methodInfo.GetParameters();
            var returnArray = new object[parameters.Length];

            for (var i = 0; i < returnArray.Length; i++)
            {
                var parameter = parameters[i];

                if (parameter.ParameterType == typeof(ITestWebApp))
                {
                    returnArray[i] = new TestWebApp(applicationInstance);
                }
                else
                {
                    returnArray[i] = applicationInstance.Provider.GetRequiredService(parameter.ParameterType);
                }
            }

            return returnArray;
        }

        private static IApplicationRoot CreateTestApplicationInstance(IEnvironment environment, Type libraryType, Action<IServiceCollection> overrides)
        {
            var module = Activator.CreateInstance(libraryType) as IApplicationModule;

            if (module == null)
            {
                throw new Exception("Could not create module for testing");
            }

            return new TestApplication(module, "", environment, overrides);
        }

        private static IApplicationRoot? CreateApplicationInstance(IEnvironment environment, Type applicationType, Action<IServiceCollection> applyMethod)
        {
            return Activator.CreateInstance(
                applicationType, environment, applyMethod) as IApplicationRoot;
        }
    }
}
