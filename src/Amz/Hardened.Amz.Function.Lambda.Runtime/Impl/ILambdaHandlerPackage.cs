using Amazon.Lambda.Core;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Amz.Function.Lambda.Runtime.Impl;

public interface ILambdaHandlerPackage
{
    IExecutionRequestHandler? GetFunctionHandler(IServiceProvider serviceProvider, ILambdaContext context);
}