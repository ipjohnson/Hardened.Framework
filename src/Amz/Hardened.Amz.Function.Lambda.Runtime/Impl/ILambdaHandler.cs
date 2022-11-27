using Amazon.Lambda.Core;
using Hardened.Shared.Runtime.Application;

namespace Hardened.Amz.Function.Lambda.Runtime.Impl;

/// <summary>
/// Interface that is implemented by hardened lambda app
/// used primarily during unit tests
/// </summary>
public interface ILambdaHandler : IApplicationRoot
{
    /// <summary>
    /// Invoke lambda by name
    /// </summary>
    /// <param name="input"></param>
    /// <param name="lambdaContext"></param>
    /// <returns></returns>
    Task<Stream> Invoke(Stream input, ILambdaContext lambdaContext);
}

/// <summary>
/// Lambda handler that is used during unit tests
/// </summary>
/// <typeparam name="TRequest"></typeparam>
/// <typeparam name="TResponse"></typeparam>
public interface ILambdaHandler<TRequest, TResponse> : ILambdaHandler
{

}

/// <summary>
/// Lambda handler interface used during unit tests
/// </summary>
/// <typeparam name="TResponse"></typeparam>
public interface ILambdaHandler<in TResponse> : ILambdaHandler
{

}