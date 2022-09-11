using System.Reflection;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Testing;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Sdk;

namespace Hardened.Web.Testing
{
    public class WebIntegrationAttribute : HardenedIntegrationAttribute
    {
        protected override object? ProcessParameter(MethodInfo methodInfo, IApplicationRoot applicationInstance,
            ParameterInfo parameter)
        {
            if (parameter.ParameterType == typeof(ITestWebApp))
            {
                return new TestWebApp(applicationInstance);
            }

            return null;
        }
        
        protected override void InitApplication(MethodInfo methodInfo, IApplicationRoot application)
        {
            var handler = application.Provider.GetRequiredService<IWebExecutionHandlerService>();
            var middleware = application.Provider.GetRequiredService<IMiddlewareService>();
            middleware.Use(_ => handler);
        }
    }
}
