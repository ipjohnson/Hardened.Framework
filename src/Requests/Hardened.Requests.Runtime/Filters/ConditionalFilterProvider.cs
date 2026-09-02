using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// Another provider, asked only about the handlers a predicate admits.
/// </summary>
/// <remarks>
/// <para>
/// The predicate is the reason this exists rather than registering the inner provider directly.
/// Applied to one handler, an attribute is read beside the code it guards and its author can reason
/// about that handler; applied to every handler in the application, nobody read anything, so the
/// registration has to say what it is willing to cover.
/// </para>
/// <para>
/// It wraps a provider rather than going through
/// <see cref="IGlobalFilterRegistry.RegisterFilter(Func{IExecutionRequestHandlerInfo, RequestFilterInfo})"/>,
/// which takes a function returning one nullable filter - so a provider yielding two would lose
/// everything past the first.
/// </para>
/// </remarks>
public class ConditionalFilterProvider : IRequestFilterProvider {
    private readonly IRequestFilterProvider _provider;
    private readonly Func<IExecutionRequestHandlerInfo, bool> _when;

    public ConditionalFilterProvider(
        IRequestFilterProvider provider, Func<IExecutionRequestHandlerInfo, bool> when) {
        _provider = provider;
        _when = when;
    }

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) =>
        _when(handlerInfo) ? _provider.GetFilters(handlerInfo) : [];
}
