using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Function.Lambda.Runtime.Impl
{
    public interface ILambdaHandlerPackage
    {
        IExecutionRequestHandler? GetFunctionHandler(IServiceProvider serviceProvider, ILambdaContext context);
    }
}
