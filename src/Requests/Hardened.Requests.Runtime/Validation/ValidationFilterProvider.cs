using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using ValidationModules;

namespace Hardened.Requests.Runtime.Validation;

/// <summary>
/// Attaches a <see cref="ValidationFilter{TValidated}"/> to a handler, through the metadata array
/// the handler already carries.
/// </summary>
/// <remarks>
/// <para>
/// <b>The filter is built once.</b> <see cref="GetFilters"/> is called from
/// <c>ExecutionHelper.CreateFilterArray</c>, which runs in the handler's constructor - and the
/// routing table builds each handler once, from the root provider, with <c>??=</c>. Only the
/// returned <see cref="RequestFilterInfo.FilterFunc"/> runs per request, so returning a captured
/// instance is what makes this construct-once rather than rebuild-per-request.
/// </para>
/// <para>
/// It is not an attribute, deliberately. Generated validators are static singletons and this wants
/// to be handed one, but an attribute argument has to be a compile-time constant - a field read is
/// not one. The metadata array is <c>object[]</c> and <c>ExecutionHelper.GetFilterInfo</c> only
/// filters it for <see cref="IRequestFilterProvider"/>, so an ordinary object works and an ordinary
/// object can take a constructor argument.
/// </para>
/// </remarks>
public sealed class ValidationFilterProvider<TValidated> : IRequestFilterProvider
    where TValidated : class {
    private readonly RequestFilterInfo _filterInfo;

    public ValidationFilterProvider(IValidatorFor<TValidated> validator) {
        var filter = new ValidationFilter<TValidated>(validator);

        _filterInfo = new RequestFilterInfo(_ => filter, FilterOrder.Validation);
    }

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        yield return _filterInfo;
    }
}
