namespace Hardened.Requests.Abstract.Execution
{
    public interface IExecutionChain
    {
        /// <summary>
        /// execute next action in chain
        /// </summary>
        /// <returns></returns>
        Task Next();

        /// <summary>
        /// Execution context
        /// </summary>
        IExecutionContext Context { get; }

        /// <summary>
        /// Copy execution chain so that it can be executed multiple times
        /// IExecutionContext will be copied as well if a new context is not provided
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        IExecutionChain Fork(IExecutionContext context);

        /// <summary>
        /// True if the chain is on it's last filter.
        /// </summary>
        bool IsLastFilter { get; }
    }
}
