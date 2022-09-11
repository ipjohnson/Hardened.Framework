using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Sdk;

namespace Hardened.Function.Lambda.Testing
{
    public class LambdaAppIntegrationAttribute : HardenedIntegrationAttribute
    {
        protected override IApplicationRoot? CreateApplicationInstance(MethodInfo methodInfo, IEnvironment environment, Type applicationType,
            Action<IServiceCollection> applyMethod)
        {
            var applicationParameter = methodInfo.GetParameters().FirstOrDefault(
                p => p.ParameterType.GetInterfaces().Any(t => t == typeof(IApplicationRoot)));

            if (applicationParameter == null)
            {
                return CreateTestApplicationInstance(environment, applicationType, applyMethod);
            }

            return base.CreateApplicationInstance(methodInfo, environment, applicationParameter.ParameterType, applyMethod);
        }

        protected override object? ProcessParameter(MethodInfo methodInfo, IApplicationRoot applicationInstance, ParameterInfo parameter)
        {
            if (applicationInstance.GetType() == parameter.ParameterType)
            {
                return applicationInstance;
            }

            return base.ProcessParameter(methodInfo, applicationInstance, parameter);
        }
        //public override IEnumerable<object?[]> GetData(MethodInfo testMethod)
        //{
        //    var lambdaEntryPoint = FindLambdaEntryPointType(testMethod);

        //    if (lambdaEntryPoint != null)
        //    {
        //        yield return XUnitHelper.GetData(testMethod, lambdaEntryPoint, ResolveParameterFromApp);
        //    }
        //}

        //private object? ResolveParameterFromApp(ParameterInfo parameterInfo, IApplicationRoot applicationRoot)
        //{
        //    if (parameterInfo.ParameterType == applicationRoot.GetType())
        //    {
        //        return applicationRoot;
        //    }

        //    return applicationRoot.Provider.GetService(parameterInfo.ParameterType);
        //}

        //private Type? FindLambdaEntryPointType(MethodInfo testMethod)
        //{
        //    foreach (var parameterInfo in testMethod.GetParameters())
        //    {
        //        if (parameterInfo.ParameterType.GetInterfaces().Any(
        //                t => t == typeof(IApplicationRoot)))
        //        {
        //            return parameterInfo.ParameterType;
        //        }
        //    }

        //    return null;
        //}
    }
}
