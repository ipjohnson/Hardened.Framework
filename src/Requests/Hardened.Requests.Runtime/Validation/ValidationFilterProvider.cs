using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Microsoft.Extensions.DependencyInjection;
using ValidationModules;

namespace Hardened.Requests.Runtime.Validation;

/// <summary>
/// Attaches the validator for a handler's generated <c>Parameters</c> class.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="ValidateAttribute{TValidated}"/>, and what separates them is the
/// validated type. The attribute is written by a consumer against a type a consumer can see; this
/// one is emitted against a handler's nested <c>Parameters</c> class, which is generated and named
/// with a computed suffix. Both resolve their validators from the container.
/// </para>
/// <para>
/// This class used to take the validator as a constructor argument, and that was deliberate: naming
/// <c>SomeValidator.Instance</c> in generated source does not compile unless the validator was
/// emitted, so a handler could not carry a filter whose validator was missing. That guarantee is
/// gone, and it had to go - a generated validator now takes the validators for its nested types as
/// constructor parameters, so there is no instance to hand in. A static singleton cannot have
/// anything injected into it, which is the reason the pattern was removed upstream rather than
/// worked around here.
/// </para>
/// <para>
/// What replaces the guarantee is that <see cref="Resolve"/> throws on an empty set. The provider is
/// only ever emitted alongside a validator registration, by the same generator run, so an empty set
/// means the application was wired against a different entry point - which is worth failing on
/// loudly. The failure moves from build time to first request; it does not become silent.
/// </para>
/// <para>
/// Still built once. <c>GetFilters</c> runs from the handler's constructor, which has no service
/// provider, so the filter is built on the first request and kept - the same shape
/// <see cref="ValidateAttribute{TValidated}"/> uses. The routing table caches handlers behind
/// <c>??=</c>, so this is once per handler per process and the steady-state request path does no
/// container work.
/// </para>
/// </remarks>
public sealed class ValidationFilterProvider<TValidated> : IRequestFilterProvider
    where TValidated : class {

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        ValidationFilter<TValidated>? filter = null;

        yield return new RequestFilterInfo(
            context => filter ??= new ValidationFilter<TValidated>(Resolve(context)),
            FilterOrder.Validation);
    }

    /// <summary>
    /// Every validator registered for <typeparamref name="TValidated"/>.
    /// </summary>
    /// <remarks>
    /// Throws when there are none, for the reason in the type's own remarks: this filter is only
    /// attached because constraints were declared, so an empty set is a wiring fault rather than an
    /// absence of work.
    /// </remarks>
    private static IReadOnlyList<IValidatorFor<TValidated>> Resolve(IExecutionContext context) {
        var validators = context.RequestServices
            .GetServices<IValidatorFor<TValidated>>()
            .ToArray();

        if (validators.Length == 0) {
            throw new InvalidOperationException(
                $"No IValidatorFor<{typeof(TValidated).Name}> is registered, but the handler declares " +
                "constraints. Check the build log for a source generator failure first - a generator " +
                "that throws is reported as warning CS8785 and leaves the build green having emitted " +
                "no validators at all. Failing that, the generated validators register themselves " +
                "through the application's entry point, so the entry point may not be the one built " +
                "against.");
        }

        return validators;
    }
}
