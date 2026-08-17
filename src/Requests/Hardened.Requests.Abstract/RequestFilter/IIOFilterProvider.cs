using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Abstract.RequestFilter;

public interface IIOFilterProvider {
    IExecutionFilter ProvideFilter(
        IExecutionRequestHandlerInfo handlerInfo,
        Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest);

    IExecutionFilter ProvideAsyncEnumerableFilter<TItem>(
        IExecutionRequestHandlerInfo handlerInfo,
        Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest);

    /// <summary>
    /// The streamed filter, framed the way the handler asked for.
    /// </summary>
    /// <param name="framing">
    /// What goes around each item, or null for newline-delimited JSON.
    /// </param>
    /// <remarks>
    /// A default implementation delegating to the two-argument overload, so an existing provider
    /// outside this repository keeps compiling and keeps answering as it did. One that wants to
    /// honour the framing overrides it - which the shipped provider does.
    /// </remarks>
    IExecutionFilter ProvideAsyncEnumerableFilter<TItem>(
        IExecutionRequestHandlerInfo handlerInfo,
        Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest,
        IStreamFraming? framing) =>
        ProvideAsyncEnumerableFilter<TItem>(handlerInfo, deserializeRequest);
}