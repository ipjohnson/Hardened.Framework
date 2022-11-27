using Amazon.Lambda.Core;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Amz.Function.Lambda.Runtime.Impl;

/// <summary>
/// Interface implemented by source generator to expose lambda invoke
/// </summary>
public interface ILambdaHandlerPackage
{
    /// <summary>
    /// Get handler based on lambda context
    /// </summary>
    /// <param name="serviceProvider"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    IExecutionRequestHandler? GetFunctionHandler(IServiceProvider serviceProvider, ILambdaContext context);
}