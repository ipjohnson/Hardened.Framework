using System.Reflection;
using Hardened.Shared.Runtime.Application;
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

            if (bootstrap == null)
            {
                yield break;
            }

            yield return GetMethodArguments(testMethod, bootstrap);
        }

        private static object[] GetMethodArguments(MethodInfo methodInfo, TestApplicationAttribute bootstrap)
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

            var testExposeAttributes = methodInfo.GetAttributes<ITestExposeAttribute>().ToList();

            var applicationInstance = CreateApplicationInstance(bootstrap.Application, collection =>
            {
                providerList.ForEach(provider => provider.RegisterService(collection));
                testExposeAttributes.ForEach(exposeAction => exposeAction.ExposeDependencies(methodInfo, collection));
            });

            if (applicationInstance == null)
            {
                throw new Exception("BootstrapApplication must be IApplicationRoot");
            }

            return GetParameterValues(methodInfo, applicationInstance);
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

        private static IApplicationRoot? CreateApplicationInstance(Type applicationType, Action<IServiceCollection> applyMethod)
        {
            return Activator.CreateInstance(applicationType, applyMethod) as IApplicationRoot;
        }
    }
}
