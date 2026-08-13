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
/// The hand-written route. Generators attach validation themselves - they know the type, they have
/// the validator, and they emit a <see cref="ValidationFilterProvider{TValidated}"/> straight into
/// the handler's metadata - so this is for a handler the generator does not cover, or one whose
/// validated shape the author wants to name explicitly.
/// </para>
/// <para>
/// It resolves the validator from the container rather than being handed one, because an attribute
/// argument has to be a compile-time constant and a static field read is not. That costs a lookup on
/// first use; the generated route pays nothing.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ValidateAttribute<TValidated> : Attribute, IRequestFilterProvider
    where TValidated : class {

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        // Built on the first request rather than here: GetFilters runs in the handler's constructor,
        // which has no service provider. The lambda has the context, and the filter it builds is
        // kept - so this is once per handler, not once per request.
        ValidationFilter<TValidated>? filter = null;

        yield return new RequestFilterInfo(
            context => filter ??= new ValidationFilter<TValidated>(
                context.RequestServices.GetRequiredService<IValidatorFor<TValidated>>()),
            FilterOrder.Validation);
    }
}
