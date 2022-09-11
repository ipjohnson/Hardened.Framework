using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Xunit.Sdk;

namespace Hardened.Shared.Testing
{
    public class HardenedIntegrationAttribute : DataAttribute
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

        protected virtual object[] GetMethodArguments(MethodInfo methodInfo, TestLibraryAttribute testLibraryAttribute)
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

            InitApplication(methodInfo, applicationInstance);

            return GetParameterValues(methodInfo, applicationInstance);
        }

        protected virtual void InitApplication(MethodInfo methodInfo, IApplicationRoot applicationInstance)
        {

        }

        protected virtual object[] GetMethodArguments(MethodInfo methodInfo, TestApplicationAttribute bootstrap)
        {
            var providerList = GetTestServiceProviders(methodInfo);

            var configValueAttributes = methodInfo.GetAttributes<AppConfigAmendAttribute>().ToList();
            var testExposeAttributes = methodInfo.GetAttributes<ITestExposeAttribute>().ToList();

            var applicationInstance = CreateApplicationInstance(
                methodInfo,
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

        protected virtual List<ITestServiceProvider> GetTestServiceProviders(MethodInfo methodInfo)
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

        protected virtual void ConfigureStaticConfigMethods(MethodInfo methodInfo, IServiceCollection collection)
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

        protected virtual IEnvironment GetEnvironment(MethodInfo methodInfo)
        {
            var name = methodInfo.GetAttribute<EnvironmentNameAttribute>()?.Name ?? "test";

            var variables = new Dictionary<string, object>();

            foreach (var environmentValueAttribute in methodInfo.GetAttributes<EnvironmentValueAttribute>())
            {
                variables[environmentValueAttribute.Variable] = environmentValueAttribute.Value;
            }

            return new TestEnvironment(name, variables);
        }

        protected virtual object[] GetParameterValues(MethodInfo methodInfo, IApplicationRoot applicationInstance)
        {
            var parameters = methodInfo.GetParameters();
            var returnArray = new object[parameters.Length];

            for (var i = 0; i < returnArray.Length; i++)
            {
                var parameter = parameters[i];

                var returnValue = ProcessParameter(methodInfo, applicationInstance, parameter);

                if (returnValue == null)
                {
                    returnArray[i] = applicationInstance.Provider.GetRequiredService(parameter.ParameterType);
                }
                else
                {
                    returnArray[i] = returnValue;
                }
            }

            return returnArray;
        }

        protected virtual object? ProcessParameter(MethodInfo methodInfo, IApplicationRoot applicationInstance,
            ParameterInfo parameter)
        {
            return null;
        }

        protected virtual IApplicationRoot CreateTestApplicationInstance(IEnvironment environment, Type libraryType, Action<IServiceCollection> overrides)
        {
            var module = Activator.CreateInstance(libraryType) as IApplicationModule;

            if (module == null)
            {
                throw new Exception("Could not create module for testing");
            }

            var application = new TestApplication(module, "", environment, overrides);
            
            return application;
        }

        protected virtual IApplicationRoot? CreateApplicationInstance(MethodInfo methodInfo, IEnvironment environment,
            Type applicationType, Action<IServiceCollection> applyMethod)
        {
            return Activator.CreateInstance(
                applicationType, environment, applyMethod) as IApplicationRoot;
        }
    }
}
