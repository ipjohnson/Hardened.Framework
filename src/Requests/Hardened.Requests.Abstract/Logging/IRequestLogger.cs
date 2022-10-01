using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Logging;

/// <summary>
/// Logger called to record requests
/// </summary>
public interface IRequestLogger
{
    /// <summary>
    /// Called at the beginning of the request
    /// </summary>
    /// <param name="context"></param>
    void RequestBegin(IExecutionContext context);

    /// <summary>
    /// Called once request has been mapped
    /// </summary>
    /// <param name="context"></param>
    void RequestMapped(IExecutionContext context);

    /// <summary>
    /// Called at the end of the request
    /// </summary>
    /// <param name="context"></param>
    void RequestEnd(IExecutionContext context);

    /// <summary>
    /// Called when the call can't bind input parameters
    /// </summary>
    /// <param name="context"></param>
    /// <param name="exp"></param>
    void RequestParameterBindFailed(IExecutionContext context, Exception? exp);

    /// <summary>
    /// Called when request throws an exception
    /// </summary>
    /// <param name="context"></param>
    /// <param name="exp"></param>
    void RequestFailed(IExecutionContext context, Exception exp);

    /// <summary>
    /// Called when no resource can be found
    /// </summary>
    /// <param name="context"></param>
    void ResourceNotFound(IExecutionContext context);
}