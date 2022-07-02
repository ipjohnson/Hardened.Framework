namespace Hardened.Requests.Abstract.Execution
{
    public delegate Task DefaultOutputFunc(IExecutionContext executionContext);

    /// <summary>
    /// Object that holds all pertinent information for executing a request
    /// </summary>
    public interface IExecutionContext : ICloneable
    {
        /// <summary>
        /// Root service provider for the application
        /// </summary>
        IServiceProvider RootServiceProvider { get; }

        /// <summary>
        /// Service provider that is created/used for the life of the request
        /// </summary>
        IServiceProvider RequestServices { get; }

        /// <summary>
        /// Request parameters
        /// </summary>
        IExecutionRequest Request { get; }

        /// <summary>
        /// Response output
        /// </summary>
        IExecutionResponse Response { get; }

        /// <summary>
        /// Handler for the call, will be null for middleware handlers
        /// </summary>
        object? HandlerInstance { get; set; }

        /// <summary>
        /// Get information about the 
        /// </summary>
        IExecutionRequestHandlerInfo? HandlerInfo { get; set; }

        /// <summary>
        /// Default output function, used to assign template
        /// </summary>
        DefaultOutputFunc? DefaultOutput { get; set; }
    }
}
