using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Microsoft.Extensions.DependencyInjection;
using ValidationModules;

namespace Hardened.Requests.Runtime.Validation;

/// <summary>
/// Validates a handler's bound parameters against <typeparamref name="TValidated"/>.
/// </summary>
/// <remarks>
/// <para>
/// One type for both routes. A generator puts an instance into the handler's metadata array -
/// <c>ExecutionHelper.GetFilterInfo</c> filters that array for <see cref="IRequestFilterProvider"/>,
/// and an attribute is an ordinary object - and an author can write it on a handler by hand. There
/// is nothing to distinguish the two cases, because both resolve their validators the same way.
/// </para>
/// <para>
/// <b>Resolved rather than handed in.</b> That is what lets a hand-written
/// <c>IValidatorFor&lt;T&gt;</c> registered for the same type run alongside the generated one and
/// merge, instead of one replacing the other. Generated validators are registered as singletons, so
/// the set is resolved once and kept.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ValidateAttribute<TValidated> : Attribute, IRequestFilterProvider
    where TValidated : class {

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        // Built on the first request rather than here: GetFilters runs in the handler's constructor,
        // which has no service provider. The filter it builds is kept, so this is once per handler.
        ValidationFilter<TValidated>? filter = null;

        yield return new RequestFilterInfo(
            context => filter ??= new ValidationFilter<TValidated>(Resolve(context)),
            FilterOrder.Validation);
    }

    /// <summary>
    /// Every validator registered for <typeparamref name="TValidated"/>.
    /// </summary>
    /// <remarks>
    /// Throws when there are none. This filter is only ever attached because something declared
    /// constraints, so an empty set means registration did not happen - and treating that as
    /// "nothing to check" would turn validation off silently on a build that is otherwise fine.
    /// </remarks>
    private static IReadOnlyList<IValidatorFor<TValidated>> Resolve(IExecutionContext context) {
        var validators = context.RequestServices
            .GetServices<IValidatorFor<TValidated>>()
            .ToArray();

        if (validators.Length == 0) {
            throw new InvalidOperationException(
                $"No IValidatorFor<{typeof(TValidated).Name}> is registered, but the handler declares " +
                "constraints. The generated validators register themselves through the application's " +
                "entry point, so this usually means the entry point was not the one built against.");
        }

        return validators;
    }
}
