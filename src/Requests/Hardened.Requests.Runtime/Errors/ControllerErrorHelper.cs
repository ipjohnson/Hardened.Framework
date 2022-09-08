using System.ComponentModel.DataAnnotations;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Errors
{
    public static class ControllerErrorHelper
    {
        public static async Task HandleException(IExecutionContext context, Exception exception)
        {
            context.Response.ExceptionValue = exception;
        }
    }
}
