using System.ComponentModel.DataAnnotations;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Errors
{
    public static class ControllerErrorHelper
    {
        public static async Task HandleException(IExecutionContext context, Exception exception)
        {
            context.RequestServices.GetRequiredService<IRequestLogger>().RequestFailed(context, exception);

            context.Response.ExceptionValue = exception;
        }
    }
}
