using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing;
using Xunit.Sdk;

namespace Hardened.Function.Lambda.Testing
{
    public class LambdaAppIntegrationAttribute : DataAttribute
    {
        public override IEnumerable<object?[]> GetData(MethodInfo testMethod)
        {
            var lambdaEntryPoint = FindLambdaEntryPointType(testMethod);

            if (lambdaEntryPoint != null)
            {
                yield return XUnitHelper.GetData(testMethod, lambdaEntryPoint, ResolveParameterFromApp);
            }
        }

        private object? ResolveParameterFromApp(ParameterInfo parameterInfo, IApplicationRoot applicationRoot)
        {
            if (parameterInfo.ParameterType == applicationRoot.GetType())
            {
                return applicationRoot;
            }

            return applicationRoot.Provider.GetService(parameterInfo.ParameterType);
        }

        private Type? FindLambdaEntryPointType(MethodInfo testMethod)
        {
            foreach (var parameterInfo in testMethod.GetParameters())
            {
                if (parameterInfo.ParameterType.GetInterfaces().Any(
                        t => t == typeof(IApplicationRoot)))
                {
                    return parameterInfo.ParameterType;
                }
            }

            return null;
        }
    }
}
